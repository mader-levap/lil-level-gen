# Obstacle placer (`ProcObstaclePlacer`) - findings and plan

General processor that puts obstacles (conditions on edges) and solutions (POIs in nodes) on an
arbitrary graph, softlock-free. First user: `GraphBuilderSimple` (end of `RunProcessorsShaping()`).
`GraphBuilderLinear` keeps its own chain-based obstacle pass.

Status: **all phases done** (1-7), plus the stack limit added afterwards (sections 4.2 and 5.2). Measurements and the outcome of tuning are in section 5.1. Phases are in section 6.

Decisions taken:
- Chokepoints: cut search *prefers* chokepoint passages; a locked non-chokepoint passage keeps its flag.
- Secret passages: free to cut, get locked with the rest of the cut, small chance to stay open (bypass).

## 1. Findings from the code

- An obstacle = one new `Solution` POI + the same `LilCondition` (`ReqSolution`) on one or more edges.
  `Complexity` counts **obstacles (solutions)**, not conditioned edges.
- Solutions are never consumed (`GraphValidator`, `GraphFlood`: a collected POI opens every edge that
  requires it). So reachability is monotone: more keys/edges never hurt, as long as passages are two-way.
- Processors run in `AGraphBuilder.RunProcessorsShaping()` (private, shared by all builders):
  `ProcRelSecrets` -> `ProcNodeObjectives`. The placer goes last there, so it sees secret nodes
  (leaf, nested, two-passage through-routes, one-way `A -> S -> A` loops) and Goal/OptionalGoal POIs.
- One-way edges exist only as `A -> S -> A` secret detours today.
- `GraphFlood.Run(start, held, collectible, blockedPoi)` already answers "what is reachable without
  solution X" - exactly the softlock test. Reads `edge.Conditions` and `node.Pois` live, so one
  instance survives in-place edits.
- `GraphIndex` gives Id-ordered visit orders (determinism independent of list order).
- `Complexity` is at most about `2 * sqrt(Size)` (~21 at Size 110): any polynomial approach is cheap.
- `CnfGraphBuilderLinear.MaxConditionPerRel = 3` is the existing "max obstacles per passage".

## 2. Softlock rule (the one invariant)

> A new obstacle `k` with solution in node `n` is safe iff `n` is reachable from start **without**
> **holding `k`** (flood with `k` blocked) on the graph that already carries `k`'s conditions.

Proof sketch: before `k` the flood reached everything. With `k` blocked the flood still reaches `n`,
so the real flood picks `k` up; from then on every `k` condition is satisfied and the flood is a
superset of the old one. By induction the graph stays fully reachable after every obstacle.

Consequences: no rollback needed (lock edges, flood, pick solution node from the flood result);
correctness does not depend on how clever cut selection is; cuts may cross each other freely.

Extra conservatism for the solution flood (stronger than the validator):
- ignores **secret** edges and never picks **secret nodes** - progress never requires finding a secret;
- ignores **one-way** edges - the solution is reachable inside the two-way part, so the player can
  always walk back. One-way edges are never locked and never cross a cut (infinite capacity).

## 3. Alternatives considered

| Approach | Verdict |
|---|---|
| Random lock + random key, validate, retry | Correct, but no control over depth or bottlenecks. Rejected. |
| Key-level labelling (Metazelda style: node level = keys needed, edge gets all keys between levels) | Clean nested cuts, but one total order: depth capped at `3 * distance(start, exit)`, cannot express "key behind another lock elsewhere". Subsumed by the chosen design. |
| Bridge tree only (lock bridges on the start-exit path) | Perfect bottlenecks, but nothing to lock inside 2-edge-connected parts or with adjacent start/exit. Subsumed: min cut finds bridges as width-1 cuts. |
| **Target-driven min cuts + blocked-flood key placement** | Chosen. See below. |

## 4. Chosen algorithm

### 4.1 Building blocks

- **Min cut** between start `s` and a target node `T` (max-flow, Edmonds-Karp; flows are tiny).
  Edge capacities: one-way or already at `MaxConditionsPerEdge` -> infinite (uncuttable); secret -> 0
  (free); chokepoint -> `CostChokepoint`; non-chokepoint -> `CostOpen` (higher); plus `CostStack` per
  condition already on the edge (prefers fresh cuts over stacking, stacking still possible).
