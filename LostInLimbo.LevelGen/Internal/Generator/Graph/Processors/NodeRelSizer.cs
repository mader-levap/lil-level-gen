using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;

/// <summary>
/// <para>
/// Graph pass that assigns <see cref="LilNode.RelSize"/> to every node. It runs
/// after nodes, relationships and POIs are known.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Sizes are sampled from a fixed set of step values (see
/// <see cref="NodeRelSizerOptions.BuildStepValues"/>): the options decide how many
/// steps exist, how large they grow, how strongly one step dominates and how hard
/// relationships and POIs pull nodes upward. Bias only changes likelihood - a large node
/// without POIs and with a single relationship stays possible, just less likely.
/// </para>
/// <para>
/// Algorithm: roll one dominant step per graph, then visit every node in Id order and
/// sample its step independently. The base weight mixes a flat share (controlled by
/// <see cref="NodeRelSizerOptions.Skew"/>) with the dominant step; a linear tilt
/// proportional to the node's bias score adds weight to higher steps. The bias score sums
/// the min-max normalized relationship count and POI count of the node - min-max keeps the
/// bias relative: when all nodes look the same, there is nothing to distinguish and the
/// bias does nothing.
/// </para>
/// <para>
/// Deterministic for the same graph contents, options and seed: the generator derives its
/// own sub-seed from a dedicated salt, so earlier passes keep their exact output, and
/// Id-order visits make the assignment independent of list insertion order. Only
/// <see cref="LilNode.RelSize"/> is modified; identifiers, edges and POIs stay untouched.
/// </para>
/// </remarks>
internal static class NodeRelSizer
{
  /// <summary>Salt for the distribution sub-seed; keeps this pass off other passes' streams.</summary>
  private const long SaltRelSize = 0x72656C73697A65; // "relsize"

  /// <summary>
  /// Precision of the uniform draw used for sampling a step: 24 bits mapped into
  /// <c>[0, 1)</c> - far more than the float weights it is scaled by.
  /// </summary>
  private const int SamplePrecision = 1 << 24;

  /// <summary>
  /// Assigns one of the configured step values as <see cref="LilNode.RelSize"/> to every
  /// node of <paramref name="graph"/>.
  /// </summary>
  /// <param name="graph">Graph whose nodes are rewritten in place; may be empty.</param>
  /// <param name="options">Distribution parameters; validated before use.</param>
  /// <param name="seedGraph">Graph sub-seed the distribution stream is derived from.</param>
  /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="options"/> is null.</exception>
  /// <exception cref="ArgumentOutOfRangeException">Options are out of range; see <see cref="NodeRelSizerOptions.Validate"/>.</exception>
  /// <exception cref="ArgumentException">Options contradict each other; see <see cref="NodeRelSizerOptions.Validate"/>.</exception>
  /// <exception cref="InvalidOperationException">An edge references a node missing from the graph, or two nodes share an identifier.</exception>
  public static void Execute(LilGraph graph, NodeRelSizerOptions options, long seedGraph)
  {
    if (graph is null) throw new ArgumentNullException(nameof(graph));
    if (options is null) throw new ArgumentNullException(nameof(options));

    // Validation runs even for empty graphs, so bad options never pass silently.
    var stepValues = options.BuildStepValues();
    if (graph.Nodes.Count == 0) return;

    // One node: with no variation allowed by validation, every node gets the single step.
    if (stepValues.Length == 1)
    {
      for (var i = 0; i < graph.Nodes.Count; i++)
      {
        graph.Nodes[i] = graph.Nodes[i] with { RelSize = stepValues[0] };
      }

      return;
    }

    var random = new SeededRandom(SeedDerivation.Derive(seedGraph, SaltRelSize));
    var dominant = random.NextInt(stepValues.Length);

    var degrees = GetDegrees(graph);
    var (minDegree, maxDegree) = GetRange(degrees);
    var (minPois, maxPois) = GetPoiRange(graph);

    // Visit in Id order so insertion order cannot influence which draws a node receives.
    var order = BuildVisitOrder(graph);
    var weights = new float[stepValues.Length];
    foreach (var index in order)
    {
      var node = graph.Nodes[index];
      var bias = options.BiasRelationships * Normalize(degrees[index], minDegree, maxDegree)
               + options.BiasPois * Normalize(node.Pois.Count, minPois, maxPois);
      var step = SampleStep(random, stepValues.Length, options.Skew, dominant, bias, weights);
      graph.Nodes[index] = node with { RelSize = stepValues[step] };
    }
  }

  //
  // Internals
  //

