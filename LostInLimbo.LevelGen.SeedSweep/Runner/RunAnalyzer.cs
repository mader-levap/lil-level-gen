using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.SeedSweep.Data;

namespace LostInLimbo.LevelGen.SeedSweep.Runner;

/// <summary>
/// Checks one generated world against its config and fills the metrics of a <see cref="RunRecord"/>.
/// </summary>
/// <remarks>
/// Errors are hard rule violations (broken structure, impossible POIs, unreachable nodes, validator
/// failure). Metrics are neutral numbers; the report decides which values look suspicious.
/// </remarks>
internal static class RunAnalyzer
{
  /// <summary>Copies the resolved config values the report compares against.</summary>
  /// <param name="record">Record to fill.</param>
  /// <param name="config">Resolved config of the run.</param>
  public static void FillConfig(RunRecord record, CnfLevelGeneration config)
  {
    if (record is null) throw new ArgumentNullException(nameof(record));
    if (config is null) throw new ArgumentNullException(nameof(config));

    var graph = config.Graph;
    record.Size = graph.Size;
    record.Connectivity = graph.Connectivity;
    record.Complexity = graph.Complexity;
    record.Builder = graph.GraphBuilder.Builder.ToString();
    record.SecretAmount = graph.ProcOpts.RelSecrets.Amount;
    record.GoalAmount = graph.ProcOpts.NodeObjectives.GoalAmount;
    record.OptGoalAmount = graph.ProcOpts.NodeObjectives.OptGoalAmount;
    record.GroupCount = graph.ProcOpts.NodeStyle.GroupCount;
    record.RelSizeSteps = graph.ProcOpts.NodeRelSize.Steps;
  }

  /// <summary>
  /// Analyzes the graph of <paramref name="world"/>. Expects <see cref="FillConfig"/> to have run.
  /// </summary>
  /// <param name="record">Record to fill with metrics and errors.</param>
  /// <param name="config">Resolved config of the run (validator options).</param>
  /// <param name="world">Generated world.</param>
  public static void Analyze(RunRecord record, CnfLevelGeneration config, LilWorld world)
  {
    if (record is null) throw new ArgumentNullException(nameof(record));
    if (config is null) throw new ArgumentNullException(nameof(config));
    if (world is null) throw new ArgumentNullException(nameof(world));

    var graph = world.Graph;
    var pois = new PoiSet(graph);
    CheckStructure(record, graph);
    CheckPois(record, graph, pois);
    CheckConditions(record, graph, pois);
    MeasureKeys(record, graph, pois);

    var validation = LevelGraphValidation.Validate(world, config.Graph.ValidatorOpts);
    if (!validation.Success) record.Error($"validator: {validation.FailureReason}");

    MeasureReach(record, graph, pois);
    MeasureStyles(record, graph);
  }

  #region Structure

  /// <summary>Node and passage counts, ids, degrees and RelSize.</summary>
  private static void CheckStructure(RunRecord record, LilGraph graph)
  {
    var nodes = graph.Nodes;
    var edges = graph.Edges;
    var degree = new Dictionary<Guid, int>();
    foreach (var node in nodes)
      if (!degree.TryAdd(node.Id, 0)) record.Error("duplicate node id");

    record.Nodes = nodes.Count;
    record.SecretNodes = nodes.Count(n => n.Secret);
    record.Edges = edges.Count;
    record.SecretEdges = edges.Count(e => e.Secret);
    record.OneWayEdges = edges.Count(e => e.Direction == EnPassageDirection.OneWay);
    record.ExtraEdges = (edges.Count - record.SecretEdges) - (record.Size - 1);

    var ordinary = nodes.Count - record.SecretNodes;
    if (ordinary != record.Size) record.Error($"ordinary node count {ordinary} != Size {record.Size}");
    if (edges.Select(e => e.Id).Distinct().Count() != edges.Count) record.Error("duplicate edge id");

    foreach (var edge in edges)
    {
      if (!degree.ContainsKey(edge.From) || !degree.ContainsKey(edge.To))
      {
        record.Error("edge references missing node");
        continue;
      }

      if (edge.From == edge.To) record.Error("self-loop edge");
      degree[edge.From]++;
      degree[edge.To]++;
    }

    foreach (var node in nodes)
    {
      var count = degree[node.Id];
      if (node.MaxEdges >= 0 && count > node.MaxEdges) record.Error($"node degree {count} > MaxEdges {node.MaxEdges}");
      if (count == 0 && nodes.Count > 1) record.Error("isolated node");
      if (node.RelSize < 1) record.Error($"RelSize {node.RelSize} < 1");
    }
  }

