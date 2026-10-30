using System.Globalization;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Data;
using Xunit.Abstractions;

namespace LostInLimbo.LevelGen.Tests.Prop;

/// <summary>
/// Soak test for <see cref="ConfigGenerator"/>: sweeps every difficulty step the game
/// exposes (0 through 9) with a thousand seeded generations each, asserting that every
/// generated configuration validates end to end and stays inside the resolver's
/// difficulty-driven ranges. All runs are deterministic; a failing (difficulty, seed)
/// pair reproduces the exact configuration. After each sweep the collected samples are
/// reported through <see cref="ITestOutputHelper"/> as a min/max/average/median table
/// plus a graph-diversity line (unique integer signatures and Shannon entropy),
/// followed by one table per graph builder that appeared in the sweep, covering that
/// builder's own configuration variables.
/// </summary>
public sealed class ConfigGeneratorStressTests
{
  /// <summary>Number of seeded generations per difficulty step.</summary>
  private const int GenerationsPerDifficulty = 1024;

  /// <summary>Fixed text seed so only the supporting draw stream varies across the sweep.</summary>
  private const string TextSeed = "mordor";

  /// <summary>Width of the Variable column; sized for the longest variable name (MaxConditionDistance).</summary>
  private const int VariableColumnWidth = 22;

  /// <summary>Per-test output sink, shown in IDE test explorers under the test's Output.</summary>
  private readonly ITestOutputHelper _output;