  /// <summary>
  /// Counts how many relationships touch each node, in node-index order. Both endpoints
  /// count for every edge - a passage is a relationship of both its nodes, one-way or not.
  /// </summary>
  /// <param name="graph">Graph to inspect.</param>
  /// <returns>Relationship count per node index.</returns>
  /// <exception cref="InvalidOperationException">An edge references a missing node, or two nodes share an identifier.</exception>
  private static int[] GetDegrees(LilGraph graph)
  {
    var indexById = new Dictionary<Guid, int>(graph.Nodes.Count);
    for (var i = 0; i < graph.Nodes.Count; i++)
    {
      indexById.Add(graph.Nodes[i].Id, i);
    }

    var degrees = new int[graph.Nodes.Count];
    foreach (var edge in graph.Edges)
    {
      if (!indexById.TryGetValue(edge.From, out var from))
        throw new InvalidOperationException($"Edge {edge.Id} references unknown node {edge.From}.");
      if (!indexById.TryGetValue(edge.To, out var to))
        throw new InvalidOperationException($"Edge {edge.Id} references unknown node {edge.To}.");

      degrees[from]++;
      degrees[to]++;
    }

    return degrees;
  }

  /// <summary>
  /// Produces the node indices in ascending Id order.
  /// </summary>
  /// <param name="graph">Graph whose nodes are visited.</param>
  /// <returns>Node indices sorted by <see cref="LilNode.Id"/>.</returns>
  private static List<int> BuildVisitOrder(LilGraph graph)
  {
    var order = new List<int>(graph.Nodes.Count);
    for (var i = 0; i < graph.Nodes.Count; i++)
    {
      order.Add(i);
    }

    order.Sort((left, right) => graph.Nodes[left].Id.CompareTo(graph.Nodes[right].Id));
    return order;
  }

  /// <summary>
  /// Finds the smallest and largest value of a set of counts.
  /// </summary>
  /// <param name="values">Values to range over; must not be empty.</param>
  /// <returns>The minimum and maximum value.</returns>
  private static (int Min, int Max) GetRange(int[] values)
  {
    var min = values[0];
    var max = values[0];
    for (var i = 1; i < values.Length; i++)
    {
      if (values[i] < min) min = values[i];
      if (values[i] > max) max = values[i];
    }

    return (min, max);
  }

  /// <summary>
  /// Finds the smallest and largest POI count across all nodes.
  /// </summary>
  /// <param name="graph">Graph to inspect.</param>
  /// <returns>The minimum and maximum POI count.</returns>
  private static (int Min, int Max) GetPoiRange(LilGraph graph)
  {
    var min = graph.Nodes[0].Pois.Count;
    var max = min;
    for (var i = 1; i < graph.Nodes.Count; i++)
    {
      var count = graph.Nodes[i].Pois.Count;
      if (count < min) min = count;
      if (count > max) max = count;
    }

    return (min, max);
  }

  /// <summary>
  /// Maps a count into <c>[0, 1]</c> relative to the range of all nodes.
  /// </summary>
  /// <param name="value">Count to normalize.</param>
  /// <param name="min">Smallest count in the graph.</param>
  /// <param name="max">Largest count in the graph.</param>
  /// <returns>Normalized value, or 0 when all nodes share the same count - no distinction, no bias.</returns>
  private static float Normalize(int value, int min, int max)
  {
    if (max <= min) return 0f;
    return (value - min) / (float)(max - min);
  }

  /// <summary>
  /// Samples one step index for a single node.
  /// </summary>
  /// <param name="random">Seeded generator providing the draw.</param>
  /// <param name="stepCount">Amount of step values; at least 2.</param>
  /// <param name="skew">Base concentration on the dominant step; within [0, 1].</param>
  /// <param name="dominant">Index of the step the skew pulls toward.</param>
  /// <param name="bias">Node's upward pull; 0 means no tilt.</param>
  /// <param name="weights">Scratch buffer of at least <paramref name="stepCount"/> entries; fully rewritten.</param>
  /// <returns>Sampled step index within <c>[0, stepCount)</c>. Deterministic for the same generator state.</returns>
  private static int SampleStep(
    SeededRandom random,
    int stepCount,
    float skew,
    int dominant,
    float bias,
    float[] weights)
  {
    // Base: flat share of the probability mass, plus the skew concentrated on the
    // dominant step. Tilt: linear upward pull - higher steps gain weight in proportion
    // to the node's bias, but never exclusively.
    var total = 0f;
    for (var step = 0; step < stepCount; step++)
    {
      var weight = (1f - skew) / stepCount;
      if (step == dominant) weight += skew;
      weight += bias * step / (stepCount - 1);
      weights[step] = weight;
      total += weight;
    }

    // Inverse-CDF sample over the weights with a uniform fixed-point draw.
    var target = random.NextInt(SamplePrecision) * (total / SamplePrecision);
    var cumulative = 0f;
    for (var step = 0; step < stepCount; step++)
    {
      cumulative += weights[step];
      if (target < cumulative) return step;
    }

    // Floating-point fallback: only reachable through rounding drift; heaviest top step.
    return stepCount - 1;
  }
}
