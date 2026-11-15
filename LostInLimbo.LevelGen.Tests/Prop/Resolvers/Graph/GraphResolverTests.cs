using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Config.Graph.Validator;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Data;
using LostInLimbo.LevelGen.Internal.Prop.Resolvers.Graph;

namespace LostInLimbo.LevelGen.Tests.Prop.Resolvers.Graph;

/// <summary>
/// Tests graph configuration resolution, grouped into suites by concern:
/// reproducibility of random draws, difficulty-driven ranges, builder pairing and
/// validator options, and difficulty boundary behavior at the resolver level.
/// </summary>
public sealed class GraphResolverTests
{
  /// <summary>Creates a resolver over a property dataset with the given internal seed and difficulty.</summary>
  private static GraphResolver CreateResolver(long propSeed, int difficulty)
  {
    return new GraphResolver(
      new PropMain { TextSeed = "mordor", Difficulty = difficulty },
      PropData.Default with { Seed = propSeed, Size = 2 });
  }

  /// <summary>Tests reproducibility of the resolver's random draws.</summary>
  public sealed class DeterminismTests
  {
    [Fact]
    public void Execute_WithFixedPropSeed_IsDeterministic()
    {
      // Act: Resolve twice from the same internal seed; the clock must not leak in.
      var first = CreateResolver(42, 3).Execute();
      var second = CreateResolver(42, 3).Execute();

      // Assert: The graph configuration is identical draw for draw.
      Assert.Equal(first, second);
    }

    [Fact]
    public void Execute_WithFixedPropSeed_MatchesPinnedValues()
    {
      // Act: Resolve graph configuration from a known internal seed at difficulty 3.
      var config = CreateResolver(42, 3).Execute();

      // Assert: Values match the pinned reference, so the resolver salt and draw order
      // cannot silently change without breaking reproducibility of generated configs.
      Assert.Equal(2, config.Size);
      Assert.Equal(2, config.Connectivity);
      Assert.Equal(1, config.Complexity);
      Assert.Equal(1, config.ProcOpts.NodeObjectives.GoalAmount);
      Assert.Equal(1, config.ProcOpts.NodeObjectives.OptGoalAmount);
      Assert.Equal(EnGraphBuilder.Linear, config.GraphBuilder.Builder);
      Assert.Equal(1, config.ProcOpts.RelSecrets.Amount);
      Assert.Equal(1, config.ProcOpts.NodeStyle.GroupCount);
      // The obstacle placer has no difficulty scaling yet: always its defaults.
      Assert.Equal(CnfProcObstaclePlacerOpt.Default, config.ProcOpts.ObstaclePlacer);
    }

    [Fact]
    public void Execute_WithLargerGraphPickingSimple_MatchesPinnedValues()
    {
      // Act: Resolve a 12-node, many-themes configuration whose seed picks the Simple builder.
      var config = CreateLargeResolver(44).Execute();

      // Assert: Every drawn field matches the pinned reference, including the builder
      // sub-config and the processor options drawn after it.
      Assert.Equal(1, config.Connectivity);
      Assert.Equal(3, config.Complexity);
      Assert.Equal(EnGraphBuilder.Simple, config.GraphBuilder.Builder);
      Assert.Equal(0.5926749f, config.GraphBuilder.CnfSimple!.ChokepointChance);
      Assert.Equal(2, config.GraphBuilder.CnfSimple.MaxExtraDegree);
      Assert.Equal(5, config.GraphBuilder.CnfSimple.MaxExtraDegreeStar);
      AssertProcOpts(config, secretAmount: 2, goalAmount: 2, optGoalAmount: 0, groupCount: 2);
    }

    [Fact]
    public void Execute_WithLargerGraphPickingLinear_MatchesPinnedValues()
    {
      // Act: Resolve a 12-node, many-themes configuration whose seed picks the Linear builder.
      var config = CreateLargeResolver(45).Execute();

      // Assert: Every drawn field matches the pinned reference, including the builder
      // sub-config and the processor options drawn after it.
      Assert.Equal(1, config.Connectivity);
      Assert.Equal(2, config.Complexity);
      Assert.Equal(EnGraphBuilder.Linear, config.GraphBuilder.Builder);
      Assert.Equal(6, config.GraphBuilder.CnfLinear!.MaxEdges);
      Assert.Equal(3, config.GraphBuilder.CnfLinear.MaxExtraDegree);
      Assert.Equal(3, config.GraphBuilder.CnfLinear.MaxConditionPerRel);
      Assert.Equal(4, config.GraphBuilder.CnfLinear.MaxConditionDistance);
      Assert.Equal(2, config.GraphBuilder.CnfLinear.MaxSkipDistance);
      AssertProcOpts(config, secretAmount: 3, goalAmount: 2, optGoalAmount: 2, groupCount: 2);
    }

