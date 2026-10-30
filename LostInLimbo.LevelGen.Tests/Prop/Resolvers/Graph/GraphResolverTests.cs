using LostInLimbo.LevelGen.Data.Config.Graph;
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
      new PropData { Seed = propSeed });
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
      Assert.Equal(5, config.Size);
      Assert.Equal(3, config.Connectivity);
      Assert.Equal(1, config.Complexity);
      Assert.Equal(1, config.ProcOpts.NodeObjectives.GoalAmount);
      Assert.Equal(0, config.ProcOpts.NodeObjectives.OptGoalAmount);
      Assert.Equal(EnGraphBuilder.Linear, config.GraphBuilder.Builder);
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

      // Assert: Every draw stays inside its half-open range for this difficulty and
      // resolved size, so the result always satisfies CnfGraph.Validate.
      Assert.InRange(config.Size, 2, difficulty + 2);
      Assert.InRange(config.Connectivity, 0, difficulty + config.Size - 1);
      Assert.InRange(config.Complexity, 0, Math.Min(config.Size, difficulty / 2 + 1) - 1);
      Assert.InRange(config.ProcOpts.NodeObjectives.GoalAmount, 1, Math.Max(1, difficulty / 2));
      Assert.InRange(config.ProcOpts.NodeObjectives.OptGoalAmount, 0, config.Size - 1);
      config.Validate();
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
    public void Execute_AlwaysEnablesValidatorDisallowingOneWayDeadEnds()
    {
      // Act: Resolve graph configuration.
      var config = CreateResolver(42, 3).Execute();

      // Assert: Graph validation is always enabled with one-way dead ends disallowed.
      Assert.Equal(CnfValidGraphOpt.Default with { Enabled = true, AllowOneWayDeadEnds = false }, config.ValidatorOpts);
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
      // Act: Resolve with the only negative difficulty that keeps NextInt bounds valid
      // (2 .. Difficulty + 3 collapses to equal bounds of 2).
      var config = CreateResolver(42, -1).Execute();

      // Assert: The size draw collapses to its fixed minimum and the config still validates.
      Assert.Equal(2, config.Size);
      config.Validate();
    }

    [Fact]
    public void Execute_WithDifficultyNegativeTwo_ThrowsArgumentOutOfRangeException()
    {
      // Act/Assert: The size range inverts (from 2 to 1) and the draw is rejected.
      Assert.Throws<ArgumentOutOfRangeException>(() => CreateResolver(42, -2).Execute());
    }

    [Fact]
    public void Execute_WithMaxDifficulty_ThrowsArgumentOutOfRangeException()
    {
      // Act/Assert: int.MaxValue + 3 overflows the exclusive upper bound, so the draw
      // is rejected before any invalid size can escape.
      Assert.Throws<ArgumentOutOfRangeException>(() => CreateResolver(42, int.MaxValue).Execute());
    }
  }
}
