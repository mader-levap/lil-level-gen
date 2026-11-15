using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Internal.Generator.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Prepass;
using LostInLimbo.LevelGen.Internal.Prop.Data;
using LostInLimbo.LevelGen.Internal.Prop.Resolvers.Graph;

namespace LostInLimbo.LevelGen.Tests.Prop.Resolvers.Graph;

/// <summary>
/// Tests relative-size option resolution: determinism, validity, the square-root step bound and the diversity of the profile roll.
/// </summary>
public sealed class ProcRelSizeResolverTests
{
  /// <summary>Largest number of steps any graph may get.</summary>
  private const int MaxStepsCap = 8;

  /// <summary>Smallest step bound; small graphs still allow three steps.</summary>
  private const int MinStepsBound = 3;

  /// <summary>
  /// Creates a resolver for the given internal seed and graph size. The resolver never sees
  /// the property dataset, so difficulty cannot influence it by construction.
  /// </summary>
  private static ProcRelSizeResolver CreateResolver(long propSeed, int size)
  {
    return new ProcRelSizeResolver(PropData.Default with { Seed = propSeed, Size = size });
  }

  /// <summary>Expected inclusive step bound: <c>min(8, max(3, ceil(sqrt(size))))</c>.</summary>
  private static int ExpectedMaxSteps(int size)
  {
    return Math.Min(MaxStepsCap, Math.Max(MinStepsBound, (int)Math.Ceiling(Math.Sqrt(size))));
  }

  /// <summary>Tests reproducibility of the draws.</summary>
  public sealed class DeterminismTests
  {
    [Fact]
    public void Execute_IsDeterministic()
    {
      // Act: Resolve twice from the same inputs.
      var first = CreateResolver(42, 30).Execute();
      var second = CreateResolver(42, 30).Execute();

      // Assert: Identical inputs produce identical options.
      Assert.Equal(first, second);
    }

    [Fact]
    public void Execute_ReturnsPinnedValues()
    {
      // Act: Resolve from a known seed and size.
      var config = CreateResolver(42, 30).Execute();

      // Assert: Values match the pinned draws, so the salt and draw order cannot silently change.
      Assert.Equal(PinnedConfig, config);
    }

    /// <summary>Pinned options for seed 42 and size 30.</summary>
    private static readonly CnfProcNodeRelSizeOpt PinnedConfig = CnfProcNodeRelSizeOpt.Default with
    {
      Steps = 3,
      Skew = 0.018331528f,
      Curve = EnRelSizeCurve.Geometric,
      MinSize = 1,
      Growth = 2.8584065f,
      BiasRelationships = 1.421742f,
      BiasPois = 1.3692398f,
    };
  }

  /// <summary>Tests that every resolved config is usable and bounded.</summary>
  public sealed class BoundsTests
  {
    [Fact]
    public void Execute_OverSeedsAndSizes_ProducesValidConfigsWithinStepBound()
    {
      for (var size = 2; size <= CnfGraph.MaxSize; size++)
      {
        for (long seed = 0; seed < 40; seed++)
        {
          // Act: Resolve options for this size and seed.
          var config = CreateResolver(seed, size).Execute();

          // Assert: The options validate and prepare step values, and the step count stays
          // inside the square-root bound.
          var values = config.Prepare();
          Assert.Equal(config.Steps, values.Length);
          Assert.InRange(config.Steps, 1, ExpectedMaxSteps(size));
        }
      }
    }

    [Fact]
    public void Execute_ForLargestGraph_ReachesStepCap()
    {
      // Act: Resolve many options for the largest graph.
      var steps = Enumerable.Range(0, 500).Select(seed => CreateResolver(seed, CnfGraph.MaxSize).Execute().Steps).ToList();

      // Assert: The cap is reached but never exceeded.
      Assert.Contains(MaxStepsCap, steps);
      Assert.All(steps, count => Assert.InRange(count, 1, MaxStepsCap));
    }

    [Fact]
    public void Execute_ForSmallGraph_StaysAtThreeSteps()
    {
      // Act: Resolve many options for the smallest graph.
      var steps = Enumerable.Range(0, 500).Select(seed => CreateResolver(seed, 2).Execute().Steps).ToList();

      // Assert: Small graphs never go beyond three steps, but still reach them.
      Assert.Contains(MinStepsBound, steps);
      Assert.All(steps, count => Assert.InRange(count, 1, MinStepsBound));
    }
  }