- **Candidates**: several cuts per target, by contracting the BFS ball of radius `r = 0, 1, 2...`
  around `T` (and around `s`) into the terminal before the flow. Gives cuts at different positions
  along the corridor. Deduplicate by edge set; reject cuts with more than `MaxEdgesPerObstacle`
  ordinary edges or infinite cost. Pick the cheapest, ties and near-ties broken by seeded roll.
- **Sides**: `A` = nodes reached from `s` over ordinary edges without the cut; the rest of the ordinary
  nodes are `B`. Secret nodes join `A` first (flood over secret edges), then `B`. Every edge between
  `A` and `B` gets the condition - except a *secret* edge may stay open with `ChanceSecretBypass`.
- **Solution placement**: flood with `k` blocked (section 2), weighted pick among reached nodes:
  lower weight for the start node and for nodes already holding solutions, bonus for leaves/dead
  ends and for distance from the locked passages. Candidates enumerated in Id order.

- **Parallel passages** (several edges between the same two nodes) are separate edges everywhere:
  the flow is indexed by edge, never by a node-pair matrix. Sides are per node, so all parallels
  between `A` and `B` are locked together; each counts toward cost and `MaxEdgesPerObstacle`; one
  saturated parallel makes the whole pair uncuttable. "Prefer chokepoints" is a summed per-edge
  cost, it never locks one parallel and leaves the other open. Candidates are deduplicated by edge set.
- **Dead end / leaf** (solution weights, side pass) means all passages lead to one other node,
  counted by distinct neighbour, not by edge count.

- **Secret bypass** (main pass only): after the solution is placed, one roll per secret passage
  of the cut with `ChanceSecretBypass` (default 0.25), in Id order; on a hit that passage loses
  the new condition. Not rolled when the cut holds secret passages only - something must stay
  locked. Never used by the side pass: opening the entry of a pocket would unlock the pocket.
  Rolled before the ordinary bypass.
- **Ordinary bypass** (main pass only): after the solution is placed, one roll per obstacle with
  `ChanceBypass` (default 0.01). On a hit, one ordinary passage of the cut is left without the
  condition - a locked shortcut with an open way around. Rules:
  - the cut must hold at least two ordinary passages (secret ones do not count), so at least one
    ordinary passage always stays locked;
  - a passage with a parallel passage in the same cut is never the one left open (an open door
    next to a locked one between the same two nodes would make the obstacle meaningless);
  - the open passage is picked by seeded roll among the eligible ones, in Id order; the chance
    roll itself is only made when an eligible passage exists;
  - rolled after solution placement, so the solution stays on the start side of the cut.
  Never used by the side pass: all passages of a pocket join one node pair.

Property: with both bypass chances 0, every edge between `A` and `B` requires `k`, so `k` is unavoidable for `T`.

### 4.2 Main pass (start -> exit, or start -> goals)

Targets (`ObstacleWorkGraph.MainTargets`): the exit. When start and exit share a node, the level
is "fetch the goals and come back", so the nodes holding `Goal` POIs (start node excluded) take
the role of the exit; with no such node, the `OptionalGoal` nodes do. With several goal targets a
random one is the primary target of each obstacle. Skipped only when no target exists (the side
pass then gets all Complexity). Without this, a shared start/exit node on a cycle (no pocket)
silently dropped all Complexity.

Budget split, decided once before the pass:
- **Normal** (`Complexity > SmallComplexityMax`): main gets `round(Complexity * MainShare)`, side the rest.
- **Small** (`Complexity <= SmallComplexityMax`, default 3): main gets everything, except that one
  Complexity is left for the side pass with `ChanceReserveSide` (default 0.75).
- **Complexity 1**: reserving would leave the main pass nothing, so the chance drops to
  `ChanceReserveSideOnly` (default 0.2); on a hit only the side pass runs.