  /// <summary>Creates the test class with its output sink supplied by the test framework.</summary>
  /// <param name="output">Per-test output sink.</param>
  public ConfigGeneratorStressTests(ITestOutputHelper output)
  {
    _output = output;
  }

  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  [InlineData(2)]
  [InlineData(3)]
  [InlineData(4)]
  [InlineData(5)]
  [InlineData(6)]
  [InlineData(7)]
  [InlineData(8)]
  [InlineData(9)]
  public void Execute_WithSeedSweepAcrossDifficulty_ProducesOnlyValidConfigurations(int difficulty)
  {
    // Arrange: Sample buffers for the end-of-sweep statistics tables.
    var degenerationSamples = new List<float>(GenerationsPerDifficulty);
    var sizeSamples = new List<int>(GenerationsPerDifficulty);
    var connectivitySamples = new List<int>(GenerationsPerDifficulty);
    var complexitySamples = new List<int>(GenerationsPerDifficulty);
    var goalAmountSamples = new List<int>(GenerationsPerDifficulty);
    var optGoalAmountSamples = new List<int>(GenerationsPerDifficulty);
    var secretsAmountSamples = new List<int>(GenerationsPerDifficulty);
    var groupCountSamples = new List<int>(GenerationsPerDifficulty);

    // Arrange: Per-builder sample buffers for the builder-specific tables.
    var simpleChokepointChanceSamples = new List<float>(GenerationsPerDifficulty);
    var simpleMaxExtraDegreeSamples = new List<int>(GenerationsPerDifficulty);
    var simpleMaxExtraDegreeStarSamples = new List<int>(GenerationsPerDifficulty);
    var linearMaxEdgesSamples = new List<int>(GenerationsPerDifficulty);
    var linearMaxExtraDegreeSamples = new List<int>(GenerationsPerDifficulty);
    var linearMaxConditionPerRelSamples = new List<int>(GenerationsPerDifficulty);
    var linearMaxConditionDistanceSamples = new List<int>(GenerationsPerDifficulty);
    var linearMaxSkipDistanceSamples = new List<int>(GenerationsPerDifficulty);

    // Arrange: Occurrence counts per distinct graph signature for the diversity line.
    // The signature covers only the integer/enum variables shown in the tables, so
    // float chance randomization cannot inflate the metric with fake diversity.
    var graphCounts = new Dictionary<GraphSignature, int>(GenerationsPerDifficulty);

    for (int i = 0; i < GenerationsPerDifficulty; i++)
    {
      // Arrange: Unique supporting seed per (difficulty, iteration) so the sweep covers
      // a thousand distinct draw streams and any failure is exactly reproducible.
      long seed = (long)difficulty * GenerationsPerDifficulty + i;
      var generator = new ConfigGenerator(
        new PropMain { TextSeed = TextSeed, Difficulty = difficulty, ManyThemes = true },
        seed);

      // Act: Generate the full configuration.
      var config = generator.Execute();

      // Assert: The whole configuration validates end to end; failures are rethrown
      // with the (difficulty, seed) pair so the exact run can be reproduced.
      try
      {
        config.Validate();
      }
      catch (Exception exception)
      {
        throw new InvalidOperationException(
          $"Invalid configuration for difficulty {difficulty}, seed {seed}.", exception);
      }

      // Assert: Graph values stay inside the resolver's difficulty-driven ranges, which
      // Validate alone does not enforce.
      var graph = config.Graph;
      Assert.InRange(graph.Size, 2, difficulty + 2);
      Assert.InRange(graph.Connectivity, 0, difficulty + graph.Size - 1);
      Assert.InRange(graph.Complexity, 0, Math.Min(graph.Size, difficulty / 2 + 1) - 1);
      Assert.InRange(graph.ProcOpts.NodeObjectives.GoalAmount, 1, Math.Max(1, difficulty / 2));
      Assert.InRange(graph.ProcOpts.NodeObjectives.OptGoalAmount, 0, graph.Size - 1);

      // Assert: OneNode stays excluded from random builder picking across all draws.
      Assert.NotEqual(EnGraphBuilder.OneNode, graph.GraphBuilder.Builder);

      // Act: Record the major config variables for the statistics table, and the picked
      // builder's own variables for the builder table. Collection happens after the
      // asserts, so a failing run throws before any partial table.
      degenerationSamples.Add(config.General.Degeneration);
      sizeSamples.Add(graph.Size);
      connectivitySamples.Add(graph.Connectivity);
      complexitySamples.Add(graph.Complexity);
      goalAmountSamples.Add(graph.ProcOpts.NodeObjectives.GoalAmount);
      optGoalAmountSamples.Add(graph.ProcOpts.NodeObjectives.OptGoalAmount);
      secretsAmountSamples.Add(graph.ProcOpts.RelSecrets.Amount);
      groupCountSamples.Add(graph.ProcOpts.NodeStyle.GroupCount);

      switch (graph.GraphBuilder.Builder)
      {
        case EnGraphBuilder.Simple:
          var simple = graph.GraphBuilder.CnfSimple!;
          simpleChokepointChanceSamples.Add(simple.ChokepointChance);
          simpleMaxExtraDegreeSamples.Add(simple.MaxExtraDegree);
          simpleMaxExtraDegreeStarSamples.Add(simple.MaxExtraDegreeStar);
          break;
        case EnGraphBuilder.Linear:
          var linear = graph.GraphBuilder.CnfLinear!;
          linearMaxEdgesSamples.Add(linear.MaxEdges);
          linearMaxExtraDegreeSamples.Add(linear.MaxExtraDegree);
          linearMaxConditionPerRelSamples.Add(linear.MaxConditionPerRel);
          linearMaxConditionDistanceSamples.Add(linear.MaxConditionDistance);
          linearMaxSkipDistanceSamples.Add(linear.MaxSkipDistance);
          break;
        default:
          throw new InvalidOperationException($"Unexpected builder {graph.GraphBuilder.Builder}.");
      }

      // Act: Tally this graph's structural signature for the diversity line.
      var signature = CreateGraphSignature(graph);
      graphCounts.TryGetValue(signature, out int seen);
      graphCounts[signature] = seen + 1;
    }

    // Act: Summarize the sweep.
    PrintStatsTable(
      difficulty,
      degenerationSamples,
      sizeSamples,
      connectivitySamples,
      complexitySamples,
      goalAmountSamples,
      optGoalAmountSamples,
      secretsAmountSamples,
      groupCountSamples);

    var culture = CultureInfo.InvariantCulture;

    // Act: Summarize each builder that appeared at least once in the sweep.
    if (simpleChokepointChanceSamples.Count > 0)
    {
      PrintBuilderTable(
        $"EnGraphBuilder.{nameof(EnGraphBuilder.Simple)}",
        simpleChokepointChanceSamples.Count,
        FormatStatsRow("ChokepointChance", simpleChokepointChanceSamples, "F4", culture),
        FormatStatsRow("MaxExtraDegree", simpleMaxExtraDegreeSamples, culture),
        FormatStatsRow("MaxExtraDegreeStar", simpleMaxExtraDegreeStarSamples, culture));
    }

    if (linearMaxEdgesSamples.Count > 0)
    {
      PrintBuilderTable(
        $"EnGraphBuilder.{nameof(EnGraphBuilder.Linear)}",
        linearMaxEdgesSamples.Count,
        FormatStatsRow("MaxEdges", linearMaxEdgesSamples, culture),
        FormatStatsRow("MaxExtraDegree", linearMaxExtraDegreeSamples, culture),
        FormatStatsRow("MaxConditionPerRel", linearMaxConditionPerRelSamples, culture),
        FormatStatsRow("MaxConditionDistance", linearMaxConditionDistanceSamples, culture),
        FormatStatsRow("MaxSkipDistance", linearMaxSkipDistanceSamples, culture));
    }

    PrintDiversityLine(GenerationsPerDifficulty, graphCounts);
  }

