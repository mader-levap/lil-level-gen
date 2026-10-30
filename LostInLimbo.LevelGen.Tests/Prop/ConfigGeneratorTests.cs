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
      new PropData { Seed = propSeed });
  }

  /// <summary>Tests that the generator rejects invalid property datasets up front.</summary>
  public sealed class ConstructionTests
  {
    [Fact]
    public void Ctor_WithDefaultPropMain_DoesNotThrow()
    {
      // Act/Assert: The default property dataset passes validation at construction.
      _ = new ConfigGenerator(PropMain.Default, PropData.Default);
    }

    [Fact]
    public void Ctor_WithZeroConfigVersion_ThrowsArgumentOutOfRangeExceptionNamingConfigVersion()
    {
      // Arrange: Property dataset with config version below the minimum of 1.
      var propMain = PropMain.Default with { ConfigVersion = 0 };

      // Act/Assert: Construction rejects the version and names the offending parameter.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => new ConfigGenerator(propMain, PropData.Default));
      Assert.Equal(nameof(PropMain.ConfigVersion), exception.ParamName);
    }

    [Fact]
    public void Ctor_WithNegativeDifficulty_ThrowsArgumentOutOfRangeExceptionNamingDifficulty()
    {
      // Arrange: Property dataset with negative difficulty.
      var propMain = PropMain.Default with { Difficulty = -1 };

      // Act/Assert: Construction rejects the difficulty before any drawing happens.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => new ConfigGenerator(propMain, PropData.Default));
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
        () => new ConfigGenerator(propMain, PropData.Default));
      Assert.Equal(nameof(PropMain.Difficulty), exception.ParamName);
    }

    [Fact]
    public void Ctor_WithNullPropMain_ThrowsNullReferenceException()
    {
      // Act/Assert: Null datasets currently surface as NullReferenceException from
      // Validate; pinned so any future ArgumentNullException is an intentional change.
      Assert.Throws<NullReferenceException>(() => new ConfigGenerator(null!, PropData.Default));
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
      Assert.Equal(5, config.Graph.Size);
      Assert.Equal(3, config.Graph.Connectivity);
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
    public void Execute_WithDifficulty_OutputValidatesAndGraphStaysInRange(int difficulty)
    {
      // Act: Generate for a difficulty step.
      var config = CreateGenerator(7, difficulty).Execute();

      // Assert: The configuration validates and the graph stays inside the resolver's
      // difficulty-driven ranges with a builder pairing that satisfies validation.
      config.Validate();
      Assert.InRange(config.Graph.Size, 2, difficulty + 2);
      Assert.InRange(config.Graph.Connectivity, 0, difficulty + config.Graph.Size - 1);
      Assert.InRange(config.Graph.Complexity, 0, Math.Min(config.Graph.Size, difficulty / 2 + 1) - 1);
      Assert.NotEqual(EnGraphBuilder.OneNode, config.Graph.GraphBuilder.Builder);
    }
  }

  /// <summary>Tests clock-seeded behavior of the single-argument constructor.</summary>
  public sealed class ClockSeedTests
  {
    [Fact]
    public void Execute_WithTextSeed_KeepsGeneralSeedStableAcrossRuns()
    {
      // Arrange: Property dataset with a text seed and two clock-seeded generators
      // whose supporting seeds cannot collide (spin until the clock ticks between
      // constructions, so sleep granularity can never let them share a tick).
      var propMain = PropMain.Default with { TextSeed = "mordor" };
      var first = new ConfigGenerator(propMain).Execute();
      WaitForNextClockTick();
      var second = new ConfigGenerator(propMain).Execute();

      // Assert: The general seed comes from the text seed, so it is stable, while the
      // supporting seed comes from the clock, so the resolver's random draws diverge.
      // Degeneration is used as the divergence probe because at difficulty 0 the graph
      // draw space is only a handful of combinations (two clock seeds could legitimately
      // draw the same graph), whereas a continuous float draw colliding is impossible
      // in practice.
      Assert.Equal(first.General.Seed, second.General.Seed);
      Assert.NotEqual(first.General.Degeneration, second.General.Degeneration);
    }

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

  /// <summary>Tests edge cases that currently surface as framework-level exceptions.</summary>
  public sealed class EdgeCaseTests
  {
    [Fact]
    public void Execute_WithNullPropData_ThrowsNullReferenceException()
    {
      // Arrange: Generator carrying a null supporting dataset.
      var generator = new ConfigGenerator(PropMain.Default, null!);

      // Act/Assert: The first resolver construction dereferences the supporting seed.
      Assert.Throws<NullReferenceException>(() => generator.Execute());
    }
  }
}
