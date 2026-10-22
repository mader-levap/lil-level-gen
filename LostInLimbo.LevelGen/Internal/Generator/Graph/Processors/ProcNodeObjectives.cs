using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;

/// <summary>
/// <para>
/// Graph pass that places objective POIs onto existing nodes: a configurable number of
/// mandatory goals (<see cref="EnPoiType.Goal"/>) and optional goals
/// (<see cref="EnPoiType.OptionalGoal"/>). A mandatory goal is never placed into a secret
/// node; an optional goal may be, but does not have to be. The pass runs in finalization,
/// right after the pass that adds nodes (<see cref="ProcRelSecrets"/>), so it sees the
/// complete node set and still runs before relative size and grouping.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Algorithm: mandatory goals are placed first, then optional goals. Each phase collects
/// the eligible nodes - nodes that do not already hold a goal POI of either type (a node
/// may hold at most one goal POI in total); the mandatory phase additionally skips secret
/// nodes. The candidates are enumerated in Id order, shuffled once and consumed in order
/// until the requested number is spent or the candidates run out. Candidates running out
/// first is normal: the excess amount is ignored and the pass simply stops early.
/// </para>
/// <para>
/// Nodes already holding Start, Exit or Solution POIs stay eligible, and POIs of any
/// existing pass are never modified - only new POIs are appended.
/// </para>
/// <para>
/// Deterministic for the same graph contents, options and seed: the pass derives its own
/// sub-seed from a dedicated salt, candidates are enumerated in Id order before the
/// shuffle and every POI identifier is derived from the type salt and the ordinal within
/// the phase, so list insertion order and platform cannot influence the result. POI
/// identifiers are unique across the whole graph: the two phases use distinct salts.
/// </para>
/// </remarks>
internal sealed class ProcNodeObjectives
{
  /// <summary>Salt for the pass sub-seed; keeps this pass off other passes' streams.</summary>
  private const long SaltNodeObjectives = 0x6F626A6E6F6465; // "objnode"

  /// <summary>Salt for mandatory goal POI identifiers; one sub-channel per goal ordinal.</summary>
  private const long SaltGoalPoi = 0x676F616C; // "goal"
  /// <summary>Salt for optional goal POI identifiers; one sub-channel per goal ordinal.</summary>
  private const long SaltOptGoalPoi = 0x6F7074476F616C; // "optGoal"

  /// <summary>Graph whose nodes gain objective POIs in place; may be empty.</summary>
  private readonly LilGraph _graph;
  /// <summary>Objective parameters; validated at construction.</summary>
  private readonly CnfProcNodeObjectivesOpts _options;
  /// <summary>Random for sub-seed.</summary>
  private readonly SeededRandom _random;
  /// <summary>Graph sub-seed the identifier derivations are based on.</summary>
  private readonly long _seedGraph;

  /// <summary>
  /// Prepares the pass over <paramref name="graph"/>. Validates the options eagerly, so bad
  /// options are rejected even when the graph is empty.
  /// </summary>
  /// <param name="graph">Graph that gains objective POIs in place by <see cref="Execute"/>; may be empty.</param>
  /// <param name="options">Objective parameters; validated before use.</param>
  /// <param name="seedGraph">Graph sub-seed the random stream and identifier derivations are based on.</param>
  /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="options"/> is null.</exception>
  /// <exception cref="ArgumentOutOfRangeException">Options are out of range; see <see cref="CnfProcNodeObjectivesOpts.Validate"/>.</exception>
  public ProcNodeObjectives(LilGraph graph, CnfProcNodeObjectivesOpts options, long seedGraph)
  {
    if (graph is null) throw new ArgumentNullException(nameof(graph));
    if (options is null) throw new ArgumentNullException(nameof(options));

    _graph = graph;
    _options = options;
    _seedGraph = seedGraph;
    _random = new SeededRandom(SeedDerivation.Derive(seedGraph, SaltNodeObjectives));
    // Validation runs even for empty graphs, so bad options never pass silently.
    options.Validate();
  }

