using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Validator;

/// <summary>
/// Verifies that a level graph is beatable - an abstract simulation of a player who starts
/// at the start POI, walks through passages, collects solutions, fulfills objectives and
/// reaches the exit without ever getting softlocked.
/// </summary>
/// <remarks>
/// <para>
/// The check is a deterministic fixpoint flood-fill. Every node containing a start POI is
/// flooded first and its POIs are collected. Then, over and over, a passage becomes passable
/// once every one of its conditions references a collected POI; a passable passage floods the
/// node on its other end (respecting <see cref="EnPassageDirection"/>), which may collect more
/// solutions and unlock further passages. The loop stops when nothing new floods.
/// </para>
/// <para>
/// Failure reasons are reported from most specific to least specific, so the message
/// points at the root cause. Each reason is reported only for the first failing check,
/// in this order:
/// </para>
/// <list type="bullet">
/// <item>Structural problems (null or empty graph, empty or duplicate identifiers, missing
/// nodes, missing Start or Exit POIs, dangling references, conditions pointing at missing
/// or non-Solution POIs, nodes over their <see cref="LilNode.MaxEdges"/> hard cap, a Goal
/// POI inside a secret node) - reported before any traversal.</item>
/// <item>A conditioned passage the player can reach but can never satisfy (softlock).</item>
/// <item>A mandatory objective trapped behind a one-way passage whose far side never
/// reaches an exit, or stranded behind a one-way passage leading toward the exit. The
/// one-way checks bail out entirely when no exit is reachable at all, so their reasons
/// never compete with the unreachable-exit reason.</item>
/// <item>An unreachable exit.</item>
/// <item>Leftover unreachable nodes as the catch-all (e.g. a disconnected dead-end branch
/// while the exit itself is fine).</item>
/// </list>
/// <para>
/// A conditioned edge wholly inside an unreachable branch is never a softlock - the player
/// cannot stand at it - so it falls through to the unreachable-region reasons.
/// </para>
/// <para>Instances hold per-call state, so create a new instance per validation
/// (or use <see cref="LevelGraphValidation"/>); one instance must not be shared across
/// concurrent calls. Safe to call in bulk seed loops.</para>
/// </remarks>
/// <param name="graph">Graph to verify.</param>
/// <param name="options">Validator options; defaults to <see cref="GraphValidatorOpt.Default"/>.</param>
internal sealed class GraphValidator(LilGraph? graph, GraphValidatorOpt? options)
{
  /// <summary>Graph to verify.</summary>
  private LilGraph? _graph = graph;
  /// <summary>Validator options; defaults to <see cref="GraphValidatorOpt.Default"/>.</summary>
  private GraphValidatorOpt _options = options ?? GraphValidatorOpt.Default;
  /// <summary>All nodes mapped by id.</summary>
  private Dictionary<Guid, LilNode>? _nodesById;
  /// <summary>All start nodes.</summary>
  private HashSet<Guid> _startNodeIds = [];
  /// <summary>All end nodes.</summary>
  private HashSet<Guid> _exitNodeIds = [];
  /// <summary>All nodes holding a mandatory <see cref="EnPoiType.Goal"/> POI.</summary>
  private HashSet<Guid> _goalNodeIds = [];
  /// <summary>All POI types mapped by POI id.</summary>
  private Dictionary<Guid, EnPoiType> _poiTypes = [];
  /// <summary>Flooded nodes.</summary>
  private HashSet<Guid> _flooded = [];

  /// <summary>
  /// Validates the given level graph for beatability.
  /// </summary>
  /// <returns>
  /// A result whose <see cref="GraphValidationResult.Success"/> is <see langword="true"/> when
  /// the graph is beatable; otherwise a failed result with the reason of the first failed check.
  /// </returns>
  public GraphValidationResult Validate()
  {
    GraphValidationResult? result = CheckFundamental();
    if (result != null) return result;

    result = Setup();
    if (result != null) return result;
    result = CheckGoalSecrets();
    if (result != null) return result;
    result = CheckEdges();
    if (result != null) return result;
    result = CheckMaxEdges();
    if (result != null) return result;
    result = CheckTraversals();
    if (result != null) return result;
    result = CheckGoalBeatability();
    if (result != null) return result;
    result = CheckExits();
    if (result != null) return result;

    return GraphValidationResult.Succeed(); // Success: All checks passed.
  }

