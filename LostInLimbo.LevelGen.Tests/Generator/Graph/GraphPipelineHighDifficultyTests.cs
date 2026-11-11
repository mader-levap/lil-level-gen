using System.Diagnostics;
using System.Globalization;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator;
using LostInLimbo.LevelGen.Internal.Generator.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Prepass;
using LostInLimbo.LevelGen.Internal.Prop;
using Xunit.Abstractions;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph;

/// <summary>
/// Runs the real graph pipeline (config generation, prepass, graph generation) across the
/// difficulty range, up to <see cref="PropMain.MaxDifficulty"/>, with several seeds each.
/// Every run must succeed within a time budget and produce a structurally sound graph.
/// </summary>
/// <remarks>
/// <para>
/// All runs are deterministic: seed = <c>difficulty * SeedsPerDifficulty + i</c>, so any
/// reported (difficulty, seed) pair reproduces the exact configuration and graph. Violations
/// are collected per difficulty and reported together.
/// </para>
/// <para>
/// <see cref="GraphGenerator.Execute"/> takes no cancellation token, so each run executes on a
/// worker task with a timeout. A run that does not finish counts as a violation (a hang
/// becomes a test failure). The abandoned task may keep running in the background.
/// </para>
/// </remarks>
[Trait("Category", "Stress")]
public sealed class GraphPipelineHighDifficultyTests
{
  /// <summary>Number of seeded runs per difficulty.</summary>
  private const int SeedsPerDifficulty = 16;

  /// <summary>Number of seeds per difficulty that are additionally run twice to check determinism.</summary>
  private const int DeterminismSeedsPerDifficulty = 4;

  /// <summary>Time budget of one pipeline run.</summary>
  private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(5);

  /// <summary>Fixed text seed so only the supporting draw stream varies across the sweep.</summary>
  private const string TextSeed = "mordor";

  /// <summary>Maximum number of violations listed in a failure message.</summary>
  private const int MaxReportedViolations = 8;

  /// <summary>Per-test output sink for timing statistics.</summary>
  private readonly ITestOutputHelper _output;

  /// <summary>Creates the test class with its output sink supplied by the test framework.</summary>
  /// <param name="output">Per-test output sink.</param>
  public GraphPipelineHighDifficultyTests(ITestOutputHelper output)
  {
    _output = output;
  }

  [Theory]
  [InlineData(0)]
  [InlineData(5)]
  [InlineData(10)]
  [InlineData(25)]
  [InlineData(50)]
  [InlineData(75)]
  [InlineData(100)]
  public void Execute_WithSeedSweepAtDifficulty_ProducesValidGraphsInTime(int difficulty)
  {
    // Arrange: Violation buffer and timing samples for this difficulty.
    var violations = new List<string>();
    var durations = new List<double>(SeedsPerDifficulty);

    for (int i = 0; i < SeedsPerDifficulty; i++)
    {
      long seed = (long)difficulty * SeedsPerDifficulty + i;

      // Act: Run the pipeline once under the time budget.
      var stopwatch = Stopwatch.StartNew();
      var run = RunWithTimeout(difficulty, seed, out string? runError);
      durations.Add(stopwatch.Elapsed.TotalMilliseconds);

      if (run is null)
      {
        violations.Add($"seed {seed}: {runError}");
        continue;
      }

      // Assert: Collect every violated rule of this run.
      foreach (var violation in CheckRun(run.Value.Config, run.Value.Result, run.Value.Graph))
        violations.Add($"seed {seed}: {violation}");

      // Assert: The same inputs reproduce the same graph.
      if (i < DeterminismSeedsPerDifficulty)
      {
        var again = RunWithTimeout(difficulty, seed, out string? againError);
        if (again is null)
          violations.Add($"seed {seed}: second run: {againError}");
        else if (!HaveSameStructure(run.Value.Graph, again.Value.Graph))
          violations.Add($"seed {seed}: second run produced a different graph");
      }
    }

    // Act: Report timing so slow growth is visible even when nothing fails.
    var culture = CultureInfo.InvariantCulture;
    _output.WriteLine(
      $"Difficulty {difficulty}: {SeedsPerDifficulty} runs, " +
      $"avg {durations.Average().ToString("F1", culture)} ms, max {durations.Max().ToString("F1", culture)} ms");

    // Assert: No run failed.
    if (violations.Count > 0)
    {
      var sample = string.Join(Environment.NewLine, violations.Take(MaxReportedViolations));
      Assert.Fail(
        $"Difficulty {difficulty}: {violations.Count} violation(s) across {SeedsPerDifficulty} seeds. " +
        $"First {Math.Min(MaxReportedViolations, violations.Count)}:{Environment.NewLine}{sample}");
    }
  }

  // Pipeline.

  /// <summary>Output of one pipeline run.</summary>
  /// <param name="Config">Generated configuration.</param>
  /// <param name="Result">Graph generation outcome.</param>
  /// <param name="Graph">Generated graph.</param>
  private readonly record struct PipelineRun(CnfLevelGeneration Config, GraphRunResult Result, LilGraph Graph);

