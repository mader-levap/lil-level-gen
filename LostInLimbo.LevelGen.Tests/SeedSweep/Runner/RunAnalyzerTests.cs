using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.SeedSweep.Data;
using LostInLimbo.LevelGen.SeedSweep.Runner;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.GraphTestCreate;

namespace LostInLimbo.LevelGen.Tests.SeedSweep.Runner;

/// <summary>
/// Tests <see cref="RunAnalyzer"/> on hand-made graphs: one error class or metric per test.
/// </summary>
/// <remarks>
/// Base graph: <c>N1[Start] -- N2[k20] -- needs k20 -- N3[Exit]</c>. Size 3, Complexity 1.
/// </remarks>
public sealed class RunAnalyzerTests
{
  private const int StartPoi = 10;
  private const int ExitPoi = 11;
  private const int KeyPoi = 20;

  #region Clean graph

  public sealed class CleanGraphTests
  {
    [Fact]
    public void Analyze_CleanGraph_ReportsNoErrors()
    {
      // Arrange: Base graph and matching config values.
      var record = CreateRecord();

      // Act: Analyze.
      RunAnalyzer.Analyze(record, CnfLevelGeneration.Default, World(BaseNodes(), BaseEdges()));

      // Assert: No errors, counts match the graph.
      Assert.Empty(record.Issues);
      Assert.Equal(3, record.Nodes);
      Assert.Equal(1, record.Solutions);
      Assert.Equal(1, record.ConditionedEdges);
      Assert.Equal(0, record.ExtraEdges);
    }

    [Fact]
    public void Analyze_CleanGraph_ExitIsLockedAndObstacleIsUseful()
    {
      // Arrange: Base graph.
      var record = CreateRecord();

      // Act: Analyze.
      RunAnalyzer.Analyze(record, CnfLevelGeneration.Default, World(BaseNodes(), BaseEdges()));

      // Assert: Exit needs the key, the key matters, it lies next to its door but not at start.
      Assert.False(record.ExitOpen);
      Assert.False(record.ExitOpenNoSecret);
      Assert.Equal(0, record.UselessObstacles);
      Assert.Equal(1, record.KeyNextToDoor);
      Assert.Equal(0, record.KeyAtStartDoor);
      Assert.Equal(2.0 / 3.0, record.OpenReachShare, 6);
    }
  }

  #endregion

  #region Errors

  public sealed class ErrorTests
  {
    [Fact]
    public void Analyze_TwoStartPois_ReportsError()
    {
      // Arrange: Second start POI in N3.
      var nodes = BaseNodes();
      nodes[2] = Node(3, Poi(ExitPoi, EnPoiType.Exit), Poi(12, EnPoiType.Start));
      var record = CreateRecord();

      // Act: Analyze.
      RunAnalyzer.Analyze(record, CnfLevelGeneration.Default, World(nodes, BaseEdges()));

      // Assert: Start count reported.
      Assert.Contains("start POIs: 2", record.Issues);
    }

    [Fact]
    public void Analyze_ConditionOnMissingPoi_ReportsError()
    {
      // Arrange: Second lock needs a POI that does not exist.
      var edges = BaseEdges();
      edges.Add(Edge(3, 1, 3, conditions: [Condition(99)]));
      var record = CreateRecord();

      // Act: Analyze.
      RunAnalyzer.Analyze(record, CnfLevelGeneration.Default, World(BaseNodes(), edges));

      // Assert: Missing target reported.
      Assert.Contains("condition requires missing POI", record.Issues);
    }

    [Fact]
    public void Analyze_SolutionInSecretNode_ReportsError()
    {
      // Arrange: Key node marked secret.
      var nodes = BaseNodes();
      nodes[1] = nodes[1] with { Secret = true };
      var record = CreateRecord();

      // Act: Analyze.
      RunAnalyzer.Analyze(record, CnfLevelGeneration.Default, World(nodes, BaseEdges()));

      // Assert: Secret solution reported.
      Assert.Contains("solution in secret node", record.Issues);
    }

    [Fact]
    public void Analyze_OrphanSolution_ReportsError()
    {
      // Arrange: Lock removed, key stays.
      var edges = BaseEdges();
      edges[1] = Edge(2, 2, 3);
      var record = CreateRecord();

      // Act: Analyze.
      RunAnalyzer.Analyze(record, CnfLevelGeneration.Default, World(BaseNodes(), edges));

      // Assert: Orphan counted and reported.
      Assert.Equal(1, record.OrphanSolutions);
      Assert.Contains("1 solution(s) required by no passage", record.Issues);
    }

    [Fact]
    public void Analyze_DegreeOverMaxEdges_ReportsError()
    {
      // Arrange: N2 has two passages but allows one.
      var nodes = BaseNodes();
      nodes[1] = Node(2, 1, Poi(KeyPoi, EnPoiType.Solution));
      var record = CreateRecord();

      // Act: Analyze.
      RunAnalyzer.Analyze(record, CnfLevelGeneration.Default, World(nodes, BaseEdges()));

      // Assert: Degree overflow reported.
      Assert.Contains("node degree 2 > MaxEdges 1", record.Issues);
    }

