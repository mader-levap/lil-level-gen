# Code review: `LostInLimbo.LevelGen`

Review of the library (tests excluded) against `AGENTS.md` and general C# practice.
All paths are relative to `LostInLimbo.LevelGen/`.

## Summary

The code is in good shape:

- It is engine-neutral.
- Every RNG is seeded (SplitMix64 and FNV-1a, no `System.Random`).
- IDs come from seeds.
- Visit orders are sorted explicitly.
- Config validation runs before a run is claimed.
- The session concurrency design (CAS claims plus `_runGate`) is careful and well documented.

The findings below are grouped by kind and given IDs (A = bugs, B = AGENTS.md adherence, C = robustness/design). The **Phases** section at the end turns them into work we do one phase at a time.

---

## Findings

### A. Bugs / correctness

- **A1. The clock seed is read twice when no text seed is given.**
  - Where: `Internal/Prop/ConfigGenerator.cs` `ResolveSeed` (internal prop seed) and `Internal/Prop/Resolvers/GeneralResolver.cs` `ResolveSeed` (world seed).
  - Problem: the two `DateTime.UtcNow.Ticks` reads are independent. The config draws (size, builder, …) therefore cannot be reproduced from the reported `GenerationStatus.Seed`, which contradicts its doc ("the reported value always reproduces the run").
  - Fix: read the clock once in `ConfigGenerator` and pass the value through `PropData`.
- **A2. `GraphValidator` cannot be reused.**
  - Where: `Internal/Generator/Graph/Validator/GraphValidator.cs`.
  - Problem: per-call state (`_poiTypes`, `_startNodeIds`, `_exitNodeIds`, `_goalNodeIds`, `_flooded`) is never reset. A second `Validate()` on the same instance fails with "Duplicate POI id".
  - Fix: reset the state at the start of a validation, or keep it in locals.
- **A3. The `LevelGenSession.Pause()` remarks name the wrong method.** They say "continue generation with a `Start`"; it should be `Resume`.
- **A4. Some public APIs throw `NullReferenceException` instead of `ArgumentNullException`:**
  - `LevelGenConfig.Execute(null)`: the NRE happens in the `ConfigGenerator` ctor.
  - `LevelGraphValidation.Validate(world, null)`: the NRE happens at `_options.Enabled`.
  - Internally: `GraphFlood` ctor, `GraphBuilderFactory.Create`, `ConfigGenerator` ctors.
- **A5. Some config sections are never validated.** `CnfGraph.Validate` / `CnfMap.Validate` only null-check `ValidatorOpts`. `CnfValidGraphOpt` / `CnfValidMapOpt` have no `Validate()`, which breaks the pattern used by every other section.

### B. AGENTS.md adherence

- **B1. `PropData` is `public`** but lives in `Internal/Prop/Data/PropData.cs`, so an internal type leaks into the public API.
- **B2. The csproj has no `<GenerateDocumentationFile>`.** The XML docs are not shipped in the NuGet package, and broken `cref`s are never reported. Broken or misleading crefs found:
  - `LevelGenerationSerializer` in `Data/Config/CnfLevelGeneration.cs` and `Data/Prop/PropMain.cs` (the real name is `LevelGenSerializer`).
  - `LevelGenerationSession` in `Data/Status/EnGenResult.cs` (the real name is `LevelGenSession`).
  - `LevelGenConfig` without its namespace in `Data/Config/CnfLevelGeneration.cs`.
  - Internal `GraphValidator` / `MapValidator` in `CnfValidGraphOpt.cs` / `CnfValidMapOpt.cs`.
  - `MapGenerator.Execute` (it is actually `ExecuteStep`) in `Internal/Generator/Map/MapRunResult.cs`.
  - Public docs pointing at internal builders: `CnfGraphBuilderLinear.cs`, `CnfGraphBuilderSimple.cs`.
- **B3. Magic numbers:**
  - `ConfigGenerator.ResolveSize` (2, +3).
  - `GeneralResolver.ResolveDegeneration` (10, 20).
  - All ranges in `BuilderLinearResolver` / `BuilderSimpleResolver`.
  - `GraphResolver`: `/2+1`, `0.1f`, `0.05f`, `2/3`.
  - `GraphBuilderSimple.PickScenario`: `NextInt(3)` with a cast that relies on enum order.