**Pocket pre-scan.** Before the split, the side pass lists the valid pockets (section 4.3) in its
seeded-shuffled order, with the pocket size roll already made. Then:
- no valid pocket -> nothing is reserved in any of the three cases, the main pass gets everything;
- otherwise the side budget is capped at the number of pockets found, the rest goes to main, and
  the first `side budget` pockets become **protected**: the main pass never puts a solution into
  a node of a protected pocket.

Whatever the main pass could not spend also falls to the side pass (it then uses unprotected
pockets that are still valid).

Keep a list of **required nodes** `Q`, initially `{exit node}`. Per obstacle:
1. Order the targets: the exit first for the first obstacle; afterwards the most recent solution
   node first with `ChanceNestSolution`, else the exit first. The remaining members of `Q` follow
   in seeded-shuffled order (shuffled from Id order).
2. Walk the targets in that order and take the best cut of each:
   - a **fresh** cut (no edge of it carries a condition yet) is accepted at once;
   - a **stacking** cut (`c` = highest condition count on its edges, 1 or 2) is accepted only with
     chance `ChanceStack ^ c` (default 0.2: 20 %, then 4 %). When refused, the cheapest fresh cut
     of the same target is taken if one exists (wider, but fresh); otherwise the next target is tried.
   "Best" means lowest cost; exactly equal costs are decided by seeded roll.
3. If no target was accepted but some stacking cut was refused, take the least stacked of them
   (ties: first in target order). Stacking is rare while alternatives exist, yet Complexity is not
   wasted on tiny graphs where stacking is the only option. No cut at all -> pass ends early.
   **Stack limit** (added after phase 7): count `locked` = passages with a condition and `extra`
   = conditions beyond the first on them, over the whole graph. A stacking cut is only
   considered while `extra <= FreeStackedConditions + MaxStackLoad * locked` (defaults 2 and
   0.25) still holds with the cut in place: each of its passages adds 1 to `locked` when fresh,
   else 1 to `extra`. A cut over the limit is refused without a roll and cannot be forced either.
   When nothing fresh is left the pass ends early; the unused Complexity falls to the side pass
   or is dropped. Fresh cuts are never limited; pre-existing conditions and secret passages count.
   **Cramped cuts** come last of all, after forced stacking: a cut that leaves no node but the
   start for the solution (no other non-secret, unprotected node on the start side). It would put
   the key next to its door at the start, and such a solution cannot be guarded later. (Added in phase 7.)
4. Lock the cut, place the solution, add its node to `Q` (unless it is the start node).

Every main obstacle guards a node the player must visit, so **all of them are mandatory for the
exit** (testable through `GraphMetadata`: exit `Reach` lists every main solution when bypass is 0).

Edge cases this covers without special code:
- **Start and exit adjacent**: every start-exit cut contains the direct edge. The first obstacle
  locks it. Later obstacles mostly lock away earlier solutions instead (chain: solution 1 behind
  obstacle 2, solution 2 behind obstacle 3...), because re-targeting the exit means stacking and
  must pass the `ChanceStack` roll. After 3 conditions the edge is uncuttable anyway. Three
  stacked obstacles with unrelated solutions ("collect three keys") are the rare outcome, or the
  forced one when the graph offers nothing else to lock.
- **Bottlenecks**: a bridge is the cheapest cut, so it wins; wide cuts only when nothing narrower
  exists, and never above `MaxEdgesPerObstacle`.
- **Fragmentation**: a cut may split off a group of nodes; fine by the softlock rule.
- **Dense graph / exit with high degree**: no cut under the width cap -> fewer obstacles, Complexity unused.

### 4.3 Side pass (secrets, goals, optional goals)

Runs with the Complexity left after the main pass (also when the main pass was skipped). Pockets
come from the pre-scan of section 4.2, protected ones first; each is re-validated right before
locking, and an invalid one is skipped for the next in the list.
- Treasure nodes: secret nodes, nodes with `Goal` or `OptionalGoal` POI. Shuffled from Id order.
  A treasure already inside a listed pocket is skipped, and listed pockets never overlap.
