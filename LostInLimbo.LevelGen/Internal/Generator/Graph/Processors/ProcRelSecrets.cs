using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;

/// <summary>
/// <para>
/// Graph pass that appends secret nodes to the graph. Every secret node hangs off exactly
/// one host node over a secret relationship, so a hidden passage is the only way in. It
/// runs after the builder produced the ordinary graph and before relative size and
/// grouping, so the appended nodes still take part in those passes.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Algorithm: for each of the <see cref="ProcRelSecretsOpt.Amount"/> secrets a host is
/// picked, then a new node (Secret, no POIs) and a new bidirectional chokepoint
/// relationship (Secret, unconditional) from the host to it are appended. The first secret
/// always attaches to a random ordinary node. Every later secret attaches to a random
/// ordinary node too, but with <see cref="ProcRelSecretsOpt.ChanceSecretInSecret"/>
/// probability attaches to a random secret created by this pass instead - over many runs
/// this nests secrets behind secrets (a secret-in-secret tree) rarely. The resulting graph
/// stays connected: every secret node is reachable from its host over an open passage.
/// </para>
/// <para>
/// The hard cap <see cref="LilNode.MaxEdges"/> is observed for every host pick: a node
/// whose current relationship count already reached its cap is never picked (a negative
/// cap means no limit). When the nesting roll hits but every secret is full, the pass
/// falls back to ordinary hosts; when no host of any kind has capacity left, the pass
/// stops early and the graph simply holds fewer secrets than requested.
/// </para>
/// <para>
/// Deterministic for the same graph contents, options and seed: the pass derives its own
/// sub-seed from a dedicated salt, host candidates are enumerated in Id order and both
/// identifiers are derived from the ordinal of the secret, so list insertion order and
/// platform cannot influence the result. Only nodes and edges are appended; nodes, edges,
/// POIs and conditions that already exist stay untouched.
/// </para>
/// </remarks>
internal sealed class ProcRelSecrets
{
  /// <summary>Salt for the pass sub-seed; keeps this pass off other passes' streams.</summary>
  private const long SaltRelSecrets = 0x72656C73656372; // "relsecr"

  /// <summary>Salt for secret node identifiers; one sub-channel per secret ordinal.</summary>
  private const long SaltSecretNode = 0x7365636E6F6465; // "secnode"
  /// <summary>Salt for secret edge identifiers; one sub-channel per secret ordinal.</summary>
  private const long SaltSecretEdge = 0x73656365646765; // "secedge"

  /// <summary>
  /// Initial relative size of every created secret node.
  /// </summary>
  private const int NodeRelSize = 1;

  /// <summary>
  /// Hard cap of created secret nodes: one relationship to the host plus at most two
  /// nested secrets hanging off this one.
  /// </summary>
  private const int SecretMaxEdges = 3;

  /// <summary>Graph that gains secret nodes and edges in place; may be empty.</summary>
  private readonly LilGraph _graph;
  /// <summary>Secret parameters; validated at construction.</summary>
  private readonly ProcRelSecretsOpt _options;
  /// <summary>Random for sub-seed.</summary>
  private readonly SeededRandom _random;
  /// <summary>Graph sub-seed the identifier derivations are based on.</summary>
  private readonly long _seedGraph;

  /// <summary>
  /// Prepares the pass over <paramref name="graph"/>. Validates the options eagerly, so bad
  /// options are rejected even when the graph is empty.
  /// </summary>
  /// <param name="graph">Graph that gains secret nodes and edges in place by <see cref="Execute"/>; may be empty.</param>
  /// <param name="options">Secret parameters; validated before use.</param>
  /// <param name="seedGraph">Graph sub-seed the random stream and identifier derivations are based on.</param>
  /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="options"/> is null.</exception>
  /// <exception cref="ArgumentOutOfRangeException">Options are out of range; see <see cref="ProcRelSecretsOpt.Validate"/>.</exception>
  public ProcRelSecrets(LilGraph graph, ProcRelSecretsOpt options, long seedGraph)
  {
    if (graph is null) throw new ArgumentNullException(nameof(graph));
    if (options is null) throw new ArgumentNullException(nameof(options));

    _graph = graph;
    _options = options;
    _seedGraph = seedGraph;
    _random = new SeededRandom(SeedDerivation.Derive(seedGraph, SaltRelSecrets));
    // Validation runs even for empty graphs, so bad options never pass silently.
    options.Validate();
  }