  #endregion

  #region POIs

  /// <summary>Start/exit uniqueness, goal and solution placement and amounts.</summary>
  private static void CheckPois(RunRecord record, LilGraph graph, PoiSet pois)
  {
    if (pois.All.Select(x => x.Poi.Id).Distinct().Count() != pois.All.Count) record.Error("duplicate POI id");
    if (pois.Starts.Count != 1) record.Error($"start POIs: {pois.Starts.Count}");
    if (pois.Exits.Count != 1) record.Error($"exit POIs: {pois.Exits.Count}");

    record.Solutions = pois.Solutions.Count;
    record.Goals = pois.Goals.Count;
    record.OptGoals = pois.OptGoals.Count;
    record.SharedStartExit = pois.StartNode is { } start && pois.ExitNode is { } exit && start == exit;

    if (pois.Goals.Any(x => x.Node.Secret)) record.Error("mandatory goal in secret node");
    if (pois.Solutions.Any(x => x.Node.Secret)) record.Error("solution in secret node");
    if (graph.Nodes.Any(n => n.Pois.Count(IsGoal) > 1)) record.Error("node with several goal POIs");
    if (record.Solutions > record.Complexity) record.Error($"solutions {record.Solutions} > Complexity {record.Complexity}");
    if (record.Goals > record.GoalAmount) record.Error($"goals {record.Goals} > GoalAmount {record.GoalAmount}");
    if (record.OptGoals > record.OptGoalAmount) record.Error($"opt goals {record.OptGoals} > OptGoalAmount {record.OptGoalAmount}");

    var startExit = new HashSet<Guid>(pois.Starts.Concat(pois.Exits).Select(x => x.Node.Id));
    record.FreeNodes = graph.Nodes.Count(n => !n.Secret && !n.Pois.Any(IsGoal));
    record.GoalsOnStartExit = pois.Goals.Count(x => startExit.Contains(x.Node.Id));
    record.OptGoalsOnStartExit = pois.OptGoals.Count(x => startExit.Contains(x.Node.Id));
    record.SolutionsInStart = pois.StartNode is { } startNode ? pois.Solutions.Count(x => x.Node.Id == startNode) : 0;
    record.GoalsWithSolution = pois.Goals.Count(x => x.Node.Pois.Any(p => p.Type == EnPoiType.Solution));
  }

  /// <summary>True for mandatory and optional goal POIs.</summary>
  private static bool IsGoal(LilPoi poi) => poi.Type is EnPoiType.Goal or EnPoiType.OptionalGoal;

  #endregion

  #region Conditions