  // Diversity line.

  /// <summary>
  /// Structural signature of a generated graph: the integer and enum variables shown in
  /// the statistics tables (main table minus Degeneration, plus the picked builder's own
  /// int fields). Deliberately excludes all float chance values, so randomizing those
  /// within a range cannot inflate unique counts or entropy with fake diversity. The
  /// builder's Max* fields are additionally banded (0=Low, 1=Mid, 2=High) because a cap
  /// wobbling by one is not a meaningfully different graph.
  /// </summary>
  /// <param name="Builder">Picked graph builder.</param>
  /// <param name="Size">Graph size.</param>
  /// <param name="Connectivity">Graph connectivity.</param>
  /// <param name="Complexity">Graph complexity.</param>
  /// <param name="GoalAmount">Mandatory objective POI count.</param>
  /// <param name="OptGoalAmount">Optional objective POI count.</param>
  /// <param name="SecretsAmount">Secret node count.</param>
  /// <param name="BuilderVar0">First picked builder's banded Max* variable; -1 when unused.</param>
  /// <param name="BuilderVar1">Second picked builder's banded Max* variable; -1 when unused.</param>
  /// <param name="BuilderVar2">Third picked builder's banded Max* variable; -1 when unused.</param>
  /// <param name="BuilderVar3">Fourth picked builder's banded Max* variable; -1 when unused.</param>
  /// <param name="BuilderVar4">Fifth picked builder's banded Max* variable; -1 when unused.</param>
  private readonly record struct GraphSignature(
    EnGraphBuilder Builder,
    int Size,
    int Connectivity,
    int Complexity,
    int GoalAmount,
    int OptGoalAmount,
    int SecretsAmount,
    int BuilderVar0,
    int BuilderVar1,
    int BuilderVar2,
    int BuilderVar3,
    int BuilderVar4);