  /// <summary>
  /// Places up to <see cref="CnfProcNodeObjectivesOpts.GoalAmount"/> mandatory goals on
  /// non-secret nodes and up to <see cref="CnfProcNodeObjectivesOpts.OptGoalAmount"/> optional
  /// goals on any node, at most one goal POI per node. Stops early when the eligible nodes
  /// run out; the excess amount is ignored.
  /// </summary>
  public void Execute()
  {
    if (_options.GoalAmount == 0 && _options.OptGoalAmount == 0) return; // Nothing to place.
    if (_graph.Nodes.Count == 0) return; // Nothing to place onto.

    // Mandatory goals claim eligible non-secret nodes first, so optional goals compete
    // only for the nodes that remain.
    PlacePois(EnPoiType.Goal, _options.GoalAmount, allowSecret: false, SaltGoalPoi);
    PlacePois(EnPoiType.OptionalGoal, _options.OptGoalAmount, allowSecret: true, SaltOptGoalPoi);
  }

  //
  // Internals
  //

  /// <summary>
  /// Places up to <paramref name="amount"/> POIs of one type onto distinct eligible nodes.
  /// </summary>
  /// <remarks>
  /// Candidates are built in ascending node Id order and shuffled once, so the choice is
  /// deterministic for the same generator state; running out of candidates ends the phase
  /// early without an error.
  /// </remarks>
  /// <param name="type">POI type to place.</param>
  /// <param name="amount">Requested amount; 0 skips the phase.</param>
  /// <param name="allowSecret">Whether secret nodes are eligible hosts.</param>
  /// <param name="salt">POI identifier salt of this type; distinct per type.</param>
  private void PlacePois(EnPoiType type, int amount, bool allowSecret, long salt)
  {
    if (amount == 0) return;

    var candidates = BuildCandidateIndices(allowSecret);
    if (candidates.Count == 0) return; // Excess amount - no eligible node left.

    _random.Shuffle(candidates);

    var placed = Math.Min(amount, candidates.Count);
    for (var ordinal = 0; ordinal < placed; ordinal++)
    {
      var node = _graph.Nodes[candidates[ordinal]];
      node.Pois.Add(new LilPoi
      {
        Id = DerivePoiId(salt, ordinal),
        Type = type,
      });
    }
  }

  /// <summary>
  /// Collects the indices of the nodes that may host a goal POI: no existing goal POI of
  /// either type (at most one goal per node) and, unless <paramref name="allowSecret"/> is
  /// set, no secret flag. Nodes with other POIs (Start, Exit, Solution) stay eligible.
  /// </summary>
  /// <param name="allowSecret">Whether secret nodes are eligible hosts.</param>
  /// <returns>Candidate indices in ascending node Id order; empty when none qualify.</returns>
  private List<int> BuildCandidateIndices(bool allowSecret)
  {
    var candidates = new List<int>(_graph.Nodes.Count);
    foreach (var index in BuildVisitOrder())
    {
      var node = _graph.Nodes[index];
      if (!allowSecret && node.Secret) continue;
      if (HasGoalPoi(node)) continue;
      candidates.Add(index);
    }

    return candidates;
  }

  /// <summary>
  /// Checks whether a node already hosts a goal POI of either type; such a node never
  /// gains another one.
  /// </summary>
  /// <param name="node">Node to check.</param>
  /// <returns>True when the node holds a <see cref="EnPoiType.Goal"/> or
  /// <see cref="EnPoiType.OptionalGoal"/> POI.</returns>
  private static bool HasGoalPoi(LilNode node)
  {
    foreach (var poi in node.Pois)
    {
      if (poi.Type is EnPoiType.Goal or EnPoiType.OptionalGoal) return true;
    }

    return false;
  }

  /// <summary>
  /// Produces the node indices in ascending Id order.
  /// </summary>
  /// <returns>Node indices sorted by <see cref="LilNode.Id"/>.</returns>
  private List<int> BuildVisitOrder()
  {
    var order = new List<int>(_graph.Nodes.Count);
    for (var i = 0; i < _graph.Nodes.Count; i++)
    {
      order.Add(i);
    }

    order.Sort((left, right) => _graph.Nodes[left].Id.CompareTo(_graph.Nodes[right].Id));
    return order;
  }

  /// <summary>
  /// Derives a goal POI identifier unique per phase and ordinal, from the graph sub-seed
  /// and the POI type salt as channel.
  /// </summary>
  /// <param name="salt">POI identifier salt of this type.</param>
  /// <param name="ordinal">Zero-based ordinal of the POI within the phase.</param>
  /// <returns>Identifier unique for the run. Not <see cref="Guid.Empty"/>.</returns>
  private Guid DerivePoiId(long salt, int ordinal)
  {
    var subSeed = SeedDerivation.Derive(_seedGraph, salt, ordinal);
    return IdDerivation.Derive(subSeed, salt);
  }
}