  /// <summary>Condition targets, orphan solutions and condition counts.</summary>
  private static void CheckConditions(RunRecord record, LilGraph graph, PoiSet pois)
  {
    var edges = graph.Edges;
    record.ConditionedEdges = edges.Count(e => e.Conditions.Count > 0);
    record.MaxConditionsPerEdge = edges.Count == 0 ? 0 : edges.Max(e => e.Conditions.Count);
    record.StackedEdges = edges.Count(e => e.Conditions.Count > 1);
    record.ConditionedOneWay = edges.Count(e => e.Conditions.Count > 0 && e.Direction == EnPassageDirection.OneWay);
    record.ConditionedSecret = edges.Count(e => e.Conditions.Count > 0 && e.Secret);
    record.ConditionedOneWayNonSecret = edges.Count(e => e.Conditions.Count > 0 && e.Direction == EnPassageDirection.OneWay && !e.Secret);

    foreach (var edge in edges)
    {
      if (edge.Conditions.Select(c => c.RequiresPoi).Distinct().Count() != edge.Conditions.Count)
        record.Error("edge requires same POI twice");

      foreach (var condition in edge.Conditions)
      {
        if (!pois.TypeById.TryGetValue(condition.RequiresPoi, out var type)) record.Error("condition requires missing POI");
        else if (type != EnPoiType.Solution) record.Error($"condition requires {type} POI");
      }
    }

    var required = new HashSet<Guid>(edges.SelectMany(e => e.Conditions).Select(c => c.RequiresPoi));
    record.OrphanSolutions = pois.SolutionIds.Count(id => !required.Contains(id));
    if (record.OrphanSolutions > 0) record.Error($"{record.OrphanSolutions} solution(s) required by no passage");
  }

  /// <summary>Counts solutions lying right next to a passage they unlock.</summary>
  private static void MeasureKeys(RunRecord record, LilGraph graph, PoiSet pois)
  {
    foreach (var (node, poi) in pois.Solutions)
    {
      var nextToDoor = graph.Edges.Any(e =>
        (e.From == node.Id || e.To == node.Id) && e.Conditions.Any(c => c.RequiresPoi == poi.Id));
      if (!nextToDoor) continue;

      record.KeyNextToDoor++;
      if (node.Id == pois.StartNode) record.KeyAtStartDoor++;
    }
  }

  #endregion

  #region Reach

  /// <summary>Unreachable nodes, open reach, exit openness and bypassed obstacles.</summary>
  private static void MeasureReach(RunRecord record, LilGraph graph, PoiSet pois)
  {
    if (pois.Starts.Count != 1) return;

    var startPoi = pois.Starts[0].Poi.Id;
    var allSolutions = pois.SolutionIds;
    var full = LevelGraphReach.GetReachableNodes(graph, startPoi, allSolutions).ToHashSet();
    var unreachable = graph.Nodes.Where(n => !full.Contains(n.Id)).ToList();
    if (unreachable.Count > 0)
      record.Error($"{unreachable.Count} node(s) unreachable with all solutions ({unreachable.Count(n => n.Secret)} secret)");

    var open = LevelGraphReach.GetReachableNodes(graph, startPoi, []).ToHashSet();
    record.OpenReachShare = graph.Nodes.Count == 0 ? 0 : (double)open.Count / graph.Nodes.Count;
    record.ExitOpen = pois.ExitNode is { } exit && open.Contains(exit);
    record.GoalsOpen = pois.Goals.Count(x => open.Contains(x.Node.Id));

    // Bypassed obstacle: with every other solution held, the same nodes are reachable.
    foreach (var solution in allSolutions)
    {
      var others = allSolutions.Where(id => id != solution).ToList();
      if (LevelGraphReach.GetReachableNodes(graph, startPoi, others).Count == full.Count) record.UselessObstacles++;
    }

    var openOrdinary = ReachOrdinary(graph, pois.StartNode!.Value);
    record.ExitOpenNoSecret = pois.ExitNode is { } exitNode && openOrdinary.Contains(exitNode);
  }