- Pocket of a treasure: far side of a cut `s | treasure` whose edges all join **one node pair**
  (one pocket node, one host node outside). Parallel passages between that pair still make a dead
  end. For this search the cost is 1 per node pair, secret edges included.
  Roll between the smallest pocket (cut nearest the treasure) and the largest dead-end branch
  (`ChanceLargestPocket` = 0.5, a constant in `ObstaclePassSide`).
  Implementation: every joined node pair is tried - take its passages away, and whatever loses
  its way to the start is the pocket behind that pair.
- Valid pocket: no passage of the pair carries a condition; at least one and at most
  `MaxEdgesPerObstacle` entry passages; pocket holds no Start, Exit or Solution POI and no
  conditioned edge; pocket holds at most half of all nodes (`MaxPocketNodeShareDivisor` = 2,
  added in phase 5: with the start in a dead end, the rest of the level would otherwise count
  as one huge pocket).
- One obstacle locks **every** passage of the pair that leads into the pocket (bidirectional ones and
  one-way ones pointing in). A one-way passage pointing out stays open: it cannot be used to enter.
  This covers the `A -> S -> A` secret loop - only the entry `A -> S` is locked. It is the one case
  where a one-way passage gets a condition; safe, because the softlock rule only needs the solution
  to be reachable without it.
- A pocket with several treasures still costs one obstacle.
- Solution: same blocked flood, but never inside any listed pocket. So side obstacles never chain.
  When no node outside the pockets can hold the solution, the pocket stays open.

### 4.4 Early exits and limits

No-op when: `Complexity == 0`; fewer than two nodes; not exactly one start node and one exit node
(missing, or several - unsupported in v1); no edges. Unused Complexity is dropped silently.
`MaxConditionsPerEdge` (3) is enforced through infinite capacity. No nodes or edges are added, so
`MaxEdges` caps are untouched. Pre-existing conditions are tolerated (count toward the cap, honoured by floods).

## 5. Potential issues

- **Reserved side Complexity going unused**: handled by the pocket pre-scan and protected pockets
  (section 4.2). Leftover risk is small: a main cut could still put a condition inside a protected
  pocket (only through zero-cost secret edges); re-validation then skips that pocket.
- **Candidates cluster on a unique cheapest bottleneck**: when one passage is strictly cheaper than
  all others, the whole target series lands on it and other positions only come from the source
  series (at most `MaxBallRadius` = 3 hops from start). `CostStack` evens this out once the
  bottleneck carries a condition. Measured in phase 7 (section 5.1): long corridors are covered
  well enough, the radius stays.
- **Stacking frequency (measured in phase 3)**: start next to exit with five leaves on the start,
  Complexity 3, 2000 seeds; share of seeds by number of conditions on the direct passage:

  | `ChanceStack` | 1 | 2 | 3 |
  |---|---|---|---|
  | 0.3 | 65.4 % | 33.6 % | 1.0 % |
  | **0.2 (default)** | 74.6 % | 25.1 % | 0.2 % |
  | 0.1 | 86.0 % | 13.9 % | 0.0 % |

  Part of the "2" share is forced stacking (a solution landed in the start node, so nothing
  else could be locked), which no `ChanceStack` value removes.
- **Large pockets take solution room from the main pass**: a protected pocket may be a whole
  branch (up to half of the graph), and no main solution goes into it. Not tuned in phase 7
  (no problem seen in the sweep); revisit if levels show that
  `ChanceLargestPocket` or the size cap needs tuning.
- **`MainShare` rounding**: the main budget is `round(Complexity * MainShare)` with halves rounded
  up, computed in decimal so that 5 * 0.7 gives 4, not 3.
- **Solutions piling up near start** when cuts land close to it: handled by solution weights and by
  preferring target-side cuts (radius 0 first). Watch in seed sweeps.
- **Locked non-chokepoint passages**: the map generator must guard every opening of such a passage (accepted).
- **Ordinary bypass** makes its obstacle optional, and with it every obstacle that only guarded
  its solution. Safe (more open passages never hurt), but such a level is easier than its
  Complexity suggests. At 0.01 it is too rare for seed sweeps; tests force it with chance 1.
- **Secret bypass** intentionally breaks "mandatory" for that obstacle; tests of the property use chance 0 for both bypasses.
  A bypassed obstacle keeps its place in the required-node list: later obstacles may still guard
  its solution, although the player can now walk around it.
