using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Tests.Utils.Rng;

/// <summary>
/// Tests deterministic sub-seed derivation, grouped into suites by concern:
/// the Mix primitive, two-argument Derive, nested three-argument Derive and
/// text-to-seed hashing.
/// </summary>
public sealed class SeedDerivationTests
{
  /// <summary>Tests <see cref="SeedDerivation.Mix"/>: the SplitMix64 mixing primitive.</summary>
  public sealed class MixTests
  {
    [Fact]
    public void Mix_WithZero_MatchesCanonicalSplitMix64Reference()
    {
      // Arrange: SplitMix64(0) is a well-known reference value from the original paper.
      const long expected = unchecked((long)0xE220A8397B1DCDAFUL);

      // Act: Mix the zero input.
      var mixed = SeedDerivation.Mix(0);

      // Assert: Output matches the published SplitMix64 result.
      Assert.Equal(expected, mixed);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    [InlineData(42L)]
    public void Mix_WithSameInput_IsDeterministic(long value)
    {
      // Act: Mix the same input twice.
      var first = SeedDerivation.Mix(value);
      var second = SeedDerivation.Mix(value);

      // Assert: Both calls produce the identical output.
      Assert.Equal(first, second);
    }

    [Fact]
    public void Mix_WithSingleBitInputChange_AvalanchesOutput()
    {
      // Arrange: 64 one-bit variants of a base input.
      const long baseline = 0x123456789ABCDEF0L;
      var flippedBits = 0;

      // Act: Count differing output bits for every single-bit input change.
      var reference = unchecked((ulong)SeedDerivation.Mix(baseline));
      for (var bit = 0; bit < 64; bit++)
      {
        var variant = baseline ^ (1L << bit);
        var mixed = unchecked((ulong)SeedDerivation.Mix(variant));
        flippedBits += System.Numerics.BitOperations.PopCount(reference ^ mixed);
      }

      // Assert: Average avalanche is ~32 bits; anything below 16 indicates a broken mix.
      var averageFlippedBits = flippedBits / 64.0;
      Assert.True(
        averageFlippedBits >= 16.0,
        $"Average avalanche was {averageFlippedBits} bits, expected at least 16.");
    }
  }

  /// <summary>Tests <see cref="SeedDerivation.Derive(long, long)"/>: per-channel sub-seeds.</summary>
  public sealed class DeriveTests
  {
    [Fact]
    public void Derive_WithKnownInputs_MatchesPinnedValues()
    {
      // Act: Derive sub-seeds for the Graph and Map channels from world seeds 0 and 42.
      var seedGraph0 = SeedDerivation.Derive(0, SeedChannels.Graph);
      var seedMap0 = SeedDerivation.Derive(0, SeedChannels.Map);
      var seedGraph42 = SeedDerivation.Derive(42, SeedChannels.Graph);
      var seedMap42 = SeedDerivation.Derive(42, SeedChannels.Map);

      // Assert: Outputs match values pinned at implementation time; any change breaks
      // reproducibility of previously generated levels and must be a conscious decision.
      Assert.Equal(-6796510041975876143L, seedGraph0);
      Assert.Equal(5628362823023774856L, seedMap0);
      Assert.Equal(1950775377620671602L, seedGraph42);
      Assert.Equal(587974172545099585L, seedMap42);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(-7L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void Derive_WithSameInputs_IsDeterministic(long worldSeed)
    {
      // Act: Derive the graph sub-seed twice from the same world seed.
      var first = SeedDerivation.Derive(worldSeed, SeedChannels.Graph);
      var second = SeedDerivation.Derive(worldSeed, SeedChannels.Graph);

      // Assert: Both calls produce the identical output.
      Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(42L)]
    [InlineData(long.MinValue)]
    public void Derive_WithDifferentWorldSeeds_ProducesDifferentSubSeeds(long worldSeed)
    {
      // Act: Derive a sub-seed for the seed itself and for a one-bit-different seed.
      var original = SeedDerivation.Derive(worldSeed, SeedChannels.Graph);
      var neighbor = SeedDerivation.Derive(worldSeed ^ 1, SeedChannels.Graph);

      // Assert: Nearby world seeds do not share sub-seeds.
      Assert.NotEqual(original, neighbor);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(42L)]
    [InlineData(long.MinValue)]
    public void Derive_WithDifferentChannels_ProducesDifferentSubSeeds(long worldSeed)
    {
      // Act: Derive sub-seeds for the Graph and Map channels of the same world seed.
      var seedGraph = SeedDerivation.Derive(worldSeed, SeedChannels.Graph);
      var seedMap = SeedDerivation.Derive(worldSeed, SeedChannels.Map);

      // Assert: The two channels never collapse onto the same sub-seed.
      Assert.NotEqual(seedGraph, seedMap);
    }
  }

  /// <summary>
  /// Tests <see cref="SeedDerivation.Derive(long, long, long)"/>: nested sub-seeds, for
  /// example per-chunk seeds under a channel.
  /// </summary>
  public sealed class NestedDeriveTests
  {
    [Fact]
    public void DeriveNested_WithKnownInputs_MatchesPinnedValue()
    {
      // Act: Derive a nested sub-seed under the Graph channel.
      var nested = SeedDerivation.Derive(0, SeedChannels.Graph, 7);

      // Assert: Output matches the value pinned at implementation time.
      Assert.Equal(5144840600214266125L, nested);
    }

    [Fact]
    public void DeriveNested_IsEquivalentToChainedTwoArgumentDerive()
    {
      // Act: Derive the nested seed both ways.
      var inlined = SeedDerivation.Derive(42, SeedChannels.Map, 13);
      var chained = SeedDerivation.Derive(SeedDerivation.Derive(42, SeedChannels.Map), 13);

      // Assert: The three-argument overload matches explicit chaining.
      Assert.Equal(chained, inlined);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-1L)]
    public void DeriveNested_WithDifferentSubChannels_ProducesDifferentSubSeeds(long subChannel)
    {
      // Act: Derive two nested sub-seeds that differ only in the sub-channel.
      var first = SeedDerivation.Derive(0, SeedChannels.Graph, subChannel);
      var second = SeedDerivation.Derive(0, SeedChannels.Graph, subChannel + 1);

      // Assert: Sub-channels address independent streams.
      Assert.NotEqual(first, second);
    }
  }