- **B4. `#region` is not used** in long multi-part files: `Api/LevelGenSession.cs`, `Internal/Generator/LevelGenerator.cs`, `Internal/Generator/Graph/Validator/GraphValidator.cs`, `Internal/Generator/Graph/Builders/GraphBuilderLinear.cs`.
- **B5. Internal code depends on `Api`.** `GraphValidator.cs` has `using LostInLimbo.LevelGen.Api;`, used only for a doc cref.
- **B6. The data contracts expose public mutable collections.**
  - `LilGraph.Nodes/Edges` are `List<>` with a public `set`.
  - `LilNode.Pois/Reach/OpenReach` and `LilEdge.Conditions` are mutable.
  - Consequences: the docs say "immutable once created", which is not true; record equality is reference-based for these members; `with` aliases the collections (`LilWorldCopy` has to work around this).
  - AGENTS.md prefers `IReadOnlyList<T>`. This is a broad public-API change, so it gets its own phase.
  - **Resolution: not needed.** Consumers never share state with the generator: `LevelGenerator.PublishTerminal` stores a deep copy and `LevelGenSession.World()` hands out another deep copy on every call (`LilWorldCopy`). Read-only types would add only compile-time protection, would make the types harder to build and edit for the editor and for the world-accepting APIs (`LevelGraphValidation`, `LevelGraphReach`), and would not fix record equality either. Fixed instead: the false "immutable once created" claims on `LilNode`, `LilEdge` and `LilGraph`, plus notes on `LilWorld` and the graph types about copy-on-read and `with` sharing collections.
  - **Revisit if:** large maps make the per-call copy in `World()` measurably expensive. Immutable snapshots could then be shared instead of copied.

### C. Robustness / design

- **C1. A shared RNG stream across sub-resolvers.** `GraphResolver` hands its `_random` to `BuilderSimpleResolver` / `BuilderLinearResolver`. One extra draw in a builder resolver shifts every later draw (proc opts) and silently changes generated levels. Use one salted sub-seed per resolver, as the generator passes already do.
- **C2. `GraphResolver.PickGraphBuilder` depends on enum order** ("exclude first member"). Any new `EnGraphBuilder` member is picked automatically. Use an explicit array of eligible builders. The same applies to `GraphBuilderSimple.PickScenario`, which casts `NextInt(ScenarioCount)` to `EnSkeletonScenario`: pick from an explicit `Scenarios` array instead (same draw, so output stays the same if the array keeps the enum order).
- **C3. `EvalWeights`:**
  - It throws `IndexOutOfRangeException`, which is reserved for the runtime.
  - It does no null checks and does not validate that weights are positive. Zero or negative weights crash in `NextInt`, and `Sum()` can overflow.
  - The size-mismatch exception has no `paramName`.
  - `Int` / `Float` are duplicates (use a generic `Pick<T>`).
  - Neither `EvalWeights` nor `EvalCurves` is used in production yet.
- **C4. `PrepData` is a `record` with mutable `set` properties**, and `PrepassGenerator` mutates it through a field. Make the properties `init`-only.
- **C5. Fields that should be `readonly`:**
  - `_world` in `GraphGenerator` / `MapGenerator`.
  - `_map`, `_options` in `MapValidator`.
  - `_graph`, `_options` in `GraphValidator`.
- **C6. Unused members:**
  - `MapGenerator._prepData`.
  - All fields of `StyleResolver`.
  - `_propMain` / `_propData` in `BuilderLinearResolver` / `BuilderSimpleResolver`.
  - `using …Generator.Graph` in `MapRunResult.cs`.
- **C7. Duplicated code:**
  - `BuildVisitOrder()` is copied in all four processors.
  - The index-by-id and degree maps are copied in `ProcNodeRelSize`, `ProcNodeStyle`, `ProcRelSecrets`.
  - `CreateNodes`, `PlacePois`, the ID salts and the finalization processor sequence are copied between `GraphBuilderSimple` and `GraphBuilderLinear`.
  - Fix: add an internal graph-index helper and move the shared builder steps into `AGraphBuilder`.
- **C8. Stale or inconsistent comments:**
  - `GraphBuilderLinear.DeriveEdgeId` says "MaxSize is 100", but `CnfGraph.MaxSize` is 110. It packs edge IDs into 8 bits while Simple uses 32; add a guard or use 32-bit packing.
  - The `MapGenerator` remarks say the API keeps the generator instance alive, but a new instance is created per step.
  - The `LevelGenerator.PublishTerminal` remark "must be called while holding `_pauseGate` semantics (this method takes it)" contradicts itself.
  - The `AGraphBuilder` precondition says the world is discarded on cancel, which contradicts the pause-over-cancel graph reset in `GraphGenerator.Execute`.
  - `LevelGenSerializer` docs say it rejects a "newer" `ConfigVersion`, but the code rejects any different version.
  - Typo "errorenous" in `Data/Status/EnGenResult.cs`.
