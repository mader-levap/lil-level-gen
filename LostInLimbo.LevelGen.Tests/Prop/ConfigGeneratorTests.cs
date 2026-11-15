using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Builder;
using LostInLimbo.LevelGen.Data.Config.Style;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Data;
using LostInLimbo.LevelGen.Internal.Utils.Rng;
using LostInLimbo.LevelGen.Tests.Prop.Resolvers.Graph;

namespace LostInLimbo.LevelGen.Tests.Prop;

/// <summary>
/// Tests full configuration generation from a property dataset, grouped into suites by
/// concern: constructor validation, reproducibility via the injectable <see cref="PropData"/>
/// seam, output structure, difficulty-driven ranges, clock-seeded behavior, and edge cases.
/// </summary>
public sealed class ConfigGeneratorTests
{
  /// <summary>Creates a generator over a fixed supporting seed for deterministic tests.</summary>
  private static ConfigGenerator CreateGenerator(long propSeed, int difficulty = 0, string? textSeed = "mordor")
  {
    return new ConfigGenerator(
      new PropMain { TextSeed = textSeed, Difficulty = difficulty },
      propSeed);
  }

  /// <summary>Tests that the generator rejects invalid property datasets up front.</summary>
  public sealed class ConstructionTests
  {
    [Fact]
    public void Ctor_WithDefaultPropMain_DoesNotThrow()
    {
      // Act/Assert: The default property dataset passes validation at construction.
      _ = new ConfigGenerator(PropMain.Default, PropData.DefaultSeed);
    }

    [Fact]
    public void Ctor_WithZeroConfigVersion_ThrowsArgumentOutOfRangeExceptionNamingConfigVersion()
    {
      // Arrange: Property dataset with config version below the minimum of 1.
      var propMain = PropMain.Default with { ConfigVersion = 0 };

      // Act/Assert: Construction rejects the version and names the offending parameter.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => new ConfigGenerator(propMain, PropData.DefaultSeed));
      Assert.Equal(nameof(PropMain.ConfigVersion), exception.ParamName);
    }

    [Fact]
    public void Ctor_WithNegativeDifficulty_ThrowsArgumentOutOfRangeExceptionNamingDifficulty()
    {
      // Arrange: Property dataset with negative difficulty.
      var propMain = PropMain.Default with { Difficulty = -1 };

      // Act/Assert: Construction rejects the difficulty before any drawing happens.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => new ConfigGenerator(propMain, PropData.DefaultSeed));
      Assert.Equal(nameof(PropMain.Difficulty), exception.ParamName);
    }

    [Fact]
    public void Ctor_WithMaxDifficulty_ThrowsArgumentOutOfRangeExceptionNamingDifficulty()
    {
      // Arrange: Property dataset above the difficulty ceiling, which would otherwise
      // overflow the graph size range inside GraphResolver.
      var propMain = PropMain.Default with { Difficulty = int.MaxValue };

      // Act/Assert: Construction rejects the difficulty before any drawing happens.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => new ConfigGenerator(propMain, PropData.DefaultSeed));
      Assert.Equal(nameof(PropMain.Difficulty), exception.ParamName);
    }

    [Fact]
    public void Ctor_WithNullPropMain_ThrowsArgumentNullException()
    {
      // Act/Assert: The seam constructor rejects a missing dataset naming the parameter.
      var exception = Assert.Throws<ArgumentNullException>(() => new ConfigGenerator(null!, PropData.DefaultSeed));
      Assert.Equal("propMain", exception.ParamName);
    }

    [Fact]
    public void PublicCtor_WithNullPropMain_ThrowsArgumentNullException()
    {
      // Act/Assert: The public-path constructor rejects a missing dataset naming the parameter.
      var exception = Assert.Throws<ArgumentNullException>(() => new ConfigGenerator(null!));
      Assert.Equal("propMain", exception.ParamName);
    }
  }

  /// <summary>Tests reproducibility through the injectable supporting-data seam.</summary>
  public sealed class DeterminismTests
  {
    [Fact]
    public void Execute_WithFixedInputs_IsDeterministic()
    {
      // Act: Generate twice from identical property and supporting datasets.
      var first = CreateGenerator(42, 3).Execute();
      var second = CreateGenerator(42, 3).Execute();

      // Assert: The full configuration is identical draw for draw.
      Assert.Equal(first, second);
    }