  /// <summary>
  /// Appends up to <see cref="ProcRelSecretsOpt.Amount"/> secret nodes, each reachable
  /// only over its own secret relationship, to the graph given to the constructor. Stops
  /// early when no node has <see cref="LilNode.MaxEdges"/> capacity left for another
  /// relationship.
  /// </summary>
  public void Execute()
  {
    if (_options.Amount == 0) return; // No secrets to add.
    if (_graph.Nodes.Count == 0) return; // Nothing to attach a secret to.

    // Host candidates are the nodes present on entry, in Id order; secrets created by this
    // pass never become ordinary hosts - nesting only happens through the secret roll.
    var hosts = BuildVisitOrder();
    var degrees = BuildDegrees();
    var secrets = new List<LilNode>(_options.Amount);

    for (var ordinal = 0; ordinal < _options.Amount; ordinal++)
    {
      var parentId = PickParent(hosts, secrets, degrees);
      if (parentId is null) break; // Every node is at its cap; the pass ends early.

      var nodeId = DeriveNodeId(ordinal);

      _graph.Nodes.Add(new LilNode {
        Id = nodeId,
        RelSize = NodeRelSize,
        MaxEdges = SecretMaxEdges,
        Secret = true });
      _graph.Edges.Add(new LilEdge
      {
        Id = DeriveEdgeId(ordinal),
        From = parentId.Value,
        To = nodeId,
        Direction = EnPassageDirection.Bidirectional,
        Chokepoint = true,
        Secret = true,
      });

      // The new relationship fills one slot on both endpoints.
      degrees[parentId.Value]++;
      degrees[nodeId] = 1;

      secrets.Add(_graph.Nodes[^1]);
    }
  }

  //
  // Internals
  //

  /// <summary>
  /// Picks the host of one secret node: a secret created earlier when the
  /// secret-in-secret roll hits and some secret still has capacity, otherwise a random
  /// ordinary node with capacity.
  /// </summary>
  /// <param name="hosts">Ordinary node indices in ascending Id order; not empty.</param>
  /// <param name="secrets">Secret nodes created so far, in creation order.</param>
  /// <param name="degrees">Current relationship count per node identifier.</param>
  /// <returns>
  /// Identifier of the chosen host, or <see langword="null"/> when every candidate sits at
  /// its <see cref="LilNode.MaxEdges"/> cap. Deterministic for the same generator state.
  /// </returns>
  private Guid? PickParent(List<int> hosts, List<LilNode> secrets, Dictionary<Guid, int> degrees)
  {
    // The first secret has nothing to nest behind; NextBool short-circuits at 0 and 1.
    if (secrets.Count > 0 && _random.NextBool(_options.ChanceSecretInSecret))
    {
      var eligibleSecrets = new List<LilNode>(secrets.Count);
      foreach (var secret in secrets)
      {
        if (HasCapacity(secret, degrees)) eligibleSecrets.Add(secret);
      }

      // Every secret is full - fall through to the ordinary hosts.
      if (eligibleSecrets.Count > 0)
        return eligibleSecrets[_random.NextInt(eligibleSecrets.Count)].Id;
    }

    var eligibleHosts = new List<int>(hosts.Count);
    foreach (var index in hosts)
    {
      if (HasCapacity(_graph.Nodes[index], degrees)) eligibleHosts.Add(index);
    }

    if (eligibleHosts.Count == 0) return null; // No capacity left anywhere.

    return _graph.Nodes[eligibleHosts[_random.NextInt(eligibleHosts.Count)]].Id;
  }

  /// <summary>
  /// Checks whether a node may gain one more relationship under its hard cap.
  /// </summary>
  /// <param name="node">Node to check.</param>
  /// <param name="degrees">Current relationship count per node identifier.</param>
  /// <returns>True when the node has capacity; a negative cap always counts as unlimited.</returns>
  private static bool HasCapacity(LilNode node, Dictionary<Guid, int> degrees)
  {
    return node.MaxEdges < 0 || degrees[node.Id] < node.MaxEdges;
  }

  /// <summary>
  /// Counts the relationships touching every node present on entry. Edges referencing a
  /// node that does not exist are ignored - the validator reports those, this pass only
  /// needs degrees for the capacity checks.
  /// </summary>
  /// <returns>Relationship count per node identifier; zero for edge-less graphs.</returns>
  private Dictionary<Guid, int> BuildDegrees()
  {
    var degrees = new Dictionary<Guid, int>(_graph.Nodes.Count);
    foreach (var node in _graph.Nodes)
    {
      degrees[node.Id] = 0;
    }

    foreach (var edge in _graph.Edges)
    {
      if (degrees.ContainsKey(edge.From)) degrees[edge.From]++;
      if (degrees.ContainsKey(edge.To)) degrees[edge.To]++;
    }

    return degrees;
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
  /// Derives a secret node identifier unique per secret, from the graph sub-seed and the
  /// secret ordinal as sub-channel.
  /// </summary>
  /// <param name="ordinal">Zero-based ordinal of the secret within the pass.</param>
  /// <returns>Identifier unique for the run. Not <see cref="Guid.Empty"/>.</returns>
  private Guid DeriveNodeId(int ordinal)
  {
    var subSeed = SeedDerivation.Derive(_seedGraph, SaltSecretNode, ordinal);
    return IdDerivation.Derive(subSeed, SaltSecretNode);
  }

  /// <summary>
  /// Derives a secret edge identifier unique per secret, from the graph sub-seed and the
  /// secret ordinal as sub-channel.
  /// </summary>
  /// <param name="ordinal">Zero-based ordinal of the secret within the pass.</param>
  /// <returns>Identifier unique for the run. Not <see cref="Guid.Empty"/>.</returns>
  private Guid DeriveEdgeId(int ordinal)
  {
    var subSeed = SeedDerivation.Derive(_seedGraph, SaltSecretEdge, ordinal);
    return IdDerivation.Derive(subSeed, SaltSecretEdge);
  }
}