    /// <summary>Creates a resolver over a 12-node graph at difficulty 7 with many themes.</summary>
    private static GraphResolver CreateLargeResolver(long propSeed)
    {
      return new GraphResolver(
        new PropMain { TextSeed = "mordor", Difficulty = 7, ManyThemes = true },
        PropData.Default with { Seed = propSeed, Size = 12 });
    }

    /// <summary>Asserts the drawn processor options and the fixed secret chances.</summary>
    private static void AssertProcOpts(CnfGraph config, int secretAmount, int goalAmount, int optGoalAmount, int groupCount)
    {
      Assert.Equal(secretAmount, config.ProcOpts.RelSecrets.Amount);
      Assert.Equal(0.1f, config.ProcOpts.RelSecrets.ChanceSecretInSecret);
      Assert.Equal(0.05f, config.ProcOpts.RelSecrets.ChanceTwoPassages);
      Assert.Equal(goalAmount, config.ProcOpts.NodeObjectives.GoalAmount);
      Assert.Equal(optGoalAmount, config.ProcOpts.NodeObjectives.OptGoalAmount);
      Assert.Equal(groupCount, config.ProcOpts.NodeStyle.GroupCount);
    }

    [Fact]
    public void Execute_WithDifferentPropSeeds_ProducesDifferentGraphConfigs()
    {
      // Act: Resolve configurations from two different internal seeds.
      var first = CreateResolver(42, 3).Execute();
      var second = CreateResolver(43, 3).Execute();

      // Assert: The internal seed feeds the draw stream, so configs diverge.
      Assert.NotEqual(first, second);
    }
  }