    [Fact]
    public void Execute_WithFixedInputs_MatchesPinnedValues()
    {
      // Act: Generate from a known internal seed at difficulty 3.
      var config = CreateGenerator(42, 3).Execute();

      // Assert: Values match the pinned reference, so resolver salts and draw order
      // cannot silently change without breaking reproducibility of generated configs.
      Assert.Equal(-8341167147440548823L, config.General.Seed);
      Assert.Equal(16.201443f, config.General.Degeneration);
      Assert.Equal(6, config.Graph.Size);
      Assert.Equal(2, config.Graph.Connectivity);
      Assert.Equal(2, config.Graph.Complexity);
      Assert.Equal(EnGraphBuilder.Linear, config.Graph.GraphBuilder.Builder);
      Assert.Equal(CnfGraphBuilderLinear.Default with
      {
        MaxEdges = 5, MaxExtraDegree = 2, MaxConditionPerRel = 2, MaxConditionDistance = 2, MaxSkipDistance = 1, ChanceObstacleBypass = 0.0075154635f,
      }, config.Graph.GraphBuilder.CnfLinear);
      Assert.Equal(1, config.Graph.ProcOpts.RelSecrets.Amount);
      Assert.Equal(2, config.Graph.ProcOpts.NodeObjectives.GoalAmount);
      Assert.Equal(0, config.Graph.ProcOpts.NodeObjectives.OptGoalAmount);
      Assert.Equal(1, config.Graph.ProcOpts.NodeStyle.GroupCount);
    }

    [Fact]
    public void Execute_WithDifferentPropSeeds_ProducesDifferentGraphConfigs()
    {
      // Act: Generate from two different supporting seeds.
      var first = CreateGenerator(42, 3).Execute();
      var second = CreateGenerator(43, 3).Execute();

      // Assert: The supporting seed feeds the draw streams, so configs diverge.
      Assert.NotEqual(first.Graph, second.Graph);
    }
  }

  /// <summary>Tests that every section is populated and the output validates.</summary>
  public sealed class StructureTests
  {
    [Fact]
    public void Execute_WithDefaultInputs_ProducesValidConfiguration()
    {
      // Act: Generate from a default-shaped property dataset.
      var config = CreateGenerator(42).Execute();

      // Act/Assert: The generated configuration validates end to end.
      config.Validate();
    }

    [Fact]
    public void Execute_WithDefaultInputs_FillsAllSectionsAndLeavesDebugNull()
    {
      // Act: Generate from a default-shaped property dataset.
      var config = CreateGenerator(42).Execute();

      // Assert: Every resolvable section is populated and wired, style stays at its
      // placeholder default, and the debug section is left off.
      Assert.NotNull(config.General);
      Assert.NotNull(config.Style);
      Assert.NotNull(config.Graph);
      Assert.NotNull(config.Map);
      Assert.Equal(CnfStyle.Default, config.Style);
      Assert.Equal(CnfLevelGeneration.CurrentConfigVersion, config.ConfigVersion);
      Assert.Null(config.Debug);
    }
  }