  /// <summary>
  /// Builds the diversity signature of a graph from its integer and enum variables.
  /// The picked builder's Max* caps are banded (0=Low, 1=Mid, 2=High); unused slots are
  /// -1 so they can never collide with a real Low band. Band edges: each default sits
  /// mid-band, and a plus/minus one wobble around a default stays inside its band.
  /// </summary>
  /// <param name="graph">Graph configuration; its builder sub-config must be populated (validated beforehand).</param>
  /// <returns>Structural signature carrying only the table's non-float variables.</returns>
  private static GraphSignature CreateGraphSignature(CnfGraph graph)
  {
    var objectives = graph.ProcOpts.NodeObjectives;
    var (
      builder,
      size,
      connectivity,
      complexity,
      goalAmount,
      optGoalAmount,
      secretsAmount) = (
      graph.GraphBuilder.Builder,
      graph.Size,
      graph.Connectivity,
      graph.Complexity,
      objectives.GoalAmount,
      objectives.OptGoalAmount,
      graph.ProcOpts.RelSecrets.Amount);

    return graph.GraphBuilder.Builder switch
    {
      // Simple: MaxExtraDegree 0-2 / 3-5 / 6+, MaxExtraDegreeStar 2-4 / 5-8 / 9+.
      EnGraphBuilder.Simple => new GraphSignature(
        builder, size, connectivity, complexity, goalAmount, optGoalAmount, secretsAmount,
        Band(graph.GraphBuilder.CnfSimple!.MaxExtraDegree, 2, 5),
        Band(graph.GraphBuilder.CnfSimple!.MaxExtraDegreeStar, 4, 8),
        -1, -1, -1),
      // Linear: MaxEdges 1-3 / 4-6 / 7+, MaxExtraDegree 0-1 / 2-3 / 4+,
      // MaxConditionPerRel 1-2 / 3-4 / 5+, MaxConditionDistance 0-2 / 3-4 / 5+,
      // MaxSkipDistance 0-1 / 2-3 / 4+.
      EnGraphBuilder.Linear => new GraphSignature(
        builder, size, connectivity, complexity, goalAmount, optGoalAmount, secretsAmount,
        Band(graph.GraphBuilder.CnfLinear!.MaxEdges, 3, 6),
        Band(graph.GraphBuilder.CnfLinear!.MaxExtraDegree, 1, 3),
        Band(graph.GraphBuilder.CnfLinear!.MaxConditionPerRel, 2, 4),
        Band(graph.GraphBuilder.CnfLinear!.MaxConditionDistance, 2, 4),
        Band(graph.GraphBuilder.CnfLinear!.MaxSkipDistance, 1, 3)),
      _ => throw new InvalidOperationException($"Unexpected builder {graph.GraphBuilder.Builder}."),
    };
  }

  /// <summary>Maps a raw Max* cap value onto its Low/Mid/High band code.</summary>
  /// <param name="value">Raw cap value.</param>
  /// <param name="lowMaxInclusive">Upper bound of the Low band (inclusive).</param>
  /// <param name="midMaxInclusive">Upper bound of the Mid band (inclusive); High is everything above.</param>
  /// <returns>0 for Low, 1 for Mid, 2 for High.</returns>
  private static int Band(int value, int lowMaxInclusive, int midMaxInclusive)
  {
    if (value <= lowMaxInclusive) return 0;
    return value <= midMaxInclusive ? 1 : 2;
  }

  /// <summary>
  /// Reports how diverse the sweep's graph signatures were: number of structurally
  /// distinct graphs, their share of the sweep, and Shannon entropy (bits plus a 0-1
  /// evenness normalization). Uniqueness is defined by <see cref="GraphSignature"/>, so
  /// float chance values never affect the metric. Low difficulties collapse toward a
  /// handful of signatures, which is what makes graph-space collisions - and flaky
  /// divergence assertions - possible there.
  /// </summary>
  /// <remarks>
  /// <para>
  /// <c>entropy x.xxx bits</c>: How much the sweep actually explored (capped by sample size).
  /// Low entropy compared to sample size means identical values repeat often.
  /// </para>
  /// <para>
  /// <c>(x.xxx normalized)</c>: Measures evenness.
  ///  1.0 = the unique signatures appeared equally often. A low value would mean a few signatures dominate despite many being seen.
  /// </para>
  /// </remarks>
  /// <param name="total">Number of generations in the sweep.</param>
  /// <param name="graphCounts">Occurrence count per distinct graph signature.</param>
  private void PrintDiversityLine(int total, IReadOnlyDictionary<GraphSignature, int> graphCounts)
  {
    var culture = CultureInfo.InvariantCulture;
    int unique = graphCounts.Count;
    double share = unique * 100.0 / total;
    double entropyBits = ShannonEntropyBits(graphCounts.Values);
    double normalized = unique > 1 ? entropyBits / Math.Log2(unique) : 0.0;

    _output.WriteLine(
      $"Diversity: {unique} unique graphs / {total} ({share.ToString("F1", culture)}%), " +
      $"entropy {entropyBits.ToString("F3", culture)} bits ({normalized.ToString("F3", culture)} normalized)");
    _output.WriteLine(string.Empty);
  }

