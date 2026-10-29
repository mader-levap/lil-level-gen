using System.Globalization;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop;
using Xunit.Abstractions;

namespace LostInLimbo.LevelGen.Tests.Prop;

/// <summary>
/// Soak test for <see cref="ConfigGenerator"/>: sweeps every difficulty step the game
/// exposes (0 through 9) with a thousand seeded generations each, asserting that every
/// generated configuration validates end to end and stays inside the resolver's
/// difficulty-driven ranges. All runs are deterministic; a failing (difficulty, seed)
/// pair reproduces the exact configuration. After each sweep the collected samples are
/// reported through <see cref="ITestOutputHelper"/> as a min/max/average/median table,
/// followed by one table per graph builder that appeared in the sweep, covering that
/// builder's own configuration variables.
/// </summary>
public sealed class ConfigGeneratorStressTests
{
  /// <summary>Number of seeded generations per difficulty step.</summary>
  private const int GenerationsPerDifficulty = 1000;

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

    // Arrange: Per-builder sample buffers for the builder-specific tables.
    var simpleChokepointChanceSamples = new List<float>(GenerationsPerDifficulty);
    var simpleMaxExtraDegreeSamples = new List<int>(GenerationsPerDifficulty);
    var simpleMaxExtraDegreeStarSamples = new List<int>(GenerationsPerDifficulty);
    var linearMaxEdgesSamples = new List<int>(GenerationsPerDifficulty);
    var linearMaxExtraDegreeSamples = new List<int>(GenerationsPerDifficulty);
    var linearMaxConditionPerRelSamples = new List<int>(GenerationsPerDifficulty);
    var linearMaxConditionDistanceSamples = new List<int>(GenerationsPerDifficulty);
    var linearMaxSkipDistanceSamples = new List<int>(GenerationsPerDifficulty);

    for (int i = 0; i < GenerationsPerDifficulty; i++)
    {
      // Arrange: Unique supporting seed per (difficulty, iteration) so the sweep covers
      // a thousand distinct draw streams and any failure is exactly reproducible.
      long seed = (long)difficulty * GenerationsPerDifficulty + i;
      var generator = new ConfigGenerator(
        new PropMain { TextSeed = TextSeed, Difficulty = difficulty },
        new PropData { Seed = seed });

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
    }

    // Act: Summarize the sweep.
    PrintStatsTable(
      difficulty,
      degenerationSamples,
      sizeSamples,
      connectivitySamples,
      complexitySamples,
      goalAmountSamples,
      optGoalAmountSamples);

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
  private void PrintStatsTable(
    int difficulty,
    IReadOnlyList<float> degenerationSamples,
    IReadOnlyList<int> sizeSamples,
    IReadOnlyList<int> connectivitySamples,
    IReadOnlyList<int> complexitySamples,
    IReadOnlyList<int> goalAmountSamples,
    IReadOnlyList<int> optGoalAmountSamples)
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