  /// <summary>
  /// Checks fundamentals.
  /// </summary>
  /// <returns>Null if success, otherwise failure.</returns>
  private GraphValidationResult? CheckFundamental()
  {
    if (_graph is null) return GraphValidationResult.Fail("Graph is null.");
    if (_graph.Nodes.Count == 0) return GraphValidationResult.Fail("Graph has no nodes.");
    return null; // Success: Graph exists and has nodes.
  }

  /// <summary>
  /// Sets up internal data: builds the node and POI indexes, rejecting empty or duplicate
  /// node/POI identifiers, records which nodes carry a mandatory Goal POI, and requires at
  /// least one Start and one Exit POI.
  /// </summary>
  /// <returns>Null if success, otherwise failure.</returns>
  private GraphValidationResult? Setup()
  {
    _nodesById = new Dictionary<Guid, LilNode>(_graph!.Nodes.Count);
    foreach (var node in _graph.Nodes)
    {
      if (node.Id == Guid.Empty) return GraphValidationResult.Fail($"Node {node.Id} has an empty id.");

      if (!_nodesById.TryAdd(node.Id, node)) return GraphValidationResult.Fail($"Duplicate node id {node.Id}.");
    }

    foreach (var node in _graph.Nodes)
    {
      foreach (var poi in node.Pois)
      {
        if (poi.Id == Guid.Empty) return GraphValidationResult.Fail($"POI {poi.Id} has an empty id.");

        if (!_poiTypes.TryAdd(poi.Id, poi.Type)) return GraphValidationResult.Fail($"Duplicate POI id {poi.Id}.");

        if (poi.Type == EnPoiType.Start) _startNodeIds.Add(node.Id);
        else if (poi.Type == EnPoiType.Exit) _exitNodeIds.Add(node.Id);
        else if (poi.Type == EnPoiType.Goal) _goalNodeIds.Add(node.Id);
      }
    }

    if (_startNodeIds.Count == 0) return GraphValidationResult.Fail("Graph has no Start POI.");
    if (_exitNodeIds.Count == 0) return GraphValidationResult.Fail("Graph has no Exit POI.");

    return null; // Success: Basic structure of graph is valid.
  }

  /// <summary>
  /// Checks that no secret node hosts a <see cref="EnPoiType.Goal"/> POI - a mandatory
  /// objective must stay findable without knowing about the hidden passage. An
  /// <see cref="EnPoiType.OptionalGoal"/> POI is allowed in a secret node, and multiple
  /// goals in one (non-secret) node are fine.
  /// </summary>
  /// <remarks>Runs after <see cref="Setup"/>, so all POIs are known to be unique.</remarks>
  /// <returns>Null if success, otherwise failure.</returns>
  private GraphValidationResult? CheckGoalSecrets()
  {
    foreach (var node in _graph!.Nodes)
    {
      if (!node.Secret) continue;

      foreach (var poi in node.Pois)
      {
        if (poi.Type == EnPoiType.Goal)
          return GraphValidationResult.Fail($"Goal POI {poi.Id} is in secret node {node.Id}.");
      }
    }

    return null; // Success: No secret contain obligatory goal.
  }

  /// <summary>
  /// Checks edge identity (non-empty, unique) and reference integrity: both endpoints must
  /// exist and differ, and every condition must carry a non-empty <see cref="LilCondition.RequiresPoi"/>
  /// that references an existing POI of type <see cref="EnPoiType.Solution"/>.
  /// </summary>
  /// <remarks>Runs after <see cref="Setup"/>, so every node and POI identifier is known to be
  /// non-empty and unique.</remarks>
  /// <returns>Null if success, otherwise failure.</returns>
  private GraphValidationResult? CheckEdges()
  {
    var edgeIds = new HashSet<Guid>();
    foreach (var edge in _graph!.Edges)
    {
      if (edge.Id == Guid.Empty)
        return GraphValidationResult.Fail($"Edge {edge.Id} has an empty id.");

      if (!edgeIds.Add(edge.Id))
        return GraphValidationResult.Fail($"Duplicate edge id {edge.Id}.");

      if (!_nodesById!.ContainsKey(edge.From))
        return GraphValidationResult.Fail($"Edge {edge.Id} references missing node {edge.From}.");

      if (!_nodesById!.ContainsKey(edge.To))
        return GraphValidationResult.Fail($"Edge {edge.Id} references missing node {edge.To}.");

      if (edge.From == edge.To)
        return GraphValidationResult.Fail($"Edge {edge.Id} connects node to itself.");

      foreach (var condition in edge.Conditions)
      {
        if (condition.RequiresPoi == Guid.Empty)
          return GraphValidationResult.Fail($"Condition on edge {edge.Id} has an empty RequiresPoi.");

        if (!_poiTypes.TryGetValue(condition.RequiresPoi, out var poiType))
          return GraphValidationResult.Fail($"Condition on edge {edge.Id} references missing POI {condition.RequiresPoi}.");

        if (condition.Type == EnConditionType.RequiresSolution && poiType != EnPoiType.Solution)
          return GraphValidationResult.Fail(
            $"Condition on edge {edge.Id} references non-Solution POI {condition.RequiresPoi} (type {poiType}).");
      }
    }
    return null; // Success: All edges are valid.
  }