  /// <summary>Computes the Shannon entropy of an occurrence-count distribution, in bits.</summary>
  /// <param name="counts">Occurrence counts; sum is the sample total. Order does not matter.</param>
  /// <returns>Entropy in bits; 0 when all samples share one outcome.</returns>
  private static double ShannonEntropyBits(IEnumerable<int> counts)
  {
    int total = counts.Sum();
    double entropy = 0.0;
    foreach (int count in counts)
    {
      if (count == 0) continue;

      double probability = (double)count / total;
      entropy -= probability * Math.Log2(probability);
    }
    return entropy;
  }

  // Statistics table.

  /// <summary>
  /// Reports the collected samples as a fixed-width min/max/average/median table, one
  /// table per difficulty step.
  /// </summary>
  /// <param name="difficulty">Difficulty step the sweep ran at.</param>
  /// <param name="degenerationSamples">Collected <c>CnfGeneral.Degeneration</c> values.</param>
  /// <param name="sizeSamples">Collected <c>CnfGraph.Size</c> values.</param>
  /// <param name="connectivitySamples">Collected <c>CnfGraph.Connectivity</c> values.</param>
  /// <param name="complexitySamples">Collected <c>CnfGraph.Complexity</c> values.</param>
  /// <param name="goalAmountSamples">Collected <c>CnfProcNodeObjectivesOpt.GoalAmount</c> values.</param>
  /// <param name="optGoalAmountSamples">Collected <c>CnfProcNodeObjectivesOpt.OptGoalAmount</c> values.</param>
  /// <param name="secretsAmountSamples">Collected <c>CnfProcRelSecretsOpt.Amount</c> values.</param>
  /// <param name="groupCountSamples">Collected <c>CnfProcNodeStyleOpt.groupCount</c> values.</param>
  private void PrintStatsTable(
    int difficulty,
    IReadOnlyList<float> degenerationSamples,
    IReadOnlyList<int> sizeSamples,
    IReadOnlyList<int> connectivitySamples,
    IReadOnlyList<int> complexitySamples,
    IReadOnlyList<int> goalAmountSamples,
    IReadOnlyList<int> optGoalAmountSamples,
    IReadOnlyList<int> secretsAmountSamples,
    IReadOnlyList<int> groupCountSamples)
  {
    var culture = CultureInfo.InvariantCulture;

    _output.WriteLine($"=== Difficulty {difficulty} - {degenerationSamples.Count} generations ===");
    _output.WriteLine($"{"Variable",-VariableColumnWidth}{"Min",12}{"Max",12}{"Average",12}{"Median",12}");
    _output.WriteLine(FormatStatsRow("Degeneration", degenerationSamples, "F4", culture));
    _output.WriteLine(FormatStatsRow("Size", sizeSamples, culture));
    _output.WriteLine(FormatStatsRow("Connectivity", connectivitySamples, culture));
    _output.WriteLine(FormatStatsRow("Complexity", complexitySamples, culture));
    _output.WriteLine(FormatStatsRow("GoalAmount", goalAmountSamples, culture));
    _output.WriteLine(FormatStatsRow("OptGoalAmount", optGoalAmountSamples, culture));
    _output.WriteLine(FormatStatsRow("SecretsAmount", secretsAmountSamples, culture));
    _output.WriteLine(FormatStatsRow("GroupCount", groupCountSamples, culture));
    _output.WriteLine(string.Empty);
  }