- **`ChanceIgnoreConditionals` of `ProcRelSecrets`** has no effect in Simple (no conditions exist
  yet when secrets are made); `ChanceSecretBypass` takes over that role.
- **Goal in a main-locked region or side pocket** makes that obstacle mandatory; harmless.
- **Future builders with real points of no return**: one-way edges are uncuttable and ignored by the
  solution flood, which is conservative; the validator stays the final arbiter.
- **Cost**: about `Complexity * candidates` max-flows plus one flood each; negligible at Size 110.
  `CancellationToken` checked per obstacle anyway.
- **Determinism**: own salted RNG stream, all enumeration in Id order, solution Ids derived from
  the obstacle ordinal with a dedicated salt; no dictionary/set iteration feeds the output.

### 5.1 Tuning results (phase 7)

Sweep through `GraphBuilderSimple`: sizes 5 / 10 / 20 / 40 / 80, connectivity 0 / size/4 / size,
Complexity `sqrt(Size)` and `2 * sqrt(Size)`, 300 seeds each, default options.

- **Complexity used**: 82-99 % of the requested obstacles are placed (lowest on 5-node graphs).
  The side pass places about 1-2 obstacles per level.
- **Solutions in the start node** were the one real problem: 9-42 % of all solutions, because
  the cut around the start is often as cheap as any other and leaves the solution nowhere else to
  go. Fix: cramped cuts are a last resort (section 4.2). Afterwards: 2-8 % at size 20 and above,
  6-17 % at size 10, 21-32 % at size 5 (few other places exist there).
- **Stacking**: share of locked passages with 2 conditions is 2-12 % on trees, 7-20 % at
  connectivity size/4, and 11-42 % at connectivity = size with the higher Complexity; 3 conditions
  stay below 9 % (12 % on 5-node trees). The high values are forced stacking: on a dense graph
  only nodes with at most `MaxEdgesPerObstacle` passages can be cut off, and fresh cuts run out.
  `ChanceStack` does not affect it; the stack limit added afterwards does (section 5.2).
- **Cut width**: trees use single passages only; at connectivity size/4 widths 1 and 2 dominate;
  at connectivity = size 60-70 % of the obstacles lock 4 passages (the cap).
- **Position along the start-exit path** (thirds of the path, share of locked path passages):
  42 / 24 / 34 % on 80-node trees (path about 16 passages), close to even on shorter paths. The
  middle of long corridors is thinner but not empty, so `MaxBallRadius` stays 3.
- **Mandatory for the exit**: 61-91 % of all solutions; the rest are side obstacles and bypasses.
- **Defaults and cost constants are unchanged.** Nothing in the sweep called for exposing the
  cost constants as settings.

### 5.2 Stack limit results

Same sweep as 5.1, the limit switched off (`MaxStackLoad = 100`) against the defaults (0.25 / 2).
Share of locked passages with 2 / 3 conditions, and share of the requested obstacles placed:

| Graphs | 2 conditions | 3 conditions | Placed |
|---|---|---|---|
| Trees (connectivity 0), any size | unchanged (2-13 %) | 0-8 % -> 0-8 % (only 5-node trees keep 8 %) | unchanged, at most -2 points |
| Connectivity size/4 | 4-20 % -> 4-19 % | 0-5 % -> 0-3 % | at most -3 points |
| Connectivity = size, Complexity `sqrt(Size)` | 9-24 % -> 3-17 % | 0-3 % -> 0-1 % | -0 to -4 points |
| Connectivity = size, Complexity `2 * sqrt(Size)` | 28-39 % -> 22-26 % | 5-8 % -> 0-2 % | 82-92 % -> 73-83 % |

So the limit bites where it should - dense graphs asked for many obstacles - and costs there about
8-9 points of placed obstacles. Variants tried: `MaxStackLoad = 0.15` (dense high-Complexity cases
drop to 15-23 % doubled passages but only 72-77 % placed) and `FreeStackedConditions = 1` (removes
triple stacks on 5-node graphs, costs 2-3 points on small graphs). Defaults stay 0.25 and 2.