    [Fact]
    public void Analyze_UnreachableNode_ReportsError()
    {
      // Arrange: N4 only has a one-way passage out of it, so nobody gets in.
      var nodes = BaseNodes();
      nodes.Add(Node(4));
      var edges = BaseEdges();
      edges.Add(Edge(3, 4, 1, EnPassageDirection.OneWay));
      var record = CreateRecord(size: 4);

      // Act: Analyze.
      RunAnalyzer.Analyze(record, CnfLevelGeneration.Default, World(nodes, edges));

      // Assert: Unreachable node reported.
      Assert.Contains("1 node(s) unreachable with all solutions (0 secret)", record.Issues);
    }

    [Fact]
    public void Analyze_OrdinaryNodeCountDiffersFromSize_ReportsError()
    {
      // Arrange: Config says 5 nodes, graph has 3.
      var record = CreateRecord(size: 5);

      // Act: Analyze.
      RunAnalyzer.Analyze(record, CnfLevelGeneration.Default, World(BaseNodes(), BaseEdges()));

      // Assert: Size mismatch reported.
      Assert.Contains("ordinary node count 3 != Size 5", record.Issues);
    }
  }

  #endregion

  #region Metrics

  public sealed class MetricTests
  {
    [Fact]
    public void Analyze_KeyInStartNodeNextToDoor_CountsKeyAtStartDoor()
    {
      // Arrange: Key in N1, lock on N1 -- N2.
      List<LilNode> nodes =
      [
        Node(1, Poi(StartPoi, EnPoiType.Start), Poi(KeyPoi, EnPoiType.Solution)),
        Node(2),
        Node(3, Poi(ExitPoi, EnPoiType.Exit)),
      ];
      List<LilEdge> edges = [Edge(1, 1, 2, conditions: [Condition(KeyPoi)]), Edge(2, 2, 3)];
      var record = CreateRecord();

      // Act: Analyze.
      RunAnalyzer.Analyze(record, CnfLevelGeneration.Default, World(nodes, edges));

      // Assert: Both key metrics count it; it is also a solution in the start node.
      Assert.Equal(1, record.KeyNextToDoor);
      Assert.Equal(1, record.KeyAtStartDoor);
      Assert.Equal(1, record.SolutionsInStart);
    }

    [Fact]
    public void Analyze_OpenBypassAroundLock_CountsUselessObstacleAndOpenExit()
    {
      // Arrange: Open passage N1 -- N3 around the lock.
      var edges = BaseEdges();
      edges.Add(Edge(3, 1, 3));
      var record = CreateRecord();

      // Act: Analyze.
      RunAnalyzer.Analyze(record, CnfLevelGeneration.Default, World(BaseNodes(), edges));

      // Assert: The lock guards nothing; exit is open with and without secrets.
      Assert.Equal(1, record.UselessObstacles);
      Assert.True(record.ExitOpen);
      Assert.True(record.ExitOpenNoSecret);
      Assert.Equal(1, record.ExtraEdges);
    }

    [Fact]
    public void Analyze_SecretBypassAroundLock_ExitOpenOnlyThroughSecret()
    {
      // Arrange: Secret passage N1 -- N3 around the lock.
      var edges = BaseEdges();
      edges.Add(Edge(3, 1, 3, secret: true));
      var record = CreateRecord();

      // Act: Analyze.
      RunAnalyzer.Analyze(record, CnfLevelGeneration.Default, World(BaseNodes(), edges));

      // Assert: Exit open via the secret only; secret passages are not extra edges.
      Assert.True(record.ExitOpen);
      Assert.False(record.ExitOpenNoSecret);
      Assert.Equal(0, record.ExtraEdges);
      Assert.Equal(1, record.SecretEdges);
    }

    [Fact]
    public void FillConfig_DefaultConfig_CopiesGraphValues()
    {
      // Arrange: Default config.
      var config = CnfLevelGeneration.Default;
      var record = new RunRecord();

      // Act: Fill.
      RunAnalyzer.FillConfig(record, config);

      // Assert: Values copied.
      Assert.Equal(config.Graph.Size, record.Size);
      Assert.Equal(config.Graph.Complexity, record.Complexity);
      Assert.Equal(config.Graph.GraphBuilder.Builder.ToString(), record.Builder);
    }
  }

  #endregion

  #region Helpers

  /// <summary>Record with config values matching the base graph.</summary>
  private static RunRecord CreateRecord(int size = 3) => new()
  {
    Size = size,
    Complexity = 1,
    GoalAmount = 0,
    OptGoalAmount = 0,
    GroupCount = 1,
  };

  /// <summary>Nodes of the base graph.</summary>
  private static List<LilNode> BaseNodes() =>
  [
    Node(1, Poi(StartPoi, EnPoiType.Start)),
    Node(2, Poi(KeyPoi, EnPoiType.Solution)),
    Node(3, Poi(ExitPoi, EnPoiType.Exit)),
  ];

  /// <summary>Passages of the base graph.</summary>
  private static List<LilEdge> BaseEdges() => [Edge(1, 1, 2), Edge(2, 2, 3, conditions: [Condition(KeyPoi)])];

  /// <summary>World holding the given graph.</summary>
  private static LilWorld World(List<LilNode> nodes, List<LilEdge> edges) =>
    new() { Graph = new LilGraph { Nodes = nodes, Edges = edges } };

  #endregion
}