  /// <summary>
  /// Reports one graph builder's configuration variables as a fixed-width
  /// min/max/average/median table; only printed for builders that appeared in the sweep.
  /// </summary>
  /// <param name="builderName">Display name of the graph builder.</param>
  /// <param name="count">Number of generations that picked this builder.</param>
  /// <param name="rows">Pre-formatted statistics rows, one per builder variable.</param>
  private void PrintBuilderTable(string builderName, int count, params string[] rows)
  {
    _output.WriteLine($"=== {builderName} - {count} generations ===");
    _output.WriteLine($"{"Variable",-VariableColumnWidth}{"Min",12}{"Max",12}{"Average",12}{"Median",12}");
    foreach (var row in rows)
    {
      _output.WriteLine(row);
    }
    _output.WriteLine(string.Empty);
  }

  /// <summary>Formats one statistics row for a float-sampled variable (all stats as fixed-point).</summary>
  /// <param name="variable">Display name of the config variable.</param>
  /// <param name="samples">Collected samples.</param>
  /// <param name="format">Numeric format applied to every stat.</param>
  /// <param name="culture">Culture used for number formatting.</param>
  /// <returns>Fixed-width table row.</returns>
  private static string FormatStatsRow(string variable, IReadOnlyList<float> samples, string format, CultureInfo culture)
  {
    var min = samples.Min().ToString(format, culture).PadLeft(12);
    var max = samples.Max().ToString(format, culture).PadLeft(12);
    var average = samples.Average().ToString(format, culture).PadLeft(12);
    var median = Median(samples).ToString(format, culture).PadLeft(12);
    return $"{variable,-VariableColumnWidth}{min}{max}{average}{median}";
  }

  /// <summary>Formats one statistics row for an int-sampled variable (min/max as plain integers).</summary>
  /// <param name="variable">Display name of the config variable.</param>
  /// <param name="samples">Collected samples.</param>
  /// <param name="culture">Culture used for number formatting.</param>
  /// <returns>Fixed-width table row.</returns>
  private static string FormatStatsRow(string variable, IReadOnlyList<int> samples, CultureInfo culture)
  {
    var min = samples.Min().ToString(culture).PadLeft(12);
    var max = samples.Max().ToString(culture).PadLeft(12);
    var average = samples.Average().ToString("F4", culture).PadLeft(12);
    var median = Median(samples).ToString("F4", culture).PadLeft(12);
    return $"{variable,-VariableColumnWidth}{min}{max}{average}{median}";
  }

  /// <summary>Computes the median of a sample set, averaging the two middle values for even counts.</summary>
  /// <param name="samples">Collected samples; not modified.</param>
  /// <returns>Median value.</returns>
  private static double Median(IReadOnlyList<int> samples)
  {
    var sorted = samples.OrderBy(value => value).ToArray();
    int middle = sorted.Length / 2;
    return sorted.Length % 2 == 0
      ? (sorted[middle - 1] + sorted[middle]) / 2.0
      : sorted[middle];
  }

  /// <summary>Computes the median of a sample set, averaging the two middle values for even counts.</summary>
  /// <param name="samples">Collected samples; not modified.</param>
  /// <returns>Median value.</returns>
  private static double Median(IReadOnlyList<float> samples)
  {
    var sorted = samples.OrderBy(value => value).ToArray();
    int middle = sorted.Length / 2;
    return sorted.Length % 2 == 0
      ? (sorted[middle - 1] + sorted[middle]) / 2.0f
      : sorted[middle];
  }
}