  /// <summary>
  /// Runs config generation, prepass and graph generation on a worker task under <see cref="RunTimeout"/>.
  /// </summary>
  /// <param name="difficulty">Difficulty to generate for.</param>
  /// <param name="seed">Supporting config seed.</param>
  /// <param name="error">Why the run produced no result; <see langword="null"/> on success.</param>
  /// <returns>Run output, or <see langword="null"/> when the run threw or timed out.</returns>
  private static PipelineRun? RunWithTimeout(int difficulty, long seed, out string? error)
  {
    var task = Task.Run(() => RunPipeline(difficulty, seed));
    try
    {
      if (!task.Wait(RunTimeout))
      {
        error = $"timed out after {RunTimeout.TotalSeconds} s";
        return null;
      }
    }
    catch (AggregateException exception)
    {
      var inner = exception.InnerException ?? exception;
      error = $"threw {inner.GetType().Name}: {inner.Message}";
      return null;
    }

    error = null;
    return task.Result;
  }

  /// <summary>Runs config generation, prepass and graph generation synchronously.</summary>
  /// <param name="difficulty">Difficulty to generate for.</param>
  /// <param name="seed">Supporting config seed.</param>
  /// <returns>Run output.</returns>
  private static PipelineRun RunPipeline(int difficulty, long seed)
  {
    var propMain = new PropMain { TextSeed = TextSeed, Difficulty = difficulty, ManyThemes = true };
    var config = new ConfigGenerator(propMain, seed).Execute();
    config.Validate();
    var prepData = new PrepassGenerator(config).Create();
    var world = new LilWorld();
    var result = new GraphGenerator(config, prepData, world).Execute();
    return new PipelineRun(config, result, world.Graph);
  }

  // Checks.

  /// <summary>
  /// Checks one pipeline run: success outcome, node count, start/exit POIs, edge integrity and
  /// reachability of every node from the start node.
  /// </summary>
  /// <param name="config">Configuration the graph was generated from.</param>
  /// <param name="result">Graph generation outcome.</param>
  /// <param name="graph">Generated graph.</param>
  /// <returns>Descriptions of every violated rule; empty when the run is sound.</returns>
  private static List<string> CheckRun(CnfLevelGeneration config, GraphRunResult result, LilGraph graph)
  {
    var violations = new List<string>();
    string shape = $"(size {config.Graph.Size}, connectivity {config.Graph.Connectivity}, " +
      $"complexity {config.Graph.Complexity}, builder {config.Graph.GraphBuilder.Builder})";

    if (result.Outcome != EnGenerationOutcome.Success)
    {
      violations.Add($"outcome {result.Outcome}: {result.FailureReason} {shape}");
      return violations; // Graph may be partial; further structural checks would only add noise.
    }

    // Node count: configured size plus secret nodes appended by the finalization pass.
    int regularNodes = graph.Nodes.Count(node => !node.Secret);
    if (regularNodes != config.Graph.Size)
      violations.Add($"{regularNodes} non-secret nodes, expected {config.Graph.Size} {shape}");

    // Exactly one start and one exit.
    int starts = GraphTestSupport.GetPois(graph, EnPoiType.Start).Count;
    int exits = GraphTestSupport.GetPois(graph, EnPoiType.Exit).Count;
    if (starts != 1 || exits != 1)
      violations.Add($"{starts} start / {exits} exit POIs, expected 1 / 1 {shape}");

    // Edge integrity: both ends exist.
    var nodeIds = new HashSet<Guid>(graph.Nodes.Select(node => node.Id));
    int danglingEdges = graph.Edges.Count(edge => !nodeIds.Contains(edge.From) || !nodeIds.Contains(edge.To));
    if (danglingEdges > 0)
      violations.Add($"{danglingEdges} edge(s) reference missing nodes {shape}");

    // Reachability: every node is connected to the start node.
    var startNode = graph.Nodes.FirstOrDefault(node => node.Pois.Any(poi => poi.Type == EnPoiType.Start));
    if (startNode is not null)
    {
      int unreachable = graph.Nodes.Count - GraphTestSupport.GetReachableNodeIds(graph, startNode.Id).Count;
      if (unreachable > 0)
        violations.Add($"{unreachable} node(s) not connected to start {shape}");
    }

    return violations;
  }

  /// <summary>
  /// Compares two graphs by node ids and edge endpoints in list order.
  /// </summary>
  /// <param name="first">First graph.</param>
  /// <param name="second">Second graph.</param>
  /// <returns><see langword="true"/> when both graphs have the same nodes and edges in the same order.</returns>
  private static bool HaveSameStructure(LilGraph first, LilGraph second)
  {
    return first.Nodes.Select(node => node.Id).SequenceEqual(second.Nodes.Select(node => node.Id))
      && first.Edges.Select(edge => (edge.Id, edge.From, edge.To, edge.Secret))
        .SequenceEqual(second.Edges.Select(edge => (edge.Id, edge.From, edge.To, edge.Secret)));
  }
}