  /// <summary>Tests that generated values stay valid across the difficulty axis.</summary>
  public sealed class DifficultyTheory
  {
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(10)]
    [InlineData(19)]
    [InlineData(20)]
    public void Execute_WithDifficulty_OutputValidatesAndGraphStaysInRange(int difficulty)
    {
      // Act: Generate for a difficulty step.
      var config = CreateGenerator(7, difficulty).Execute();

      // Assert: The configuration validates and the graph stays inside the resolver's
      // difficulty-driven ranges with a builder pairing that satisfies validation.
      config.Validate();
      var (sizeMin, sizeMax) = ConfigGenerator.ResolveSizeRange(difficulty);
      Assert.InRange(config.Graph.Size, sizeMin, sizeMax);
      Assert.InRange(config.Graph.Connectivity, 0, GraphResolverExpect.MaxConnectivity(config.Graph.Size, difficulty));
      Assert.InRange(config.Graph.Complexity, GraphResolverExpect.MinComplexity(config.Graph.Size, difficulty), GraphResolverExpect.MaxComplexity(config.Graph.Size, difficulty));
      Assert.InRange(config.Graph.ProcOpts.RelSecrets.Amount, 0, GraphResolverExpect.MaxSecretAmount(config.Graph.Size, difficulty));
      Assert.InRange(config.Graph.ProcOpts.NodeObjectives.GoalAmount, 1, GraphResolverExpect.MaxGoalAmount(config.Graph.Size, difficulty));
      Assert.InRange(config.Graph.ProcOpts.NodeObjectives.OptGoalAmount, 0, GraphResolverExpect.MaxOptGoalAmount(config.Graph.Size, difficulty));
      Assert.NotEqual(EnGraphBuilder.OneNode, config.Graph.GraphBuilder.Builder);
    }
  }

  /// <summary>
  /// Tests how difficulty maps to the graph size range: a slowly stepping lower bound and a
  /// shifted square-root upper bound, both inclusive.
  /// </summary>
  public sealed class SizeRangeTests
  {
    /// <summary>Number of seeds the size sweep draws per difficulty.</summary>
    private const int SeedCount = 400;

    [Theory]
    [InlineData(0, 2, 2)]
    [InlineData(1, 2, 4)]
    [InlineData(3, 3, 8)]
    [InlineData(4, 3, 9)]
    [InlineData(10, 5, 15)]
    [InlineData(20, 8, 23)]
    public void ResolveSizeRange_WithDefaults_MatchesPinnedTable(int difficulty, int expectedMin, int expectedMax)
    {
      // Act: Resolve the inclusive size range for a difficulty.
      var (min, max) = ConfigGenerator.ResolveSizeRange(difficulty);

      // Assert: Bounds match the pinned table for the default growth constants.
      Assert.Equal(expectedMin, min);
      Assert.Equal(expectedMax, max);
    }

    [Fact]
    public void ResolveSizeRange_AcrossDifficulty_IsMonotonicAndOrdered()
    {
      // Arrange: Range at difficulty 0 as the starting point.
      var previous = ConfigGenerator.ResolveSizeRange(0);

      for (int difficulty = 0; difficulty <= PropMain.MaxDifficulty; difficulty++)
      {
        // Act: Resolve the range for the next difficulty.
        var current = ConfigGenerator.ResolveSizeRange(difficulty);

        // Assert: Bounds are ordered, inside graph limits, and never shrink with difficulty.
        Assert.True(current.Min <= current.Max, $"Difficulty {difficulty}: min {current.Min} > max {current.Max}.");
        Assert.True(current.Min >= 2, $"Difficulty {difficulty}: min {current.Min} below 2.");
        Assert.True(current.Max <= CnfGraph.MaxSize, $"Difficulty {difficulty}: max {current.Max} above MaxSize.");
        Assert.True(current.Min >= previous.Min, $"Difficulty {difficulty}: min shrank.");
        Assert.True(current.Max >= previous.Max, $"Difficulty {difficulty}: max shrank.");
        previous = current;
      }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(20)]
    public void Execute_WithSeedSweep_SizeStaysInsideAndReachesBounds(int difficulty)
    {
      // Arrange: Expected inclusive range for this difficulty.
      var (min, max) = ConfigGenerator.ResolveSizeRange(difficulty);

      // Act: Generate many configurations across seeds.
      var sizes = Enumerable.Range(0, SeedCount)
        .Select(seed => CreateGenerator(seed, difficulty).Execute().Graph.Size)
        .ToList();

      // Assert: Every size is inside the range and both bounds are actually drawn.
      Assert.All(sizes, size => Assert.InRange(size, min, max));
      Assert.Contains(min, sizes);
      Assert.Contains(max, sizes);
    }

    [Fact]
    public void Execute_AtZeroDifficulty_SizeIsMinimum()
    {
      // Act: Generate at the easiest difficulty.
      var config = CreateGenerator(42, 0).Execute();

      // Assert: The range collapses to the minimum graph size.
      Assert.Equal(2, config.Graph.Size);
    }
  }

  /// <summary>
  /// Tests where the single-argument constructor takes its seeds from: the text seed when
  /// given (fully reproducible config), otherwise the clock.
  /// </summary>
  /// <remarks>
  /// Degeneration is used as the divergence probe because it is a continuous float draw;
  /// two different seeds drawing the same value is impossible in practice, unlike the
  /// small discrete graph draw space at low difficulty.
  /// </remarks>
  public sealed class SeedSourceTests
  {
    [Fact]
    public void Execute_WithTextSeed_ProducesIdenticalConfigurationAcrossRuns()
    {
      // Arrange: Property dataset with a text seed and a large draw space.
      var propMain = new PropMain { TextSeed = "mordor", Difficulty = 10 };

      // Act: Generate twice, with the clock ticking between runs so a clock-derived
      // seed could not accidentally match.
      var first = new ConfigGenerator(propMain).Execute();
      WaitForNextClockTick();
      var second = new ConfigGenerator(propMain).Execute();

      // Assert: The whole configuration comes from the text seed, so it is identical.
      Assert.Equal(first, second);
    }

    [Fact]
    public void Execute_WithDifferentTextSeeds_ProducesDifferentConfigurations()
    {
      // Act: Generate from two different text seeds.
      var first = new ConfigGenerator(new PropMain { TextSeed = "mordor", Difficulty = 10 }).Execute();
      var second = new ConfigGenerator(new PropMain { TextSeed = "gondor", Difficulty = 10 }).Execute();

      // Assert: Both the world seed and the config draws diverge.
      Assert.NotEqual(first.General.Seed, second.General.Seed);
      Assert.NotEqual(first.General.Degeneration, second.General.Degeneration);
    }

    [Fact]
    public void Execute_WithTextSeed_UsesTextHashAsGeneralSeed()
    {
      // Act: Generate from a known text seed.
      var config = new ConfigGenerator(new PropMain { TextSeed = "mordor" }).Execute();

      // Assert: The world seed is the raw FromText hash.
      Assert.Equal(SeedDerivation.FromText("mordor"), config.General.Seed);
    }

    [Fact]
    public void Execute_WithTextSeedDifferingByCase_ProducesDifferentGeneralSeeds()
    {
      // Act: Generate from text seeds that differ only in case.
      var lower = new ConfigGenerator(new PropMain { TextSeed = "mordor" }).Execute();
      var upper = new ConfigGenerator(new PropMain { TextSeed = "Mordor" }).Execute();

      // Assert: Text seeding is case-sensitive.
      Assert.NotEqual(lower.General.Seed, upper.General.Seed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Execute_WithBlankTextSeed_UsesClockForGeneralSeed(string textSeed)
    {
      // Arrange: Empty and whitespace-only text seeds count as "no text seed given".
      var propMain = new PropMain { TextSeed = textSeed };

      // Act: Capture the clock window around generation.
      var before = DateTime.UtcNow.Ticks;
      var config = new ConfigGenerator(propMain).Execute();
      var after = DateTime.UtcNow.Ticks;

      // Assert: The general seed is a timestamp taken between the two clock reads.
      Assert.InRange(config.General.Seed, before, after);
    }

    [Fact]
    public void Execute_WithoutTextSeed_ProducesDifferentConfigurationsAcrossRuns()
    {
      // Arrange: Property dataset without a text seed.
      var propMain = new PropMain { Difficulty = 10 };

      // Act: Generate twice with the clock ticking between runs.
      var first = new ConfigGenerator(propMain).Execute();
      WaitForNextClockTick();
      var second = new ConfigGenerator(propMain).Execute();

      // Assert: The config draws come from the clock, so they diverge.
      Assert.NotEqual(first.General.Degeneration, second.General.Degeneration);
    }

    [Fact]
    public void Execute_WithTextSeed_MatchesPinnedValues()
    {
      // Act: Generate from a known text seed through the public-path constructor.
      var config = new ConfigGenerator(new PropMain { TextSeed = "mordor", Difficulty = 3 }).Execute();

      // Assert: Values match the pinned reference, so a text seed keeps mapping to the
      // same level across library versions unless this test is deliberately updated.
      config.Validate();
      Assert.Equal(PinnedGeneralSeed, config.General.Seed);
      Assert.Equal(PinnedDegeneration, config.General.Degeneration);
      Assert.Equal(PinnedGraphSize, config.Graph.Size);
      Assert.Equal(PinnedConnectivity, config.Graph.Connectivity);
      Assert.Equal(PinnedComplexity, config.Graph.Complexity);
      Assert.Equal(PinnedBuilder, config.Graph.GraphBuilder.Builder);
      Assert.Equal(PinnedLinear, config.Graph.GraphBuilder.CnfLinear);
      Assert.Equal(PinnedSecretAmount, config.Graph.ProcOpts.RelSecrets.Amount);
      Assert.Equal(PinnedGoalAmount, config.Graph.ProcOpts.NodeObjectives.GoalAmount);
      Assert.Equal(PinnedOptGoalAmount, config.Graph.ProcOpts.NodeObjectives.OptGoalAmount);
      Assert.Equal(PinnedGroupCount, config.Graph.ProcOpts.NodeStyle.GroupCount);
    }

    /// <summary>Pinned world seed for text seed "mordor" (raw text-seed hash).</summary>
    private const long PinnedGeneralSeed = -8341167147440548823L;
    /// <summary>Pinned degeneration for "mordor" at difficulty 3.</summary>
    private const float PinnedDegeneration = 49.082054f;
    /// <summary>Pinned graph size for "mordor" at difficulty 3.</summary>
    private const int PinnedGraphSize = 7;
    /// <summary>Pinned graph connectivity for "mordor" at difficulty 3.</summary>
    private const int PinnedConnectivity = 1;
    /// <summary>Pinned graph complexity for "mordor" at difficulty 3.</summary>
    private const int PinnedComplexity = 2;
    /// <summary>Pinned graph builder for "mordor" at difficulty 3.</summary>
    private const EnGraphBuilder PinnedBuilder = EnGraphBuilder.Linear;
    /// <summary>Pinned Linear builder options for "mordor" at difficulty 3.</summary>
    private static readonly CnfGraphBuilderLinear PinnedLinear = CnfGraphBuilderLinear.Default with
    {
      MaxEdges = 6, MaxExtraDegree = 2, MaxConditionPerRel = 1, MaxConditionDistance = 2, MaxSkipDistance = 1, ChanceObstacleBypass = 0.024041189f,
    };
    /// <summary>Pinned secret amount for "mordor" at difficulty 3.</summary>
    private const int PinnedSecretAmount = 1;
    /// <summary>Pinned mandatory goal amount for "mordor" at difficulty 3.</summary>
    private const int PinnedGoalAmount = 1;
    /// <summary>Pinned optional goal amount for "mordor" at difficulty 3.</summary>
    private const int PinnedOptGoalAmount = 0;
    /// <summary>Pinned style group count for "mordor" at difficulty 3.</summary>
    private const int PinnedGroupCount = 1;

    [Fact]
    public void Execute_WithoutTextSeed_UsesClockForGeneralSeed()
    {
      // Arrange: Property dataset without a text seed; capture the clock window around
      // generation so the fallback seed can be bracketed.
      var before = DateTime.UtcNow.Ticks;
      var config = new ConfigGenerator(PropMain.Default).Execute();
      var after = DateTime.UtcNow.Ticks;

      // Assert: The general seed is a timestamp taken between the two clock reads.
      Assert.InRange(config.General.Seed, before, after);
    }

    [Fact]
    public void Execute_WithoutTextSeed_ReportedSeedReproducesConfigDraws()
    {
      // Arrange: Property dataset without a text seed and a large draw space.
      var propMain = new PropMain { Difficulty = 10 };

      // Act: Generate from the clock, then replay the config draws from the internal seed
      // derived from the reported world seed.
      var config = new ConfigGenerator(propMain).Execute();
      var internalSeed = SeedDerivation.Derive(config.General.Seed, ConfigGenerator.SaltPropSeed);
      var replay = new ConfigGenerator(propMain, internalSeed).Execute();

      // Assert: The clock is read once, so the reported world seed alone reproduces every
      // config draw.
      Assert.Equal(config.General.Degeneration, replay.General.Degeneration);
      Assert.Equal(config.Graph, replay.Graph);
      Assert.Equal(config.Map, replay.Map);
    }

    /// <summary>Spins until the wall clock advances, so two clock-seeded constructions cannot share a tick.</summary>
    private static void WaitForNextClockTick()
    {
      var previous = DateTime.UtcNow;
      while (DateTime.UtcNow == previous)
      {
        // Busy-wait: a tick is shorter than any sleep granularity.
      }
    }
  }

  /// <summary>Tests end-to-end behavior of <see cref="PropMain.ManyThemes"/>.</summary>
  public sealed class ManyThemesTests
  {
    [Fact]
    public void Execute_WithManyThemesTrue_OutputValidatesAndGroupCountInRange()
    {
      // Arrange: Property dataset requesting many themes at difficulty 3.
      var propMain = new PropMain { TextSeed = "mordor", Difficulty = 3, ManyThemes = true };

      // Act: Generate from a fixed supporting seed.
      var config = new ConfigGenerator(propMain, PropData.DefaultSeed).Execute();

      // Assert: The full configuration validates end to end.
      config.Validate();

      // Assert: Group count is at least two and within the resolver's upper bound.
      var size = config.Graph.Size;
      int groupCountMax = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(size)));
      Assert.InRange(config.Graph.ProcOpts.NodeStyle.GroupCount, 2, groupCountMax);
    }
  }
}