  /// <summary>
  /// Checks that no node exceeds its <see cref="LilNode.MaxEdges"/> hard cap; a negative
  /// cap means no limit. Counts every relationship touching the node, from either end.
  /// </summary>
  /// <remarks>Runs after <see cref="CheckEdges"/>, so every edge endpoint is known to exist.</remarks>
  /// <returns>Null if success, otherwise failure.</returns>
  private GraphValidationResult? CheckMaxEdges()
  {
    var degrees = new Dictionary<Guid, int>(_graph!.Nodes.Count);
    foreach (var node in _graph.Nodes)
    {
      degrees[node.Id] = 0;
    }

    foreach (var edge in _graph.Edges)
    {
      degrees[edge.From]++;
      degrees[edge.To]++;
    }

    foreach (var node in _graph.Nodes)
    {
      if (node.MaxEdges >= 0 && degrees[node.Id] > node.MaxEdges)
        return GraphValidationResult.Fail(
          $"Node {node.Id} has {degrees[node.Id]} relationships, over its MaxEdges cap of {node.MaxEdges}.");
    }

    return null; // Success: All nodes observe MaxEdges hard cap.
  }

  /// <summary>
  /// Traversal pass: flood from every start node until no new node becomes reachable, then
  /// report the first conditioned passage the player can reach but can never satisfy.
  /// </summary>
  /// <returns>Null if success, otherwise failure.</returns>
  private GraphValidationResult? CheckTraversals()
  {
    var collectedPois = new HashSet<Guid>();

    foreach (var nodeId in _startNodeIds!)
      FloodNode(_flooded, collectedPois, nodeId);

    var changed = true;
    while (changed)
    {
      changed = false;
      foreach (var edge in _graph!.Edges)
      {
        if (!IsPassable(edge, collectedPois)) continue;

        // Forward direction: From -> To, valid for both passage directions.
        if (_flooded.Contains(edge.From) && FloodNode(_flooded, collectedPois, edge.To)) changed = true;

        // Backward direction: only bidirectional passages can be walked against their From -> To sense.
        if (edge.Direction == EnPassageDirection.Bidirectional
            && _flooded.Contains(edge.To)
            && FloodNode(_flooded, collectedPois, edge.From))
          changed = true;
      }
    }

    // Outcome pass, most specific failure first (see type remarks).
    foreach (var edge in _graph!.Edges)
    {
      if (edge.Conditions.Count == 0 || IsPassable(edge, collectedPois)) continue;

      // Only a gate the player can actually stand at is a softlock. A conditioned edge with
      // both sides outside the flooded region lives inside an unreachable branch - there the
      // unreachable-region reasons of CheckExits are the root cause, not the gate.
      if (!IsAtReachableFrontier(edge)) continue;

      return GraphValidationResult.Fail($"Conditioned edge {edge.Id} can never be satisfied.");
    }
    return null; // Success: All nodes are reachable by player.
  }