  /// <summary>
  /// Nodes reachable from <paramref name="startNode"/> holding no solution, over ordinary
  /// (non-secret) passages only: what a player who has not found any secret can reach.
  /// </summary>
  /// <param name="graph">Graph to walk.</param>
  /// <param name="startNode">Node to start from.</param>
  /// <returns>Reached node ids including <paramref name="startNode"/>.</returns>
  internal static HashSet<Guid> ReachOrdinary(LilGraph graph, Guid startNode)
  {
    var seen = new HashSet<Guid> { startNode };
    var queue = new Queue<Guid>();
    queue.Enqueue(startNode);
    while (queue.Count > 0)
    {
      var node = queue.Dequeue();
      foreach (var edge in graph.Edges)
      {
        if (edge.Secret || edge.Conditions.Count > 0) continue;

        Guid next;
        if (edge.From == node) next = edge.To;
        else if (edge.To == node && edge.Direction == EnPassageDirection.Bidirectional) next = edge.From;
        else continue;

        if (seen.Add(next)) queue.Enqueue(next);
      }
    }

    return seen;
  }

  #endregion

  #region Styles

  /// <summary>Style groups and RelSize spread.</summary>
  private static void MeasureStyles(RunRecord record, LilGraph graph)
  {
    var nodes = graph.Nodes;
    record.DistinctGroups = nodes.Select(n => n.Style.Group ?? "<null>").Distinct().Count();
    record.NullGroupNodes = nodes.Count(n => n.Style.Group is null);
    if (record.DistinctGroups > record.GroupCount)
      record.Error($"distinct groups {record.DistinctGroups} > GroupCount {record.GroupCount}");

    record.RelSizeMin = nodes.Count == 0 ? 0 : nodes.Min(n => n.RelSize);
    record.RelSizeMax = nodes.Count == 0 ? 0 : nodes.Max(n => n.RelSize);
  }

  #endregion

  /// <summary>POIs of a graph, grouped by type, with their nodes.</summary>
  private sealed class PoiSet
  {
    /// <summary>Collects the POIs of <paramref name="graph"/>.</summary>
    public PoiSet(LilGraph graph)
    {
      All = graph.Nodes.SelectMany(n => n.Pois.Select(p => (Node: n, Poi: p))).ToList();
      Starts = Of(EnPoiType.Start);
      Exits = Of(EnPoiType.Exit);
      Solutions = Of(EnPoiType.Solution);
      Goals = Of(EnPoiType.Goal);
      OptGoals = Of(EnPoiType.OptionalGoal);
      SolutionIds = Solutions.Select(x => x.Poi.Id).ToList();
      TypeById = All.GroupBy(x => x.Poi.Id).ToDictionary(g => g.Key, g => g.First().Poi.Type);
      StartNode = Starts.Count == 1 ? Starts[0].Node.Id : null;
      ExitNode = Exits.Count == 1 ? Exits[0].Node.Id : null;
    }

    /// <summary>Every POI with its node.</summary>
    public List<(LilNode Node, LilPoi Poi)> All { get; }
    /// <summary>Start POIs.</summary>
    public List<(LilNode Node, LilPoi Poi)> Starts { get; }
    /// <summary>Exit POIs.</summary>
    public List<(LilNode Node, LilPoi Poi)> Exits { get; }
    /// <summary>Solution POIs.</summary>
    public List<(LilNode Node, LilPoi Poi)> Solutions { get; }
    /// <summary>Mandatory goal POIs.</summary>
    public List<(LilNode Node, LilPoi Poi)> Goals { get; }
    /// <summary>Optional goal POIs.</summary>
    public List<(LilNode Node, LilPoi Poi)> OptGoals { get; }
    /// <summary>Ids of the solution POIs, in graph order.</summary>
    public List<Guid> SolutionIds { get; }
    /// <summary>POI type by id (first one wins on duplicate ids).</summary>
    public Dictionary<Guid, EnPoiType> TypeById { get; }
    /// <summary>Node of the single start POI; null when there is not exactly one.</summary>
    public Guid? StartNode { get; }
    /// <summary>Node of the single exit POI; null when there is not exactly one.</summary>
    public Guid? ExitNode { get; }

    /// <summary>POIs of one type.</summary>
    private List<(LilNode Node, LilPoi Poi)> Of(EnPoiType type) => All.Where(x => x.Poi.Type == type).ToList();
  }
}
