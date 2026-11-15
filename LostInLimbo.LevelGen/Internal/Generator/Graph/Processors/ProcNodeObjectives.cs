using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;

/// <summary>
/// <para>
/// Graph pass that places objective POIs onto existing nodes: a configurable number of
/// mandatory goals (<see cref="EnPoiType.Goal"/>) and optional goals
/// (<see cref="EnPoiType.OptionalGoal"/>). A mandatory goal is never placed into a secret
/// node; an optional goal may be, but does not have to be. The pass runs in processor pass,
/// right after the pass that adds nodes (<see cref="ProcRelSecrets"/>), so it sees the
/// complete node set and still runs before relative size and grouping.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Algorithm: mandatory goals are placed first, then optional goals. Each phase collects
/// the eligible nodes - nodes that do not already hold a goal POI of either type (a node
/// may hold at most one goal POI in total); the mandatory phase additionally skips secret
/// nodes. The candidates are enumerated in Id order and picked one by one by weighted
/// sampling without replacement until the requested number is spent or the candidates run
/// out. Candidates running out first is normal: the excess amount is ignored and the pass
/// simply stops early.
/// </para>
/// <para>
/// Nodes already holding POIs stay eligible but are less likely hosts: a node without POIs
/// uses <see cref="CnfProcNodeObjectivesOpt.WeightNoPoi"/>, a node with other POIs (e.g.
/// Solution) <see cref="CnfProcNodeObjectivesOpt.WeightOtherPoi"/> and a node with Start or
/// Exit <see cref="CnfProcNodeObjectivesOpt.WeightStartExitPoi"/>. POIs of any existing
/// pass are never modified - only new POIs are appended.
/// </para>
/// <para>
/// Deterministic for the same graph contents, options and seed: the pass derives its own
/// sub-seed from a dedicated salt, candidates are enumerated in Id order before sampling
/// and every POI identifier is derived from the type salt and the ordinal within
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
  private readonly CnfProcNodeObjectivesOpt _options;
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
  /// <exception cref="ArgumentOutOfRangeException">Options are out of range; see <see cref="CnfProcNodeObjectivesOpt.Validate"/>.</exception>
  public ProcNodeObjectives(LilGraph graph, CnfProcNodeObjectivesOpt options, long seedGraph)
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
  /// Places up to <see cref="CnfProcNodeObjectivesOpt.GoalAmount"/> mandatory goals on
  /// non-secret nodes and up to <see cref="CnfProcNodeObjectivesOpt.OptGoalAmount"/> optional
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
  /// Weighted sampling without replacement: candidates are built in ascending node Id order
  /// with their weights (<see cref="ComputeWeight"/>), then each placement rolls against the
  /// remaining total and removes the picked candidate. Deterministic for the same generator
  /// state; running out of candidates ends the phase early without an error.
  /// </remarks>
  /// <param name="type">POI type to place.</param>
  /// <param name="amount">Requested amount; 0 skips the phase.</param>
  /// <param name="allowSecret">Whether secret nodes are eligible hosts.</param>
  /// <param name="salt">POI identifier salt of this type; distinct per type.</param>
  private void PlacePois(EnPoiType type, int amount, bool allowSecret, long salt)
  {
    if (amount == 0) return;

    var candidates = BuildCandidates(allowSecret, out var totalWeight);

    // Candidates running out first is normal: the excess amount is ignored.
    for (var ordinal = 0; ordinal < amount && candidates.Count > 0; ordinal++)
    {
      var pick = PickWeighted(candidates, totalWeight);
      totalWeight -= candidates[pick].Weight;
      var node = _graph.Nodes[candidates[pick].Index];
      candidates.RemoveAt(pick);

      node.Pois.Add(new LilPoi
      {
        Id = DerivePoiId(salt, ordinal),
        Type = type,
      });
    }
  }

  /// <summary>
  /// Collects the nodes that may host a goal POI, with their pick weights: no existing goal
  /// POI of either type (at most one goal per node) and, unless <paramref name="allowSecret"/>
  /// is set, no secret flag. Nodes with other POIs (Start, Exit, Solution) stay eligible at
  /// a reduced weight.
  /// </summary>
  /// <param name="allowSecret">Whether secret nodes are eligible hosts.</param>
  /// <param name="totalWeight">Sum of all candidate weights; 0 when none qualify.</param>
  /// <returns>Candidate node indices and weights in ascending node Id order; empty when none qualify.</returns>
  private List<(int Index, int Weight)> BuildCandidates(bool allowSecret, out int totalWeight)
  {
    var candidates = new List<(int Index, int Weight)>(_graph.Nodes.Count);
    totalWeight = 0;
    foreach (var index in GraphIndex.NodeVisitOrder(_graph))
    {
      var node = _graph.Nodes[index];
      if (!allowSecret && node.Secret) continue;
      if (HasGoalPoi(node)) continue;

      var weight = ComputeWeight(node, _options);
      candidates.Add((index, weight));
      totalWeight += weight;
    }

    return candidates;
  }

  /// <summary>
  /// Picks one candidate with a chance proportional to its weight.
  /// </summary>
  /// <param name="candidates">Non-empty candidate list; every weight is at least 1.</param>
  /// <param name="totalWeight">Sum of the candidate weights.</param>
  /// <returns>Position of the picked candidate within <paramref name="candidates"/>.</returns>
  /// <exception cref="InvalidOperationException">The total does not match the candidate weights.</exception>
  private int PickWeighted(List<(int Index, int Weight)> candidates, int totalWeight)
  {
    var roll = _random.NextInt(totalWeight);
    for (var position = 0; position < candidates.Count; position++)
    {
      roll -= candidates[position].Weight;
      if (roll < 0) return position;
    }

    // Unreachable: the roll is always below the total weight.
    throw new InvalidOperationException("Weighted goal host pick ran past its candidate list.");
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
    return node.HasPoi(EnPoiType.Goal) || node.HasPoi(EnPoiType.OptionalGoal);
  }

  /// <summary>
  /// Computes the relative pick weight of an eligible host node: a Start or Exit POI gives
  /// <see cref="CnfProcNodeObjectivesOpt.WeightStartExitPoi"/> (wins over other POIs), any
  /// other POI gives <see cref="CnfProcNodeObjectivesOpt.WeightOtherPoi"/>, no POI gives
  /// <see cref="CnfProcNodeObjectivesOpt.WeightNoPoi"/>.
  /// </summary>
  /// <param name="node">Candidate node; never holds a goal POI when called by the pass.</param>
  /// <param name="options">Options providing the weights.</param>
  /// <returns>Weight of at least 1 for validated options.</returns>
  internal static int ComputeWeight(LilNode node, CnfProcNodeObjectivesOpt options)
  {
    if (node.HasPoi(EnPoiType.Start) || node.HasPoi(EnPoiType.Exit)) return options.WeightStartExitPoi;
    if (node.Pois.Count > 0) return options.WeightOtherPoi;
    return options.WeightNoPoi;
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