  /// <summary>
  /// Checks that crossing a reachable one-way passage can never softlock the player. For
  /// every reachable one-way passage the level must stay beatable in at least one of two
  /// ways: the whole level can still be won without ever crossing it (a voluntary dead
  /// end stays legal), or the far side alone still holds everything a winning playthrough
  /// needs - every mandatory <see cref="EnPoiType.Goal"/> POI and an
  /// <see cref="EnPoiType.Exit"/> POI. A forced crossing whose far side never reaches an
  /// exit is reported as a trapped mandatory POI; a forced crossing that leaves a
  /// reachable mandatory goal on the near side keeps the stranded report. Visiting a goal
  /// node does not complete its objective - the player may walk through without doing it -
  /// so crossing the point of no return can leave an incomplete mandatory objective out of
  /// reach while the exit refuses to work.
  /// An <see cref="EnPoiType.OptionalGoal"/> POI never forces a crossing (only non-optional
  /// objectives gate the exit), so one-ways into exit-less dead ends holding only optional
  /// content are allowed.
  /// </summary>
  /// <remarks>
  /// Runs after <see cref="CheckTraversals"/>, so the flooded set holds every
  /// start-reachable node, and before <see cref="CheckExits"/>, so a trapped or stranded
  /// goal is reported ahead of the unreachable-region reasons; the whole check bails out
  /// when no exit is reachable at all, so its reasons never compete with the
  /// unreachable-exit reason. Conditions are respected while deciding whether the level is
  /// winnable without the far side (an openable door matters to a player who stays behind)
  /// but ignored when mapping the far side itself - a gate inside the point of no return
  /// only opens more routes, and a gate that can never be satisfied is already reported by
  /// <see cref="CheckTraversals"/>. That winnable-without-crossing shortfall is only
  /// blamed on the passage when the missed exit or goal is reachable in the main flood but
  /// not without the far side; content unreachable either way belongs to the
  /// <see cref="CheckExits"/> reasons. Edges, nodes and POIs are scanned in list order, so
  /// the first reported problem is deterministic. The cost is linear in the number of
  /// reachable one-way passages, so bulk seed loops stay cheap.
  /// </remarks>
  /// <returns>Null if success, otherwise failure.</returns>
  private GraphValidationResult? CheckGoalBeatability()
  {
    // Without a reachable exit every reason below is moot: CheckExits' unreachable-exit
    // report is the root cause and must own it. This also keeps the reasons of this check
    // mutually exclusive with the unreachable-exit reason.
    if (!ContainsAnyExit(_flooded)) return null;

    // POIs any passage condition points at; only those count as must-collect content.
    var referencedSolutions = new HashSet<Guid>();
    foreach (var edge in _graph!.Edges)
    {
      foreach (var condition in edge.Conditions)
        referencedSolutions.Add(condition.RequiresPoi);
    }

    foreach (var edge in _graph.Edges)
    {
      if (edge.Direction != EnPassageDirection.OneWay) continue;
      if (!_flooded.Contains(edge.From)) continue;

      // Flood the far side structurally to see what a player crossing the point of no
      // return could still reach.
      var farSide = new HashSet<Guid>();
      FloodStructural(farSide, edge.To);
      bool containsAnyExit = ContainsAnyExit(farSide);

      // When one-way dead ends are disallowed, entering a one-way whose far side cannot
      // reach the Exit is always a softlock - even when the dead end holds no important
      // POI and the level stays winnable without crossing it.
      if (!_options.AllowOneWayDeadEnds && !containsAnyExit)
        return TrappedFailure(edge, farSide, referencedSolutions);

      // What stays winnable without ever crossing: condition-aware, with the far side off
      // limits for both standing around and picking anything up.
      var canWin = FloodExcluding(farSide);

      // The level stays winnable without crossing: a voluntary dead end is legal.
      if (IsWinning(canWin)) continue;

      // The shortfall must come from far-side content the main flood could only get by
      // crossing; anything unreachable either way is CheckExits' root cause, not this
      // passage's.
      if (!IsShortfallFromFarSide(canWin)) continue;

      if (!containsAnyExit) return TrappedFailure(edge, farSide, referencedSolutions);

      // The far side holds the exit, so crossing it is forced to finish the level: every
      // mandatory goal the player can currently reach must stay reachable from there.
      foreach (var node in _graph.Nodes)
      {
        if (farSide.Contains(node.Id)) continue;
        // A goal unreachable either way is an unreachable region, not a stranded one.
        if (!_flooded.Contains(node.Id)) continue;

        foreach (var poi in node.Pois)
        {
          if (poi.Type == EnPoiType.Goal)
            return GraphValidationResult.Fail(
              $"Goal POI {poi.Id} is stranded behind one-way passage {edge.Id}.");
        }
      }
    }

    return null; // Success: graph is beatable.
  }