  /// <summary>Tests <see cref="SeedDerivation.FromText"/>: text-to-seed hashing.</summary>
  public sealed class FromTextTests
  {
    [Fact]
    public void FromText_WithKnownText_MatchesPinnedValues()
    {
      // Act: Hash texts chosen at implementation time: an ASCII word, a numeric string
      // and a non-ASCII string with diacritics.
      var mordor = SeedDerivation.FromText("mordor");
      var numeric = SeedDerivation.FromText("42");
      var unicode = SeedDerivation.FromText("Poznań");

      // Assert: Outputs match values pinned at implementation time; any change breaks
      // reproducibility of previously generated levels and must be a conscious decision.
      Assert.Equal(-8341167147440548823L, mordor);
      Assert.Equal(3332507518487421172L, numeric);
      Assert.Equal(7468663056517749686L, unicode);
    }

    [Theory]
    [InlineData("mordor")]
    [InlineData("MORDOR")]
    [InlineData("")]
    [InlineData("a much longer seed text with spaces and punctuation!")]
    [InlineData("Poznań 日本語")]
    [InlineData("ǅungla")]
    public void FromText_WithSameText_IsDeterministic(string text)
    {
      // Arrange: Empty and whitespace inputs are rejected, so skip them here.
      if (string.IsNullOrWhiteSpace(text)) return;

      // Act: Hash the same text twice.
      var first = SeedDerivation.FromText(text);
      var second = SeedDerivation.FromText(text);

      // Assert: Both calls produce the identical output.
      Assert.Equal(first, second);
    }

    [Fact]
    public void FromText_DifferingOnlyByCase_ProducesDifferentSeeds()
    {
      // Act: Hash a word in two casings.
      var lower = SeedDerivation.FromText("mordor");
      var upper = SeedDerivation.FromText("Mordor");

      // Assert: Text hashing is case-sensitive.
      Assert.NotEqual(lower, upper);
    }

    [Fact]
    public void FromText_WithSimilarTexts_ProducesDistinctSeeds()
    {
      // Arrange: Words differing by a single character.
      string[] texts = ["mordor", "morbor", "mordol", "lordor", "mordoo"];

      // Act: Hash every word.
      var seeds = texts.Select(SeedDerivation.FromText).ToArray();

      // Assert: No two words collapse onto the same seed.
      Assert.Equal(seeds.Length, seeds.Distinct().Count());
    }

    [Fact]
    public void FromText_WithNull_ThrowsArgumentNullException()
    {
      // Act/Assert: Null text is rejected.
      Assert.Throws<ArgumentNullException>(() => SeedDerivation.FromText(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void FromText_WithEmptyOrWhitespace_ThrowsArgumentException(string text)
    {
      // Act/Assert: Empty and whitespace-only text is rejected.
      Assert.Throws<ArgumentException>(() => SeedDerivation.FromText(text));
    }

    [Fact]
    public void FromText_WithComposedAndDecomposedUnicode_ProducesSameSeed()
    {
      // Arrange: "Poznań" with a composed n-acute (U+0144) versus decomposed
      // "n" + combining acute (U+0301); visually identical text from different inputs.
      const string composed = "Pozna\u0144";
      const string decomposed = "Poznan\u0301";

      // Act: Hash both spellings.
      var composedSeed = SeedDerivation.FromText(composed);
      var decomposedSeed = SeedDerivation.FromText(decomposed);

      // Assert: Unicode normalization makes both spellings hash identically.
      Assert.Equal(composedSeed, decomposedSeed);
    }
  }
}
