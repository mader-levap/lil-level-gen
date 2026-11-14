using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler;

/// <summary>
/// State shared by the passes of the obstacle placer, and the one place where an obstacle is
/// actually written into the graph.
/// </summary>
/// <remarks>
/// <para>
/// <b>Softlock rule.</b> <see cref="TryPlaceObstacle"/> first locks the passages, then floods
/// from the start with the new solution blocked, and only then picks the solution node among
/// the nodes that flood reached. The solution is therefore always obtainable before any of its
/// passages is needed, so the graph stays fully reachable after every obstacle.
/// </para>
/// <para>
/// That flood is stricter than the validator: it ignores secret passages (progress never
/// requires finding a secret) and one-way passages (the player can always walk back from the
/// solution).
/// </para>
/// <para>
/// Deterministic: one random stream derived from the graph sub-seed with a dedicated salt,
/// solution identifiers derived from the obstacle ordinal.
/// </para>
/// </remarks>
internal sealed class ObstacleState
{
  /// <summary>Salt for the processor sub-seed; keeps it off other processors' streams.</summary>
  private const long SaltObstaclePlacer = 0x6F6273746163; // "obstac"
  /// <summary>Salt for solution POI identifiers; one sub-channel per obstacle ordinal.</summary>
  private const long SaltSolutionPoi = 0x6F627374736F6C; // "obstsol"

  /// <summary>Graph sub-seed the identifier derivations are based on.</summary>
  private readonly long _seedGraph;
  /// <summary>Flood over two-way, non-secret passages; reads conditions and POIs live.</summary>
  private readonly GraphFlood _flood;
  /// <summary>Every solution POI in the graph; what the flood may pick up.</summary>
  private readonly HashSet<Guid> _solutionIds = [];
  /// <summary>Solution of the most recently placed obstacle; empty before the first one.</summary>
  private Guid _lastSolutionId = Guid.Empty;

  /// <summary>
  /// Prepares the shared state for one run of the obstacle placer.
  /// </summary>
  /// <param name="graph">Graph that receives obstacles in place.</param>
  /// <param name="options">Obstacle placer options; expected to be validated.</param>
  /// <param name="seedGraph">Graph sub-seed the random stream and solution identifiers derive from.</param>
  /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="options"/> is null.</exception>
  /// <exception cref="ArgumentException">Two nodes share an identifier.</exception>
  /// <exception cref="InvalidOperationException">A passage references a missing node.</exception>
  public ObstacleState(LilGraph graph, CnfProcObstaclePlacerOpt options, long seedGraph)
  {
    if (graph is null) throw new ArgumentNullException(nameof(graph));
    if (options is null) throw new ArgumentNullException(nameof(options));

    _seedGraph = seedGraph;
    Options = options;
    Work = new ObstacleWorkGraph(graph, options);
    Random = new SeededRandom(SeedDerivation.Derive(seedGraph, SaltObstaclePlacer));
    Search = new CutSearch(Work, options.MaxEdgesPerObstacle);
    Picker = new SolutionPicker(Work, Random);
    _flood = new GraphFlood(graph, edge => !edge.Secret && edge.Direction == EnPassageDirection.Bidirectional);

    // Solutions that exist already keep working: the flood must be able to collect them.
    foreach (var node in graph.Nodes)
    {
      foreach (var poi in node.Pois)
      {
        if (poi.Type == EnPoiType.Solution) _solutionIds.Add(poi.Id);
      }
    }
  }

  /// <summary>Obstacle placer options.</summary>
  public CnfProcObstaclePlacerOpt Options { get; }

  /// <summary>Index view of the graph.</summary>
  public ObstacleWorkGraph Work { get; }

  /// <summary>Random stream of the obstacle placer, shared by all passes.</summary>
  public SeededRandom Random { get; }

  /// <summary>Candidate cut search over <see cref="Work"/>.</summary>
  public CutSearch Search { get; }

  /// <summary>Solution node picker over <see cref="Work"/>.</summary>
  public SolutionPicker Picker { get; }

  /// <summary>Number of obstacles placed so far by all passes.</summary>
  public int ObstacleCount { get; private set; }