  /// <summary>
  /// Check exits.
  /// </summary>
  /// <returns>Null if success, otherwise failure.</returns>
  private GraphValidationResult? CheckExits()
  {
    if (!ContainsAnyExit(_flooded))
      return GraphValidationResult.Fail("Exit POI is not reachable from Start.");

    var unreachableCount = _graph!.Nodes.Count - _flooded.Count;
    if (unreachableCount > 0)
      return GraphValidationResult.Fail($"{unreachableCount} node(s) unreachable from Start.");
    return null; // Success: Exit POI is reachable from Start POI.
  }

  // ///////////////////////////////////////////////////////////////////////////
  // Helper functions.

  /// <summary>Floods a node and everything structurally reachable from it: bidirectional
  /// passages both ways, one-way passages forward only, conditions ignored and no POIs
  /// collected. Used by the goal-beatability check to model the region beyond a
  /// point of no return.</summary>
  /// <param name="reached">Set of ids of reached nodes.</param>
  /// <param name="startNodeId">Node to flood from.</param>
  private void FloodStructural(HashSet<Guid> reached, Guid startNodeId)
  {
    var pending = new Stack<Guid>();
    pending.Push(startNodeId);

    while (pending.Count > 0)
    {
      var nodeId = pending.Pop();
      if (!reached.Add(nodeId)) continue;

      foreach (var edge in _graph!.Edges)
      {
        if (edge.From == nodeId) pending.Push(edge.To);
        else if (edge.To == nodeId && edge.Direction == EnPassageDirection.Bidirectional)
          pending.Push(edge.From);
      }
    }
  }

  /// <summary>Floods everything a player can reach without ever entering an excluded
  /// node: starts first, then a fixpoint over the passages with the same traversal rules
  /// as the main pass (bidirectional both ways, one-way forward only), conditions enforced
  /// and POIs collected only from non-excluded nodes. Models a playthrough that stays on
  /// the near side of a point of no return.</summary>
  /// <param name="excluded">Node ids the flood must never enter; the far side of a one-way.</param>
  /// <returns>
  /// Ids of the reached nodes; empty when every start node sits inside
  /// <paramref name="excluded"/>. Deterministic: edges are scanned in list order and the
  /// reached set does not depend on hash-set iteration order.
  /// </returns>
  private HashSet<Guid> FloodExcluding(HashSet<Guid> excluded)
  {
    var reached = new HashSet<Guid>();
    var collectedPois = new HashSet<Guid>();

    foreach (var nodeId in _startNodeIds)
    {
      if (excluded.Contains(nodeId)) continue;
      FloodNode(reached, collectedPois, nodeId);
    }

    var changed = true;
    while (changed)
    {
      changed = false;
      foreach (var edge in _graph!.Edges)
      {
        if (!IsPassable(edge, collectedPois)) continue;

        // Forward direction: From -> To, valid for both passage directions.
        if (reached.Contains(edge.From) && !excluded.Contains(edge.To)
            && FloodNode(reached, collectedPois, edge.To))
          changed = true;

        // Backward direction: only bidirectional passages can be walked against their sense.
        if (edge.Direction == EnPassageDirection.Bidirectional
            && reached.Contains(edge.To)
            && !excluded.Contains(edge.From)
            && FloodNode(reached, collectedPois, edge.From))
          changed = true;
      }
    }

    return reached;
  }

  /// <summary>Checks whether a flood state wins the level: at least one exit node was
  /// reached and every mandatory goal node is inside the state.</summary>
  /// <param name="reached">Node ids reached by a flood.</param>
  /// <returns>True when the state can finish the level.</returns>
  private bool IsWinning(HashSet<Guid> reached)
  {
    if (!ContainsAnyExit(reached)) return false;

    foreach (var nodeId in _goalNodeIds)
    {
      if (!reached.Contains(nodeId)) return false;
    }

    return true;
  }

  /// <summary>
  /// Checks whether a flood that excludes the far side falls short because of the far
  /// side: the missed exit or mandatory goal is reachable in the main flood - the main
  /// traversal could only get it by crossing the one-way - yet unreachable without the far
  /// side. Content unreachable in both floods belongs to the unreachable-region reasons of
  /// <see cref="CheckExits"/> and must not be blamed on the passage.
  /// </summary>
  /// <param name="excludedReached">Node ids reached while excluding the far side.</param>
  /// <returns>True when the far side is load-bearing for a winning playthrough.</returns>
  private bool IsShortfallFromFarSide(HashSet<Guid> excludedReached)
  {
    foreach (var nodeId in _exitNodeIds)
    {
      if (_flooded.Contains(nodeId) && !excludedReached.Contains(nodeId)) return true;
    }

    foreach (var nodeId in _goalNodeIds)
    {
      if (_flooded.Contains(nodeId) && !excludedReached.Contains(nodeId)) return true;
    }

    return false;
  }