  /// <summary>Statistical tests of the profile roll; bounds are loose on purpose.</summary>
  public sealed class DiversityTests
  {
    /// <summary>Number of resolved configs the statistics are based on.</summary>
    private const int SampleCount = 2000;

    /// <summary>Resolves the sample configs for a mid-sized graph.</summary>
    private static List<CnfProcNodeRelSizeOpt> ResolveSamples()
    {
      return Enumerable.Range(0, SampleCount).Select(seed => CreateResolver(seed, 30).Execute()).ToList();
    }

    /// <summary>Ratio of the largest to the smallest step value.</summary>
    private static double Spread(CnfProcNodeRelSizeOpt config)
    {
      var values = config.Prepare();
      return (double)values[^1] / values[0];
    }

    [Fact]
    public void Execute_SingleSizeGraphs_AreVeryRare()
    {
      // Act: Count configs with a single step.
      var share = ResolveSamples().Count(config => config.Steps == 1) / (double)SampleCount;

      // Assert: All-same-size graphs happen, but very rarely.
      Assert.InRange(share, 0.02, 0.09);
    }

    [Fact]
    public void Execute_TwoSizeGraphs_AreRare()
    {
      // Act: Count configs with exactly two steps.
      var share = ResolveSamples().Count(config => config.Steps == 2) / (double)SampleCount;

      // Assert: Two-size graphs happen, more often than single-size ones but still rarely.
      Assert.InRange(share, 0.05, 0.16);
    }

    [Fact]
    public void Execute_NearDefaultGraphs_AreCommon()
    {
      // Act: Count configs that look like the default: three geometric steps starting at 1
      // with a growth close to 2.
      var share = ResolveSamples().Count(config =>
        config.Steps == 3 && config.Curve == EnRelSizeCurve.Geometric && config.MinSize == 1 &&
        config.Growth is >= 2f and < 2.5f) / (double)SampleCount;

      // Assert: A sizeable share of graphs keeps the familiar feel.
      Assert.InRange(share, 0.2, 0.5);
    }

    [Fact]
    public void Execute_MultiStepGraphs_CoverSubtleAndWildSpreads()
    {
      // Act: Look at the spread of all configs with at least three steps.
      var spreads = ResolveSamples().Where(config => config.Steps >= 3).Select(Spread).ToList();

      // Assert: Some graphs barely differ in size, others differ wildly.
      Assert.Contains(spreads, spread => spread <= 2.0);
      Assert.Contains(spreads, spread => spread >= 20.0);
    }

    [Fact]
    public void Execute_DrawsBothCurvesAndVariedSkew()
    {
      // Act: Resolve the sample configs.
      var configs = ResolveSamples().Where(config => config.Steps > 1).ToList();

      // Assert: Both curves appear and skew covers low and high concentration.
      Assert.Contains(configs, config => config.Curve == EnRelSizeCurve.Linear);
      Assert.Contains(configs, config => config.Curve == EnRelSizeCurve.Geometric);
      Assert.Contains(configs, config => config.Skew < 0.1f);
      Assert.Contains(configs, config => config.Skew > 0.9f);
    }
  }

  /// <summary>Tests the resolved options in full graph generation.</summary>
  public sealed class IntegrationTests
  {
    [Theory]
    [InlineData("mordor", 3)]
    [InlineData("gondor", 8)]
    [InlineData("rohan", 14)]
    [InlineData("shire", 20)]
    public void Generate_WithResolvedOptions_AssignsOnlyConfiguredSizes(string textSeed, int difficulty)
    {
      // Arrange: Resolve a full configuration.
      var config = LevelGenConfig.Execute(new PropMain { TextSeed = textSeed, Difficulty = difficulty });
      var allowed = config.Graph.ProcOpts.NodeRelSize.Prepare();

      // Act: Generate the graph.
      var world = new LilWorld();
      new GraphGenerator(config, new PrepassGenerator(config).Create(), world).Execute();

      // Assert: Every node got one of the configured step values.
      Assert.NotEmpty(world.Graph.Nodes);
      Assert.All(world.Graph.Nodes, node => Assert.Contains(node.RelSize, allowed));
    }
  }
}