  /// <summary>Tests that difficulty-driven draws stay inside their documented ranges.</summary>
  public sealed class RangeTests
  {
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(20)]
    public void Execute_WithDifficulty_GraphValuesStayInsideRanges(int difficulty)
    {
      // Act: Resolve graph configuration for a difficulty step.
      var config = CreateResolver(7, difficulty).Execute();

      // Assert: Size is echoed from PropData and every draw stays inside its range for this difficulty and
      // resolved size, so the result always satisfies CnfGraph.Validate.
      Assert.Equal(2, config.Size);
      Assert.InRange(config.Connectivity, 0, GraphResolverExpect.MaxConnectivity(config.Size, difficulty));
      Assert.InRange(config.Complexity, GraphResolverExpect.MinComplexity(config.Size, difficulty), GraphResolverExpect.MaxComplexity(config.Size, difficulty));
      Assert.InRange(config.ProcOpts.RelSecrets.Amount, 0, GraphResolverExpect.MaxSecretAmount(config.Size, difficulty));
      Assert.InRange(config.ProcOpts.NodeObjectives.GoalAmount, 1, GraphResolverExpect.MaxGoalAmount(config.Size, difficulty));
      Assert.InRange(config.ProcOpts.NodeObjectives.OptGoalAmount, 0, GraphResolverExpect.MaxOptGoalAmount(config.Size, difficulty));
      config.Validate();
    }
  }

  /// <summary>
  /// Tests that connectivity and complexity scale with the square root of the size: large
  /// graphs stay moderate while small graphs keep a usable range.
  /// </summary>
  public sealed class ScalingTests
  {
    /// <summary>Number of seeds each scaling test sweeps.</summary>
    private const int SeedCount = 200;

    /// <summary>Resolves configurations for a size and difficulty over a seed sweep.</summary>
    private static List<CnfGraph> ResolveMany(int size, int difficulty)
    {
      return Enumerable.Range(0, SeedCount)
        .Select(seed => new GraphResolver(
          new PropMain { TextSeed = "mordor", Difficulty = difficulty },
          PropData.Default with { Seed = seed, Size = size }).Execute())
        .ToList();
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 2)]
    [InlineData(8, 10)]
    [InlineData(30, 15)]
    [InlineData(4, 20)]
    [InlineData(100, 20)]
    public void Execute_WithSizeAndDifficulty_ReachesButNeverExceedsSqrtBounds(int size, int difficulty)
    {
      // Act: Resolve many configurations for the same size and difficulty.
      var configs = ResolveMany(size, difficulty);
      var maxConnectivity = GraphResolverExpect.MaxConnectivity(size, difficulty);
      var maxComplexity = GraphResolverExpect.MaxComplexity(size, difficulty);
      var maxSecretAmount = GraphResolverExpect.MaxSecretAmount(size, difficulty);
      var maxGoalAmount = GraphResolverExpect.MaxGoalAmount(size, difficulty);
      var maxOptGoalAmount = GraphResolverExpect.MaxOptGoalAmount(size, difficulty);

      // Assert: Every draw stays inside the inclusive bound, and the bound itself is drawn.
      Assert.All(configs, config => Assert.InRange(config.Connectivity, 0, maxConnectivity));
      var minComplexity = GraphResolverExpect.MinComplexity(size, difficulty);
      Assert.All(configs, config => Assert.InRange(config.Complexity, minComplexity, maxComplexity));
      Assert.All(configs, config => Assert.InRange(config.ProcOpts.RelSecrets.Amount, 0, maxSecretAmount));
      Assert.All(configs, config => Assert.InRange(config.ProcOpts.NodeObjectives.GoalAmount, 1, maxGoalAmount));
      Assert.All(configs, config => Assert.InRange(config.ProcOpts.NodeObjectives.OptGoalAmount, 0, maxOptGoalAmount));
      Assert.Contains(configs, config => config.Connectivity == maxConnectivity);
      Assert.Contains(configs, config => config.Complexity == maxComplexity);
      Assert.Contains(configs, config => config.ProcOpts.RelSecrets.Amount == maxSecretAmount);
      Assert.Contains(configs, config => config.ProcOpts.NodeObjectives.GoalAmount == maxGoalAmount);
      Assert.Contains(configs, config => config.ProcOpts.NodeObjectives.OptGoalAmount == maxOptGoalAmount);
    }

    [Fact]
    public void Execute_ForLargestGraphAtMaxDifficulty_StaysSublinear()
    {
      // Act: Resolve the biggest graph at the highest difficulty.
      var configs = ResolveMany(100, PropMain.MaxDifficulty);

      // Assert: Extras stay below a third of the node count, obstacles below a fifth and
      // secrets and goals below a sixth, optional goals at a tenth - the old linear bounds
      // allowed about two extras, half an obstacle, one secret and one optional goal per node.
      Assert.All(configs, config => Assert.True(config.Connectivity <= 30, $"Connectivity {config.Connectivity}."));
      Assert.All(configs, config => Assert.True(config.Complexity <= 20, $"Complexity {config.Complexity}."));
      Assert.All(configs, config => Assert.True(
        config.ProcOpts.RelSecrets.Amount <= 15, $"Secret amount {config.ProcOpts.RelSecrets.Amount}."));
      Assert.All(configs, config => Assert.True(
        config.ProcOpts.NodeObjectives.GoalAmount <= 15, $"Goal amount {config.ProcOpts.NodeObjectives.GoalAmount}."));
      Assert.All(configs, config => Assert.True(
        config.ProcOpts.NodeObjectives.OptGoalAmount <= 10, $"Optional goal amount {config.ProcOpts.NodeObjectives.OptGoalAmount}."));
    }

    [Fact]
    public void Execute_ForSmallGraphAtLowDifficulty_KeepsNonZeroValues()
    {
      // Act: Resolve a small graph at a low difficulty.
      var configs = ResolveMany(5, 5);

      // Assert: Small graphs still get some extras, obstacles and secrets, not just zeros.
      Assert.Contains(configs, config => config.Connectivity >= 2);
      Assert.Contains(configs, config => config.Complexity >= 1);
      Assert.Contains(configs, config => config.ProcOpts.RelSecrets.Amount >= 1);
      Assert.Contains(configs, config => config.ProcOpts.NodeObjectives.GoalAmount >= 2);
      Assert.Contains(configs, config => config.ProcOpts.NodeObjectives.OptGoalAmount >= 2);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 1)]
    [InlineData(10, 5)]
    [InlineData(23, 20)]
    public void Execute_WithDifficultyAboveZero_NeverDrawsZeroComplexity(int size, int difficulty)
    {
      // Act: Resolve many configurations above difficulty 0.
      var configs = ResolveMany(size, difficulty);

      // Assert: Every level gets at least one obstacle.
      Assert.All(configs, config => Assert.True(config.Complexity >= 1, $"Complexity {config.Complexity}."));
    }

    [Theory]
    [InlineData(23, 20, 4)]
    [InlineData(100, 20, 10)]
    [InlineData(50, 10, 3)]
    public void Execute_WithHighDifficulty_ReachesLowerBoundAndNeverGoesBelow(int size, int difficulty, int expectedMin)
    {
      // Act: Resolve many configurations where the lower bound rises above 1.
      var configs = ResolveMany(size, difficulty);

      // Assert: The independent formula agrees with the hand-computed lower bound.
      Assert.Equal(expectedMin, GraphResolverExpect.MinComplexity(size, difficulty));

      // Assert: Nothing falls below the lower bound, and the bound itself is drawn.
      Assert.All(configs, config => Assert.True(config.Complexity >= expectedMin, $"Complexity {config.Complexity}."));
      Assert.Contains(configs, config => config.Complexity == expectedMin);
    }

    [Fact]
    public void Execute_AtDifficultyZero_StillAllowsZeroComplexity()
    {
      // Act: Resolve many of the smallest, easiest configurations.
      var configs = ResolveMany(2, 0);

      // Assert: Difficulty 0 keeps obstacle-free levels possible.
      Assert.Contains(configs, config => config.Complexity == 0);
    }

    [Fact]
    public void Execute_WithMinSizeAtDifficultyOne_DrawsExactlyOne()
    {
      // Act: Resolve the smallest graph difficulty 1 can draw (Size 2).
      var configs = ResolveMany(2, 1);

      // Assert: Lower bound 1 meets the Size - 1 cap of 1.
      Assert.All(configs, config => Assert.Equal(1, config.Complexity));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Execute_WithTinyGraphAtMaxDifficulty_CapsComplexityAtSizeMinusOne(int size)
    {
      // Act: Resolve tiny graphs where the square-root bound would exceed the cut count.
      var configs = ResolveMany(size, PropMain.MaxDifficulty);

      // Assert: There are only size - 1 cuts to guard, and the lower bound never exceeds them.
      Assert.All(configs, config => Assert.InRange(config.Complexity, 1, size - 1));
    }
  }

  /// <summary>Tests graph builder selection and sub-config pairing.</summary>
  public sealed class BuilderTests
  {
    [Fact]
    public void Execute_AnySeed_NeverPicksOneNode()
    {
      // Act: Resolve across a seed sweep covering both remaining builders.
      for (long seed = 0; seed < 64; seed++)
      {
        var config = CreateResolver(seed, 5).Execute();

        // Assert: OneNode is excluded from random builder picking.
        Assert.NotEqual(EnGraphBuilder.OneNode, config.GraphBuilder.Builder);
      }
    }

    [Fact]
    public void Execute_SeedSweep_CarriesMatchingSubConfigWithResolverValues()
    {
      // Arrange: Flags tracking which builders the sweep observed.
      var seenSimple = false;
      var seenLinear = false;

      // Act: Resolve across a seed sweep wide enough to hit both builders.
      for (long seed = 0; seed < 64 && (!seenSimple || !seenLinear); seed++)
      {
        var builder = CreateResolver(seed, 5).Execute().GraphBuilder;

        // Assert: The populated sub-config matches the selected builder and carries
        // values inside the builder resolver's ranges; the unselected section stays null.
        switch (builder.Builder)
        {
          case EnGraphBuilder.Simple:
            Assert.NotNull(builder.CnfSimple);
            Assert.Null(builder.CnfLinear);
            Assert.InRange(builder.CnfSimple!.ChokepointChance, 0.2f, 0.95f);
            Assert.InRange(builder.CnfSimple!.MaxExtraDegree, 2, 5);
            Assert.InRange(builder.CnfSimple!.MaxExtraDegreeStar, 4, 8);
            seenSimple = true;
            break;
          case EnGraphBuilder.Linear:
            Assert.NotNull(builder.CnfLinear);
            Assert.Null(builder.CnfSimple);
            Assert.InRange(builder.CnfLinear!.MaxEdges, 3, 6);
            Assert.InRange(builder.CnfLinear!.MaxExtraDegree, 2, 4);
            Assert.InRange(builder.CnfLinear!.MaxConditionPerRel, 1, 4);
            Assert.InRange(builder.CnfLinear!.MaxConditionDistance, 2, 4);
            Assert.InRange(builder.CnfLinear!.MaxSkipDistance, 1, 3);
            seenLinear = true;
            break;
          default:
            throw new InvalidOperationException($"Unexpected builder {builder.Builder}.");
        }
      }

      // Assert: The sweep observed both builders, so both branches are covered.
      Assert.True(seenSimple, "Seed sweep never picked the Simple builder.");
      Assert.True(seenLinear, "Seed sweep never picked the Linear builder.");
    }

    [Fact]
    public void Execute_SeedSweep_BuilderSubConfigMatchesStandaloneBuilderResolver()
    {
      // Arrange: Flags tracking which builders the sweep observed.
      var seenSimple = false;
      var seenLinear = false;

      // Act: Resolve across a seed sweep wide enough to hit both builders.
      for (long seed = 0; seed < 64 && (!seenSimple || !seenLinear); seed++)
      {
        var propMain = new PropMain { TextSeed = "mordor", Difficulty = 5 };
        var propData = PropData.Default with { Seed = seed, Size = 2 };
        var builder = new GraphResolver(propMain, propData).Execute().GraphBuilder;

        // Assert: The builder sub-config equals a standalone resolve from the same data,
        // so builder options run on their own sub-seed, independent of how many draws the
        // graph resolver made before them (and its later draws are independent of them).
        switch (builder.Builder)
        {
          case EnGraphBuilder.Simple:
            Assert.Equal(new BuilderSimpleResolver(propMain, propData).Execute(), builder.CnfSimple);
            seenSimple = true;
            break;
          case EnGraphBuilder.Linear:
            Assert.Equal(new BuilderLinearResolver(propMain, propData).Execute(), builder.CnfLinear);
            seenLinear = true;
            break;
          default:
            throw new InvalidOperationException($"Unexpected builder {builder.Builder}.");
        }
      }

      // Assert: The sweep observed both builders, so both branches are covered.
      Assert.True(seenSimple, "Seed sweep never picked the Simple builder.");
      Assert.True(seenLinear, "Seed sweep never picked the Linear builder.");
    }

    [Fact]
    public void Execute_AlwaysEnablesValidatorDisallowingOneWayDeadEnds()
    {
      // Act: Resolve graph configuration.
      var config = CreateResolver(42, 3).Execute();

      // Assert: Graph validation is always enabled with one-way dead ends disallowed.
      Assert.Equal(CnfValidGraphOpt.Default with { Enabled = true, AllowOneWayDeadEnds = false }, config.ValidatorOpts);
    }
  }

  /// <summary>Tests the effect of <see cref="PropMain.ManyThemes"/> on resolved style options.</summary>
  public sealed class ManyThemesTests
  {
    /// <summary>Creates a resolver over a property dataset with full control over size and ManyThemes.</summary>
    private static GraphResolver CreateResolver(long propSeed, int difficulty, int size, bool manyThemes)
    {
      return new GraphResolver(
        new PropMain { TextSeed = "mordor", Difficulty = difficulty, ManyThemes = manyThemes },
        PropData.Default with { Seed = propSeed, Size = size });
    }

    [Fact]
    public void Execute_ManyThemesFalse_GroupCountIsOne()
    {
      // Act: Resolve with ManyThemes disabled across several sizes.
      for (int size = 2; size <= 10; size++)
      {
        var config = CreateResolver(42, 3, size, manyThemes: false).Execute();

        // Assert: Group count stays at its default of 1 when themes are not requested.
        Assert.Equal(1, config.ProcOpts.NodeStyle.GroupCount);
      }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(30)]
    [InlineData(100)]
    public void Execute_ManyThemesTrue_GroupCountInsideRange(int size)
    {
      // Arrange: Expected inclusive upper bound from the resolver's rule: max(2, ceil(sqrt(Size))).
      int groupCountMax = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(size)));

      // Act & Assert: Across a seed sweep, group count always lands in [2, groupCountMax].
      for (long seed = 0; seed < 32; seed++)
      {
        var config = CreateResolver(seed, 3, size, manyThemes: true).Execute();
        Assert.InRange(config.ProcOpts.NodeStyle.GroupCount, 2, groupCountMax);
      }
    }

    [Fact]
    public void Execute_ManyThemesTrue_GroupCountMaxReachable()
    {
      // Arrange: At Size 16 the inclusive bound is ceil(sqrt(16)) = 4.
      var seen = new HashSet<int>();

      // Act: Resolve across many supporting seeds.
      for (long seed = 0; seed < 128; seed++)
        seen.Add(CreateResolver(seed, 3, 16, manyThemes: true).Execute().ProcOpts.NodeStyle.GroupCount);

      // Assert: Both ends of the inclusive range are reached and nothing exceeds it.
      Assert.Contains(2, seen);
      Assert.Contains(4, seen);
      Assert.All(seen, groupCount => Assert.InRange(groupCount, 2, 4));
    }

    [Fact]
    public void Execute_ManyThemesTrue_GroupCountIndependentOfDifficulty()
    {
      // Arrange: Size 100 gives inclusive bound ceil(sqrt(100)) = 10 regardless of difficulty.
      const int size = 100;
      const int groupCountMax = 10;

      // Act & Assert: Lowest and highest difficulty both stay inside the same range.
      for (long seed = 0; seed < 32; seed++)
      {
        var low = CreateResolver(seed, 0, size, manyThemes: true).Execute();
        var high = CreateResolver(seed, PropMain.MaxDifficulty, size, manyThemes: true).Execute();
        Assert.InRange(low.ProcOpts.NodeStyle.GroupCount, 2, groupCountMax);
        Assert.InRange(high.ProcOpts.NodeStyle.GroupCount, 2, groupCountMax);
      }
    }

    [Fact]
    public void Execute_ManyThemesTrue_WithFixedSeed_IsDeterministic()
    {
      // Act: Resolve twice from the same supporting seed and settings.
      var first = CreateResolver(42, 3, 10, manyThemes: true).Execute();
      var second = CreateResolver(42, 3, 10, manyThemes: true).Execute();

      // Assert: Identical inputs produce identical group counts.
      Assert.Equal(first.ProcOpts.NodeStyle.GroupCount, second.ProcOpts.NodeStyle.GroupCount);
    }

    [Fact]
    public void Execute_ManyThemesTrue_WithDifferentSeeds_CanDiverge()
    {
      // Arrange: Collect distinct group counts across a seed sweep.
      var seen = new HashSet<int>();

      // Act: Resolve across many supporting seeds at a size that admits multiple groups.
      for (long seed = 0; seed < 64; seed++)
      {
        var config = CreateResolver(seed, 3, 10, manyThemes: true).Execute();
        seen.Add(config.ProcOpts.NodeStyle.GroupCount);
      }

      // Assert: More than one group count is reachable, confirming the random draw is active.
      Assert.True(seen.Count > 1, $"Only one group count observed: {string.Join(", ", seen)}.");
    }

    [Fact]
    public void Execute_ManyThemesTrue_SizeTwo_GroupCountIsTwo()
    {
      // Act: Resolve at Size==2, where ceil(sqrt(2)) = 2 and the minimum of 2 meet.
      var config = CreateResolver(42, 3, 2, manyThemes: true).Execute();

      // Assert: The single-value range [2, 2] yields exactly two groups.
      Assert.Equal(2, config.ProcOpts.NodeStyle.GroupCount);
    }
  }

  /// <summary>
  /// Tests difficulty boundaries at the resolver level. GraphResolver does not call
  /// <c>PropMain.Validate</c>, so these tests reach values the generator would reject.
  /// </summary>
  public sealed class DifficultyBoundaryTests
  {
    [Fact]
    public void Execute_WithDifficultyNegativeOne_ReturnsMinimumSize()
    {
      // Act: Resolve with a negative difficulty; size comes fixed from PropData (2), so only
      // the square-root bounds react to the difficulty.
      var config = CreateResolver(42, -1).Execute();

      // Assert: The size draw collapses to its fixed minimum and the config still validates.
      Assert.Equal(2, config.Size);
      config.Validate();
    }

    [Fact]
    public void Execute_WithDifficultyNegativeTwo_StillProducesValidConfig()
    {
      // Act: Resolve slightly below the difficulty range; the square-root bounds only shrink
      // a little and stay at or above their minimums, so no draw range inverts.
      var config = CreateResolver(42, -2).Execute();

      // Assert: The config still validates. Rejecting such a difficulty is the job of
      // PropMain.Validate in the config generator, not of this resolver.
      Assert.True(config.ProcOpts.NodeObjectives.GoalAmount >= 1);
      config.Validate();
    }

    [Fact]
    public void Execute_WithIntMaxDifficulty_BoundsDoNotWrapAround()
    {
      // Act: Resolve far above PropMain.MaxDifficulty; the square-root bounds are computed
      // in floating point, so no integer bound overflows into a negative range.
      var config = CreateResolver(42, int.MaxValue).Execute();

      // Assert: The draws stay non-negative. Rejecting such a difficulty is the job of
      // PropMain.Validate in the config generator, not of this resolver.
      Assert.True(config.Connectivity >= 0);
      Assert.True(config.Complexity >= 0);
      Assert.True(config.ProcOpts.RelSecrets.Amount >= 0);
    }
  }
}