- **C9. Minor style:**
  - `LilPoint.Move` uses `nameof(EnDimension)` instead of `nameof(dimension)`, is missing `<returns>`, and could be a `readonly record struct`.
  - `GraphBuilderFactory` passes `nameof(cnfGraphBuilder.Builder)` as the param name.
  - Brace style is mixed: `if (...) {` in `LevelGenerator`, `GraphValidator`, `GraphResolver`; inline `new LilNode {` in the builders.
  - Redundant `= null` on `LevelGenerator._prepData`.
  - Unneeded `!` on `_startNodeIds`.
  - `GetRange`, `Normalize`, `IsRestricted` could be `static`.
  - `volatile` on `LevelGenSession._generator` is redundant (every access holds `_runGate`), and its comment is misleading.

**Intentionally not flagged:**
- The `System.Text.Json` dependency (already in place).
- `Thread.Sleep` in the placeholder map step.
- The internal test hooks.
- The documented clock fallback in config generation itself.

---

## Phases

Rules for every phase (from AGENTS.md):
- Write a failing test first for bugs and new behavior.
- Update tests for any behavior change.
- `dotnet build LostInLimbo.slnx` and `dotnet test LostInLimbo.slnx` must pass with no new warnings.
- No commits; the user reviews and commits.

### Phase 1: Bugs (A1–A5)
- [x] A1: read the clock once and pass the seed through `PropData`. Add a test that the world seed and the config draws come from the same resolved seed.
- [x] A2: reset `GraphValidator` per-call state. Add a test that calls `Validate()` twice on one instance.
- [x] A3: fix the `Pause()` doc (`Start` → `Resume`).
- [x] A4: add `ArgumentNullException` guards to public and internal entry points, with tests.
- [x] A5: add `Validate()` to `CnfValidGraphOpt` / `CnfValidMapOpt` and call it from the parent configs.

**Done when:** the new tests fail before the fixes and pass after them, and the whole suite is green.

### Phase 2: AGENTS.md adherence (B1–B5)
- [x] B1: make `PropData` `internal`.
- [x] B2: enable `<GenerateDocumentationFile>`, then fix all cref warnings and the public docs that point at internal types.
- [x] B3: replace the magic numbers with named constants.
- [x] B4: add `#region`s to the four long files.
- [x] B5: remove the `Api` dependency from `GraphValidator`.

**Done when:** the build has no doc/cref warnings, the public API no longer contains `PropData`, and generated output is unchanged.

### Phase 3: Robustness (C1–C6)
- [x] C1: give each sub-resolver its own salted sub-seed. **This changes the generated config for the same text seed**, so review the seed-snapshot test diffs explicitly.
- [x] C2: pick from explicit lists of eligible graph builders (`GraphResolver.PickGraphBuilder`) and skeleton scenarios (`GraphBuilderSimple.PickScenario`, which drops `ScenarioCount`).
- [x] C3: clean up `EvalWeights`: generic `Pick<T>` over arrays, constant-time input checks only (hot path; the caller guarantees valid weights, as documented), no `IndexOutOfRangeException`.
- [x] C4: make `PrepData` immutable.
- [x] C5: add the missing `readonly` modifiers.
- [ ] C6: remove the unused members. **Deferred by the user; not part of this pass.**

**Done when:** the suite is green and the only output change is the intended one from C1.

### Phase 4: Refactors and cleanup (C7–C9)
- [x] C7: extract a shared graph-index helper (visit order, index-by-id, degrees) and move the shared builder steps into `AGraphBuilder`. The RNG draw order must not change.
- [x] C8: fix the stale and inconsistent comments, plus the 8-bit edge-ID packing guard.
- [x] C9: style fixes.

**Done when:** the determinism tests show identical generated graphs before and after, and the suite is green.

### Phase 5: Read-only data contracts (B6) - dropped
- [x] Not needed: copy-on-read already isolates consumers (see B6). Replaced by a docs fix of the result types' mutability claims.

**Done when:** the docs match the actual mutability. Done.
