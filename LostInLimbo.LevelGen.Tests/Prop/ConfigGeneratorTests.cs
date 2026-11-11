using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Style;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Data;

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
    public void Ctor_WithNullPropMain_ThrowsNullReferenceException()
    {
      // Act/Assert: Null datasets currently surface as NullReferenceException from
      // Validate; pinned so any future ArgumentNullException is an intentional change.
      Assert.Throws<NullReferenceException>(() => new ConfigGenerator(null!, PropData.DefaultSeed));
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
      Assert.Equal(3, config.Graph.Size);
      Assert.Equal(5, config.Graph.Connectivity);
      Assert.Equal(1, config.Graph.Complexity);
      Assert.Equal(EnGraphBuilder.Linear, config.Graph.GraphBuilder.Builder);
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
    [InlineData(20)]
    [InlineData(50)]
    [InlineData(99)]
    [InlineData(100)]
    public void Execute_WithDifficulty_OutputValidatesAndGraphStaysInRange(int difficulty)
    {
      // Act: Generate for a difficulty step.
      var config = CreateGenerator(7, difficulty).Execute();

      // Assert: The configuration validates and the graph stays inside the resolver's
      // difficulty-driven ranges with a builder pairing that satisfies validation.
      config.Validate();
      Assert.InRange(config.Graph.Size, 2, Math.Min(difficulty + 2, CnfGraph.MaxSize));
      Assert.InRange(config.Graph.Connectivity, 0, difficulty + config.Graph.Size - 1);
      Assert.InRange(config.Graph.Complexity, 0, Math.Min(config.Graph.Size, difficulty / 2 + 1) - 1);
      Assert.NotEqual(EnGraphBuilder.OneNode, config.Graph.GraphBuilder.Builder);
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
    }

    /// <summary>Pinned world seed for text seed "mordor" (raw text-seed hash).</summary>
    private const long PinnedGeneralSeed = -8341167147440548823L;
    /// <summary>Pinned degeneration for "mordor" at difficulty 3.</summary>
    private const float PinnedDegeneration = 49.082054f;
    /// <summary>Pinned graph size for "mordor" at difficulty 3.</summary>
    private const int PinnedGraphSize = 2;
    /// <summary>Pinned graph connectivity for "mordor" at difficulty 3.</summary>
    private const int PinnedConnectivity = 1;
    /// <summary>Pinned graph complexity for "mordor" at difficulty 3.</summary>
    private const int PinnedComplexity = 1;
    /// <summary>Pinned graph builder for "mordor" at difficulty 3.</summary>
    private const EnGraphBuilder PinnedBuilder = EnGraphBuilder.Linear;

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
      int groupCountMax = Math.Max(2, size * 2 / 3 + 1);
      Assert.InRange(config.Graph.ProcOpts.NodeStyle.GroupCount, 2, groupCountMax);
    }
  }
}