  /// <summary>
  /// Places one obstacle: the same new condition on every given passage, and its solution in
  /// a node that is reachable without it.
  /// </summary>
  /// <param name="lockEdges">Work indices of the passages to lock; none may be at the condition cap.</param>
  /// <param name="excludedSolutionNodes">Nodes that must not receive the solution; null excludes nothing.</param>
  /// <param name="solutionNode">
  /// Work index of the node that received the solution, or <see cref="ObstacleWorkGraph.NoNode"/> on failure.
  /// </param>
  /// <param name="allowOneWay">
  /// True to accept one-way passages (entries of a side pocket); otherwise they are rejected
  /// like passages at the condition cap.
  /// </param>
  /// <returns>
  /// True when the obstacle was placed. False when no allowed node can hold the solution; the
  /// graph is then left exactly as it was.
  /// </returns>
  /// <exception cref="ArgumentNullException"><paramref name="lockEdges"/> is null.</exception>
  /// <exception cref="ArgumentException"><paramref name="lockEdges"/> is empty.</exception>
  /// <exception cref="InvalidOperationException">A passage cannot take another condition.</exception>
  public bool TryPlaceObstacle(
    IReadOnlyList<int> lockEdges,
    bool[]? excludedSolutionNodes,
    out int solutionNode,
    bool allowOneWay = false)
  {
    if (lockEdges is null) throw new ArgumentNullException(nameof(lockEdges));
    if (lockEdges.Count == 0) throw new ArgumentException("An obstacle must lock at least one passage.", nameof(lockEdges));
    foreach (var edge in lockEdges)
    {
      if (!(allowOneWay ? Work.HasConditionRoom(edge) : Work.IsLockable(edge)))
        throw new InvalidOperationException($"Passage {Work.GetEdge(edge).Id} cannot take another condition.");
    }

    var solutionId = DeriveSolutionId(ObstacleCount);
    var condition = new LilCondition { Type = EnConditionType.ReqSolution, RequiresPoi = solutionId };

    // Lock first, so the flood sees the graph as it will be.
    foreach (var edge in lockEdges) Work.GetEdge(edge).Conditions.Add(condition);

    solutionNode = Picker.Pick(FloodWithout(solutionId), lockEdges, excludedSolutionNodes);
    if (solutionNode == ObstacleWorkGraph.NoNode)
    {
      // Undo: the condition was appended last on every passage.
      foreach (var edge in lockEdges)
      {
        var conditions = Work.GetEdge(edge).Conditions;
        conditions.RemoveAt(conditions.Count - 1);
      }

      return false;
    }

    Work.GetNode(solutionNode).Pois.Add(new LilPoi { Id = solutionId, Type = EnPoiType.Solution });
    _solutionIds.Add(solutionId);
    _lastSolutionId = solutionId;
    ObstacleCount++;
    return true;
  }

  /// <summary>
  /// Takes the condition of the most recently placed obstacle off one of its passages again:
  /// a bypass, a deliberately open way around that obstacle.
  /// </summary>
  /// <remarks>
  /// Always safe: opening a passage can only add ways through the graph. The caller keeps the
  /// obstacle meaningful by leaving at least one of its passages locked.
  /// </remarks>
  /// <param name="edge">Work index of a passage locked by the most recent obstacle.</param>
  /// <exception cref="InvalidOperationException">
  /// No obstacle was placed yet, or the passage does not carry the condition of the most recent one.
  /// </exception>
  public void ReopenPassage(int edge)
  {
    if (ObstacleCount == 0) throw new InvalidOperationException("No obstacle was placed yet.");

    var conditions = Work.GetEdge(edge).Conditions;
    var index = conditions.FindIndex(condition => condition.RequiresPoi == _lastSolutionId);
    if (index < 0)
      throw new InvalidOperationException($"Passage {Work.GetEdge(edge).Id} is not locked by the most recent obstacle.");

    conditions.RemoveAt(index);
  }

  /// <summary>
  /// Floods from the start over two-way, non-secret passages, collecting every solution
  /// except the blocked one.
  /// </summary>
  /// <param name="blockedSolution">Solution treated as never held.</param>
  /// <returns>Flags per node work index: reachable without the blocked solution.</returns>
  private bool[] FloodWithout(Guid blockedSolution)
  {
    var startId = Work.GetNode(Work.StartNode).Id;
    var (reached, _) = _flood.Run(startId, Array.Empty<Guid>(), _solutionIds, blockedSolution);

    var reachable = new bool[Work.NodeCount];
    for (var node = 0; node < reachable.Length; node++)
    {
      reachable[node] = reached.Contains(Work.GetNode(node).Id);
    }

    return reachable;
  }

  /// <summary>
  /// Derives a solution POI identifier unique per obstacle, from the graph sub-seed and the
  /// obstacle ordinal as sub-channel.
  /// </summary>
  /// <param name="ordinal">Zero-based ordinal of the obstacle within the run.</param>
  /// <returns>Identifier unique for the run. Not <see cref="Guid.Empty"/>.</returns>
  private Guid DeriveSolutionId(int ordinal)
  {
    var subSeed = SeedDerivation.Derive(_seedGraph, SaltSolutionPoi, ordinal);
    return IdDerivation.Derive(subSeed, SaltSolutionPoi);
  }
}
