using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Data;
using LostInLimbo.LevelGen.Internal.Prop.Resolvers;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Tests.Prop;

/// <summary>
/// Tests general configuration resolution, grouped into suites by concern:
/// seed resolution from a text seed and from the fallback clock.
/// </summary>
public sealed class GeneralResolverTests
{
  /// <summary>Creates a resolver over a property dataset carrying the given text seed.</summary>
  private static GeneralResolver CreateResolver(string? textSeed)
  {
    return new GeneralResolver(new PropMain { TextSeed = textSeed }, PropData.Default);
  }

  /// <summary>Tests text seeds and no-text-seed fallback.</summary>
  public sealed class SeedTests
  {
    [Fact]
    public void Execute_WithTextSeed_HashesIntoSeed()
    {
      // Act: Resolve general configuration with a known text seed.
      var config = CreateResolver("mordor").Execute();

      // Assert: The seed is the FromText hash, pinned here so text seeds keep
      // reproducing previously generated levels.
      Assert.Equal(SeedDerivation.FromText("mordor"), config.Seed);
      Assert.Equal(-8341167147440548823L, config.Seed);
    }

    [Fact]
    public void Execute_WithTextSeedDifferingByCase_ProducesDifferentSeeds()
    {
      // Act: Resolve configurations whose text seeds differ only in case.
      var lower = CreateResolver("mordor").Execute();
      var upper = CreateResolver("Mordor").Execute();

      // Assert: Text seeding is case-sensitive.
      Assert.NotEqual(lower.Seed, upper.Seed);
    }

    [Fact]
    public void Execute_WithSameTextSeed_IsDeterministic()
    {
      // Act: Resolve twice from the same text seed; the clock must not leak in.
      var first = CreateResolver("mordor").Execute();
      var second = CreateResolver("mordor").Execute();

      // Assert: Text-seeded runs ignore the clock and stay identical.
      Assert.Equal(first.Seed, second.Seed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Execute_WithoutTextSeed_FallsBackToTime(string? textSeed)
    {
      // Act: Capture the clock window around resolution; null, empty and
      // whitespace-only text seeds all count as "no text seed given".
      var before = DateTime.UtcNow.Ticks;
      var seed = CreateResolver(textSeed).Execute().Seed;
      var after = DateTime.UtcNow.Ticks;

      // Assert: The seed is a timestamp taken between the two clock reads.
      Assert.InRange(seed, before, after);
    }
  }

  /// <summary>Tests degeneration level resolution from the resolver sub-seed stream.</summary>
  public sealed class DegenerationTests
  {
    /// <summary>Creates a resolver over a property dataset with the given internal seed and difficulty.</summary>
    private static GeneralResolver CreateResolver(long propSeed, int difficulty)
    {
      return new GeneralResolver(new PropMain { TextSeed = "mordor", Difficulty = difficulty }, new PropData { Seed = propSeed });
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
