using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Data;
using LostInLimbo.LevelGen.Internal.Prop.Resolvers;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Tests.Prop.Resolvers;

/// <summary>
/// Tests general configuration resolution, grouped into suites by concern:
/// seed resolution from a text seed and from the fallback clock.
/// </summary>
public sealed class GeneralResolverTests
{
  /// <summary>
  /// Tests that the world seed is taken from <see cref="PropData.WorldSeed"/>. Resolving it from
  /// the text seed or the clock is <c>ConfigGenerator</c>'s job (see <c>ConfigGeneratorTests</c>).
  /// </summary>
  public sealed class SeedTests
  {
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mordor")]
    public void Execute_CopiesWorldSeedFromPropData(string? textSeed)
    {
      // Arrange: Supporting data carrying a known world seed; the text seed must not matter.
      var propData = PropData.Default with { WorldSeed = 123456789L };
      var resolver = new GeneralResolver(new PropMain { TextSeed = textSeed }, propData);

      // Act: Resolve general configuration.
      var config = resolver.Execute();

      // Assert: The world seed is copied unchanged, without reading the text seed or the clock.
      Assert.Equal(123456789L, config.Seed);
    }

    [Fact]
    public void Execute_WithSamePropData_IsDeterministic()
    {
      // Arrange: Clock-less inputs (no text seed, fixed world seed).
      var propData = PropData.Default with { WorldSeed = 7L };

      // Act: Resolve twice from the same inputs.
      var first = new GeneralResolver(PropMain.Default, propData).Execute();
      var second = new GeneralResolver(PropMain.Default, propData).Execute();

      // Assert: Identical output; the clock cannot leak in.
      Assert.Equal(first, second);
    }
  }

  /// <summary>Tests degeneration level resolution from the resolver sub-seed stream.</summary>
  public sealed class DegenerationTests
  {
    /// <summary>Creates a resolver over a property dataset with the given internal seed and difficulty.</summary>
    private static GeneralResolver CreateResolver(long propSeed, int difficulty)
    {
      return new GeneralResolver(new PropMain { TextSeed = "mordor", Difficulty = difficulty }, PropData.Default with { Seed = propSeed });
    }

    [Fact]
    public void Execute_WithFixedPropSeed_IsDeterministic()
    {
      // Act: Resolve twice from the same internal seed; the clock must not leak in.
      var first = CreateResolver(42, 0).Execute();
      var second = CreateResolver(42, 0).Execute();

      // Assert: The degeneration level is identical draw for draw.
      Assert.Equal(first.Degeneration, second.Degeneration);
    }

    [Fact]
    public void Execute_WithFixedPropSeed_MatchesPinnedValue()
    {
      // Act: Resolve degeneration from a known internal seed at difficulty 0.
      var config = CreateResolver(42, 0).Execute();

      // Assert: The value matches the pinned reference, so the resolver salt cannot
      // silently change without breaking reproducibility of generated configs.
      Assert.Equal(3.10072184f, config.Degeneration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Execute_WithDifficulty_StaysInsideDegenerationRange(int difficulty)
    {
      // Act: Resolve degeneration for a difficulty step.
      var config = CreateResolver(7, difficulty).Execute();

      // Assert: The value stays inside the difficulty step's range, with the lower
      // bound clamped at 0.0 and the value finite (so CnfGeneral.Validate passes).
      var baseValue = difficulty * 10.0f;
      var from = Math.Max(0.0f, baseValue - 20.0f);
      var to = baseValue + 20.0f;

      Assert.True(float.IsFinite(config.Degeneration), "Degeneration must be finite.");
      Assert.True(
        config.Degeneration >= from && config.Degeneration < to,
        $"Degeneration {config.Degeneration} is outside [{from}, {to}).");
    }
  }
}