  /// <summary>Builds the trapped-POI failure for a point of no return whose far side
  /// never reaches an exit: names the first mandatory goal inside the far side in node
  /// list order, else the first condition-referenced solution inside it - the shortfall
  /// attribution guarantees the far side holds at least one of the two - else the passage
  /// alone for degenerate shapes such as a start node sitting beyond the one-way.</summary>
  /// <param name="edge">One-way passage that traps the player.</param>
  /// <param name="farSide">Node ids structurally reachable beyond the passage.</param>
  /// <param name="referencedSolutions">Ids of all POIs referenced by passage conditions.</param>
  /// <returns>A failed result naming the trapped POI.</returns>
  private GraphValidationResult TrappedFailure(
    LilEdge edge,
    HashSet<Guid> farSide,
    HashSet<Guid> referencedSolutions)
  {
    foreach (var node in _graph!.Nodes)
    {
      if (!farSide.Contains(node.Id)) continue;

      foreach (var poi in node.Pois)
      {
        if (poi.Type == EnPoiType.Goal)
          return GraphValidationResult.Fail(
            $"Goal POI {poi.Id} is trapped behind one-way passage {edge.Id} (far side cannot reach the Exit).");
      }
    }

    foreach (var node in _graph.Nodes)
    {
      if (!farSide.Contains(node.Id)) continue;

      foreach (var poi in node.Pois)
      {
        if (poi.Type == EnPoiType.Solution && referencedSolutions.Contains(poi.Id))
          return GraphValidationResult.Fail(
            $"Solution POI {poi.Id} is trapped behind one-way passage {edge.Id} (far side cannot reach the Exit).");
      }
    }

    return GraphValidationResult.Fail(
      $"One-way passage {edge.Id} is a point of no return: the far side cannot reach the Exit.");
  }

  /// <summary>Checks whether any node holding an <see cref="EnPoiType.Exit"/> POI is
  /// contained in the given node id set.</summary>
  /// <param name="nodeIds">Node ids to test.</param>
  /// <returns>True when at least one exit node is present.</returns>
  private bool ContainsAnyExit(HashSet<Guid> nodeIds)
  {
    foreach (var exitId in _exitNodeIds)
    {
      if (nodeIds.Contains(exitId)) return true;
    }

    return false;
  }

  /// <summary>Floods a node (marks it reachable) and collects its POIs; returns false when the node
  /// was already flooded. Collecting on flood models the player picking up everything
  /// inside a place as soon as the place becomes accessible.</summary>
  /// <param name="flooded">Set of ids of flooded nodes.</param>
  /// <param name="collectedPois">Set of ids of found POIs.</param>
  /// <param name="nodeId">Node identifier.</param>
  /// <returns>True if the flood was successful, otherwise false.</returns>
  private bool FloodNode(HashSet<Guid> flooded, HashSet<Guid> collectedPois, Guid nodeId)
  {
    if (!flooded.Add(nodeId)) return false;

    foreach (var poi in _nodesById![nodeId].Pois)
      collectedPois.Add(poi.Id);

    return true;
  }

  /// <summary>
  /// Checks whether the player can reach an entrance of the given passage, mirroring the
  /// traversal rules of the flood pass: the source node always counts, and only a
  /// bidirectional passage can also be encountered from its destination side.
  /// </summary>
  /// <param name="edge">Passage to check.</param>
  /// <returns>True when at least one reachable side of the passage was flooded.</returns>
  private bool IsAtReachableFrontier(LilEdge edge) =>
    _flooded.Contains(edge.From)
    || (edge.Direction == EnPassageDirection.Bidirectional && _flooded.Contains(edge.To));

  /// <summary>
  /// Checks whether a passage can be used given the POIs collected so far.
  /// </summary>
  /// <param name="edge">Passage to check.</param>
  /// <param name="collectedPois">Identifiers of POIs inside already flooded nodes.</param>
  /// <returns>True when every condition of the passage references a collected POI.</returns>
  private static bool IsPassable(LilEdge edge, HashSet<Guid> collectedPois)
  {
    foreach (var condition in edge.Conditions)
    {
      if (!collectedPois.Contains(condition.RequiresPoi)) return false;
    }
    return true;
  }
}