## 6. Implementation plan

New code, namespace `LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler`
(folder `Internal/Generator/Graph/Processors/Obstacler/`):

| File | Role |
|---|---|
| `ProcObstaclePlacer.cs` | Entry: `ctor(graph, options, complexity, seedGraph)`, `Execute(cancellationToken)`. Early exits, budget split, runs the passes. |
| `ObstacleWorkGraph.cs` | Index view of the graph (Id order via `GraphIndex`), start/exit lookup, cut costs, BFS balls, dead-end check, side assignment. |
| `ObstacleState.cs` | State shared by the passes (work graph, RNG, search, picker, obstacle count) and `TryPlaceObstacle`: lock, blocked flood, pick solution node, undo on failure. (Added in phase 3; replaces the planned `AddObstacle` on the work graph.) |
| `MinCutFinder.cs` | Max-flow / min cut on the index view with contracted terminals. |
| `CutCandidate.cs` | Cut edges, far-side nodes, ordinary width, cost, highest existing condition count. |
| `CutSearch.cs` | Candidate cuts per target: ball-grown terminals, dedupe, width cap, cost order. (Added in phase 2; split out of `ObstacleWorkGraph`.) |
| `SolutionPicker.cs` | Weighted node pick among the nodes the blocked flood reached, with an exclusion set (protected pockets, side pockets). The flood itself lives in `ObstacleState`. |
| `ObstaclePassMain.cs`, `ObstaclePassSide.cs` | The two passes; the side pass also exposes the pocket pre-scan (`Scan`, `BuildNodeMask`) used by the budget split. |
| `ObstaclePocket.cs` | One pocket: gate node, host node, nodes, passages of the pair, entry passages. (Added in phase 5.) |

Changes to existing code:
- `Internal/Generator/Graph/Meta/GraphFlood.cs`: optional edge filter in the constructor (skip
  secret / one-way edges for the solution flood). Existing callers unchanged.
- `Internal/Generator/Graph/Builders/AGraphBuilder.cs`: `protected virtual bool UsesObstaclePlacer => false`;
  `RunProcessorsShaping()` runs the placer last when true.
- `GraphBuilderSimple.cs`: override to `true`; fix docs ("Complexity is ignored", "without obstacles").
- `Data/Config/Graph/Proc/CnfProcObstaclePlacerOpt.cs` (new public record + `Validate()`), added to
  `CnfProcOpt` as `ObstaclePlacer`: `MaxConditionsPerEdge = 3`, `MaxEdgesPerObstacle = 4`,
  `MainShare = 0.7`, `SmallComplexityMax = 3`, `ChanceReserveSide = 0.75`, `ChanceReserveSideOnly = 0.2`,
  `ChanceNestSolution = 0.4`, `ChanceStack = 0.2`, `MaxStackLoad = 0.25`, `FreeStackedConditions = 2`, `ChanceSecretBypass = 0.25`, `ChanceBypass = 0.01`. Cost weights
  (`CostChokepoint = 4`, `CostOpen = 6`, `CostStack = 2`, `CostSecret = 0`) stay
  named constants inside `ObstacleWorkGraph` until tuning shows they need exposing.
- `Internal/Prop/Resolvers/Graph/GraphResolver.cs` (`ResolveProcOpts`): pass the default section.
- `GENERATOR.md`: describe the processor and the new Simple behaviour.

### Phases

Work goes phase by phase. Every phase ends with `dotnet build LostInLimbo.slnx` and
`dotnet test LostInLimbo.slnx` green, then **stops for review** (the user commits). Tests come first
where practical, per `AGENTS.md`. The tests of section 7 are written in the phase that adds the behaviour.

