using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;

/// <summary>
/// <para>
/// Graph pass that appends secret nodes to the graph. Every secret node hangs off exactly
/// one host node over a secret entry passage, so reaching the secret means knowing where
/// it is; a rare secret may also carry a second (exit) passage onward. It runs after the
/// builder produced the ordinary graph and before relative size and grouping, so the
/// appended nodes still take part in those passes.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Algorithm: for each of the <see cref="CnfProcRelSecretsOpt.Amount"/> secrets a host is
/// picked, then a new node (Secret, no POIs) and a new relationship from the host to it
/// are appended. The first secret always attaches to a random ordinary node. Every later
/// secret attaches to a random ordinary node too, but with
/// <see cref="CnfProcRelSecretsOpt.ChanceSecretInSecret"/> probability attaches to a
/// random secret created by this pass instead - over many runs this nests secrets behind
/// secrets (a secret-in-secret tree) rarely. The resulting graph stays connected: every
/// secret node is reachable from its host over an open passage.
/// </para>
/// <para>
/// With <see cref="CnfProcRelSecretsOpt.ChanceTwoPassages"/> probability a secret gets two
/// passages instead of one: an entry passage from the host and an exit passage to another
/// node. With <see cref="CnfProcRelSecretsOpt.ChanceReturnToHost"/> probability the exit
/// leads back to the host itself (<c>A -&gt; S -&gt; A</c>), both passages one way; this
/// needs two free relationship slots on the host. Otherwise the exit node is picked by
/// weight from the ordinary nodes within <see cref="CnfProcRelSecretsOpt.MaxExitDistance"/>
/// relationships of the host, ignoring passage direction; nearer nodes carry proportionally
/// higher weight. Whether conditional passages may be crossed while searching for the exit
/// is rolled once per secret (<see cref="CnfProcRelSecretsOpt.ChanceIgnoreConditionals"/>),
/// so one secret either allows such shortcuts or not at all. When no eligible exit node
/// exists, the pass falls back to the normal single-passage secret.
/// </para>
/// <para>
/// The hard cap <see cref="LilNode.MaxEdges"/> is observed for every host pick and every
/// passage endpoint: a node whose current relationship count already reached its cap is
/// never picked (a negative cap means no limit). When the nesting roll hits but every
/// secret is full, the pass falls back to ordinary hosts; when no host of any kind has
/// capacity left, the pass stops early and the graph simply holds fewer secrets than
/// requested.
/// </para>
/// <para>
/// Deterministic for the same graph contents, options and seed: the pass derives its own
/// sub-seed from a dedicated salt, host and exit candidates are enumerated in Id order and
/// all identifiers are derived from the ordinal of the secret, so list insertion order and
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
  /// Salt for the exit-edge identifiers of two-passage secrets; one sub-channel per secret
  /// ordinal, kept apart from <see cref="SaltSecretEdge"/> so the two identifiers of one
  /// secret can never collide.
  /// </summary>
  private const long SaltSecretExitEdge = 0x73656365786974; // "secexit"

  /// <summary>
  /// Initial relative size of every created secret node.
  /// </summary>
  private const int NodeRelSize = 1;

  /// <summary>Graph that gains secret nodes and edges in place; may be empty.</summary>
  private readonly LilGraph _graph;
  /// <summary>Secret parameters; validated at construction.</summary>
  private readonly CnfProcRelSecretsOpt _options;
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
  /// <exception cref="ArgumentOutOfRangeException">Options are out of range; see <see cref="CnfProcRelSecretsOpt.Validate"/>.</exception>
  public ProcRelSecrets(LilGraph graph, CnfProcRelSecretsOpt options, long seedGraph)
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
  /// Appends up to <see cref="CnfProcRelSecretsOpt.Amount"/> secret nodes to the graph
  /// given to the constructor: each over its entry passage from the host, plus an exit
  /// passage when the two-passage layout is rolled and an eligible exit exists. Stops
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
      var parent = PickParent(hosts, secrets, degrees);
      if (parent is null) break; // Every node is at its cap; the pass ends early.

      var nodeId = DeriveNodeId(ordinal);

      // Decide the passage layout before anything is appended, so a two-passage search
      // that finds no eligible exit simply falls back to the single-passage secret.
      var returnToHost = false;
      Guid? exitId = null;
      if (_random.NextBool(_options.ChanceTwoPassages))
      {
        // Rolled once per secret: either conditional passages may be crossed on the exit
        // path or none at all.
        var ignoreConditionals = _random.NextBool(_options.ChanceIgnoreConditionals);

        if (_random.NextBool(_options.ChanceReturnToHost) && HasCapacity(parent, degrees, 2))
          returnToHost = true;
        else exitId = FindExitNode(parent.Id, hosts, degrees, ignoreConditionals);
      }

      _graph.Nodes.Add(new LilNode {
        Id = nodeId,
        RelSize = NodeRelSize,
        MaxEdges = _options.MaxEdges,
        Secret = true });
      _graph.Edges.Add(new LilEdge
      {
        Id = DeriveEdgeId(ordinal),
        From = parent.Id,
        To = nodeId,
        // The A -> S -> A layout is a one-way detour in and out; every other layout is
        // an open two-way passage.
        Direction = returnToHost ? EnPassageDirection.OneWay : EnPassageDirection.Bidirectional,
        Chokepoint = true,
        Secret = true,
      });

      // The exit passage: back to the host (one way, closing the detour) or onward to a
      // nearby ordinary node (two way). Both are secret chokepoint passages without
      // conditions, like the entry passage.
      if (returnToHost)
      {
        _graph.Edges.Add(new LilEdge
        {
          Id = DeriveExitEdgeId(ordinal),
          From = nodeId,
          To = parent.Id,
          Direction = EnPassageDirection.OneWay,
          Chokepoint = true,
          Secret = true,
        });
      }
      else if (exitId is Guid exit)
      {
        _graph.Edges.Add(new LilEdge
        {
          Id = DeriveExitEdgeId(ordinal),
          From = nodeId,
          To = exit,
          Direction = EnPassageDirection.Bidirectional,
          Chokepoint = true,
          Secret = true,
        });
        degrees[exit]++;
      }

      // The new relationships fill slots on both endpoints.
      degrees[parent.Id] += returnToHost ? 2 : 1;
      degrees[nodeId] = returnToHost || exitId is not null ? 2 : 1;

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
  /// The chosen host, or <see langword="null"/> when every candidate sits at its
  /// <see cref="LilNode.MaxEdges"/> cap. Deterministic for the same generator state.
  /// </returns>
  private LilNode? PickParent(List<int> hosts, List<LilNode> secrets, Dictionary<Guid, int> degrees)
  {
    // The first secret has nothing to nest behind; NextBool short-circuits at 0 and 1.
    if (secrets.Count > 0 && _random.NextBool(_options.ChanceSecretInSecret))
    {
      var eligibleSecrets = new List<LilNode>(secrets.Count);
      foreach (var secret in secrets)
      {
        if (HasCapacity(secret, degrees)) eligibleSecrets.Add(secret);
      }

      // Nest behind an eligible secret while any of them has capacity.
      if (eligibleSecrets.Count > 0)
        return eligibleSecrets[_random.NextInt(eligibleSecrets.Count)];
    }

    var eligibleHosts = new List<int>(hosts.Count);
    foreach (var index in hosts)
    {
      if (HasCapacity(_graph.Nodes[index], degrees)) eligibleHosts.Add(index);
    }

    if (eligibleHosts.Count == 0) return null; // No capacity left anywhere.

    return _graph.Nodes[eligibleHosts[_random.NextInt(eligibleHosts.Count)]];
  }

  /// <summary>
  /// Checks whether a node may gain one more relationship under its hard cap.
  /// </summary>
  /// <param name="node">Node to check.</param>
  /// <param name="degrees">Current relationship count per node identifier.</param>
  /// <returns>True when the node has capacity; a negative cap always counts as unlimited.</returns>
  private static bool HasCapacity(LilNode node, Dictionary<Guid, int> degrees)
  {
    return HasCapacity(node, degrees, 1);
  }

  /// <summary>
  /// Checks whether a node may gain the given number of additional relationships under
  /// its hard cap - used when one secret needs more than one slot on the same node.
  /// </summary>
  /// <param name="node">Node to check.</param>
  /// <param name="degrees">Current relationship count per node identifier.</param>
  /// <param name="slots">Number of additional relationships the node should gain; must be positive.</param>
  /// <returns>True when the node has enough capacity; a negative cap always counts as unlimited.</returns>
  private static bool HasCapacity(LilNode node, Dictionary<Guid, int> degrees, int slots)
  {
    return node.MaxEdges < 0 || degrees[node.Id] + slots <= node.MaxEdges;
  }

  /// <summary>
  /// Picks the exit node of a two-passage secret: an ordinary node within
  /// <see cref="CnfProcRelSecretsOpt.MaxExitDistance"/> relationships of the host, ignoring
  /// passage direction, that still has a free relationship slot. Candidates are weighted so
  /// the chance falls linearly with distance (nodes at the same distance share a weight).
  /// </summary>
  /// <param name="hostId">Host node the entry passage starts from.</param>
  /// <param name="hosts">Ordinary node indices in ascending Id order; secret nodes created by this pass are never candidates.</param>
  /// <param name="degrees">Current relationship count per node identifier.</param>
  /// <param name="ignoreConditionals">True when conditional passages may be crossed while measuring distance.</param>
  /// <returns>
  /// Identifier of the picked exit node, or <see langword="null"/> when no eligible node
  /// exists - the caller then falls back to a single-passage secret. Deterministic for the
  /// same generator state.
  /// </returns>
  private Guid? FindExitNode(Guid hostId, List<int> hosts, Dictionary<Guid, int> degrees, bool ignoreConditionals)
  {
    var distances = FloodDistances(hostId, ignoreConditionals);

    // Candidates in ascending Id order with integer weights: the nearest node carries the
    // highest weight, the farthest (at MaxExitDistance) still carries one.
    var candidates = new List<(Guid Id, int Weight)>(hosts.Count);
    var totalWeight = 0;
    foreach (var index in hosts)
    {
      var node = _graph.Nodes[index];
      if (node.Id == hostId) continue; // The secret returns to the host only via the return layout.
      if (!distances.TryGetValue(node.Id, out var distance)) continue; // Not reachable under the current rules.
      if (!HasCapacity(node, degrees)) continue; // No free relationship slot left.

      var weight = _options.MaxExitDistance + 1 - distance;
      candidates.Add((node.Id, weight));
      totalWeight += weight;
    }

    if (candidates.Count == 0) return null;

    var roll = _random.NextInt(totalWeight);
    foreach (var candidate in candidates)
    {
      roll -= candidate.Weight;
      if (roll < 0) return candidate.Id;
    }

    // Unreachable: the roll is always below the total weight.
    throw new InvalidOperationException("Weighted exit pick ran past its candidate list.");
  }

  /// <summary>
  /// Measures the relationship distance from a start node over an undirected view of the
  /// graph - passage direction is ignored, so a one-way passage can be traversed backwards
  /// - bounded at <see cref="CnfProcRelSecretsOpt.MaxExitDistance"/> hops.
  /// </summary>
  /// <param name="startId">Node to measure from.</param>
  /// <param name="ignoreConditionals">True to cross every passage; false to skip conditional passages.</param>
  /// <returns>Distance in hops per reachable node, including the start node at distance 0.</returns>
  private Dictionary<Guid, int> FloodDistances(Guid startId, bool ignoreConditionals)
  {
    var adjacency = BuildSearchAdjacency(ignoreConditionals);
    var distances = new Dictionary<Guid, int>(_graph.Nodes.Count) { [startId] = 0 };
    var queue = new Queue<Guid>();
    queue.Enqueue(startId);

    while (queue.Count > 0)
    {
      var current = queue.Dequeue();
      var next = distances[current] + 1;
      if (next > _options.MaxExitDistance) continue; // Never walk past the configured bound.

      if (!adjacency.TryGetValue(current, out var neighbors)) continue;
      foreach (var neighbor in neighbors)
      {
        if (distances.ContainsKey(neighbor)) continue;
        distances[neighbor] = next;
        queue.Enqueue(neighbor);
      }
    }

    return distances;
  }

  /// <summary>
  /// Builds the undirected neighbor lists used for the exit search: every passage
  /// contributes both directions, conditional passages are dropped unless they are
  /// explicitly allowed, and references to unknown nodes are ignored like in
  /// <see cref="BuildDegrees"/>.
  /// </summary>
  /// <param name="ignoreConditionals">True to keep conditional passages.</param>
  /// <returns>Neighbor list per node identifier.</returns>
  private Dictionary<Guid, List<Guid>> BuildSearchAdjacency(bool ignoreConditionals)
  {
    var adjacency = new Dictionary<Guid, List<Guid>>(_graph.Nodes.Count);
    foreach (var node in _graph.Nodes)
    {
      adjacency[node.Id] = new List<Guid>();
    }

    foreach (var edge in _graph.Edges)
    {
      if (!ignoreConditionals && edge.Conditions.Count > 0) continue;
      if (!adjacency.TryGetValue(edge.From, out var from)) continue;
      if (!adjacency.TryGetValue(edge.To, out var to)) continue;

      from.Add(edge.To);
      to.Add(edge.From);
    }

    return adjacency;
  }

  /// <summary>
  /// Counts the relationships touching every node present on entry. Edges referencing a
  /// node that does not exist are ignored - the validator reports those; this pass only
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

  /// <summary>
  /// Derives the exit-edge identifier of a two-passage secret, unique per secret, from the
  /// graph sub-seed and the secret ordinal as sub-channel.
  /// </summary>
  /// <param name="ordinal">Zero-based ordinal of the secret within the pass.</param>
  /// <returns>Identifier unique for the run. Not <see cref="Guid.Empty"/>.</returns>
  private Guid DeriveExitEdgeId(int ordinal)
  {
    var subSeed = SeedDerivation.Derive(_seedGraph, SaltSecretExitEdge, ordinal);
    return IdDerivation.Derive(subSeed, SaltSecretExitEdge);
  }
}