| Phase | Content | Visible effect |
|---|---|---|
| 1. Foundations | `CnfProcObstaclePlacerOpt` (all settings at once, with `Validate()`), added to `CnfProcOpt`; default section in `GraphResolver`; `GraphFlood` edge filter. | None. Generated graphs unchanged. |
| 2. Cut machinery | `ObstacleWorkGraph` (index view, capacities, BFS balls, side assignment), `MinCutFinder`, `CutCandidate`. Tests on hand-made graphs: chain, cycle, star, parallel passages, saturated edge, one-way, secret edges. | None. Not wired. |
| 3. Main pass | `SolutionPicker`, `ProcObstaclePlacer` (early exits, solution Ids, `AddObstacle`), `ObstaclePassMain` (targets, nesting, `ChanceStack`). All Complexity goes to the main pass; secret passages on a cut are always locked; no bypasses. Tests on hand-made graphs, validator as oracle. | None. Not wired. |
| 4. Wiring | `UsesObstaclePlacer` hook in `AGraphBuilder`, enabled in `GraphBuilderSimple`; builder docs and `GENERATOR.md`; sweep test over seeds x sizes x connectivity x complexity; existing Simple tests adjusted (graphs now carry conditions). | `GraphBuilderSimple` produces obstacles between start and exit. Can be inspected in the editor. |
| 5. Side pass | `ObstaclePassSide` (pockets, incl. parallel passages and `A -> S -> A` entries), pocket pre-scan, budget split (`MainShare`, `SmallComplexityMax`, `ChanceReserveSide`, `ChanceReserveSideOnly`), protected pockets. | Secrets, goals and optional goals in dead ends get locked. |
| 6. Bypasses | `ChanceSecretBypass` and `ChanceBypass`. | Rare open shortcuts past obstacles. |
| 7. Tuning | Statistics over a seed sweep (obstacles placed vs Complexity, stacking rate, solutions in or next to start, cut widths); adjust defaults and cost constants, expose constants as settings if needed; final pass over this document (status, deviations). | Better defaults only. |

Notes:
- Settings existed from phase 1 and became active in their phase (5: budget split; 6: bypass chances).
- Phases 2 and 3 are unreachable from the public API, so they cannot break generation.
- Phase 4 is the first one that changes generated worlds; phases 5 and 6 change them again for the same seed.

## 7. Tests (`LostInLimbo.LevelGen.Tests/Generator/Graph/Processors/Obstacler/`)

- Pocket pre-scan: no valid pocket -> main pass gets all Complexity; side budget never exceeds the
  pocket count; no main solution inside a protected pocket; reserved Complexity is actually spent.
- Budget split: Complexity above the small limit follows `MainShare`; small Complexity reserves one for
  the side pass at chance 1 and never at chance 0; Complexity 1 uses `ChanceReserveSideOnly`.
- Early exits: Complexity 0, one node, missing start/exit -> graph unchanged.
- Determinism: same seed -> identical graph; shuffled node/edge list order -> same result.
- Limits: obstacles <= Complexity; conditions per edge <= 3; ordinary edges per obstacle <= cap;
  no condition on one-way edges except pocket entries; no solution in secret nodes; every condition points at an existing Solution POI.
- Softlock: `GraphValidator` passes; additionally each solution is reachable with its own POI blocked
  and secret edges removed.
- Main pass: with both bypass chances 0, exit `Reach` (via `GraphMetadata`) contains every main solution; chain
  graph gets width-1 cuts only; cycle gets a 2-edge cut; start adjacent to exit (triangle, direct
  edge only): nesting dominates over many seeds, stacking only when rolled or forced (two-node graph stacks up to 3); start == exit -> only side obstacles.
- Side pass: pocket contents rule; solution outside all pockets; pocket behind parallel passages
  gets all of them locked with one solution; `A -> S -> A` secret gets only its entry locked;
  no side obstacles without spare Complexity.
- Ordinary bypass: chance 1 leaves exactly one ordinary passage open per multi-passage cut, never on a
  single-passage cut, never a parallel of a locked passage, never in the side pass; graph still validates.
- Secret bypass: chance 1 leaves secret cut edges open and the graph still validates.
- Cancellation: cancelled token throws `OperationCanceledException`.
- Sweep through `GraphBuilderSimple`: many seeds x sizes x connectivity x complexity, secrets with
  two passages enabled -> always beatable, caps hold.

## 8. Verification

`dotnet build LostInLimbo.slnx` and `dotnet test LostInLimbo.slnx` after each step; the
`GraphBuilderSimple` sweep test is the end-to-end check.
