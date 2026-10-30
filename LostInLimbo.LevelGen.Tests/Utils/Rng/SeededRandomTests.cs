using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Tests.Utils.Rng;

/// <summary>
/// Tests <see cref="SeededRandom"/>: reproducibility, range compliance, unbiased bounds
/// behavior and deterministic shuffling.
/// </summary>
public sealed class SeededRandomTests
{
  /// <summary>Sub-suite covering the raw 64-bit stream.</summary>
  public sealed class NextULongTests
  {
    [Fact]
    public void NextULong_WithSameSeed_ProducesIdenticalSequence()
    {
      // Arrange: Two generators seeded identically.
      var first = new SeededRandom(42);
      var second = new SeededRandom(42);

      // Act: Draw a short sequence from both.
      var firstSequence = Enumerable.Range(0, 20).Select(_ => first.NextULong()).ToArray();
      var secondSequence = Enumerable.Range(0, 20).Select(_ => second.NextULong()).ToArray();

      // Assert: The sequences match draw for draw.
      Assert.Equal(firstSequence, secondSequence);
    }

    [Fact]
    public void NextULong_WithDifferentSeeds_ProducesDifferentSequences()
    {
      // Arrange: Two generators seeded differently.
      var first = new SeededRandom(1);
      var second = new SeededRandom(2);

      // Act: Draw one value from both.
      var firstValue = first.NextULong();
      var secondValue = second.NextULong();

      // Assert: Different seeds diverge immediately.
      Assert.NotEqual(firstValue, secondValue);
    }

    [Fact]
    public void NextULong_WithSeed42_MatchesPinnedSequence()
    {
      // Arrange: Generator with a known seed.
      var random = new SeededRandom(42);

      // Act: Draw five raw values.
      var draws = Enumerable.Range(0, 5).Select(_ => random.NextULong()).ToArray();

      // Assert: The sequence matches the pinned reference, so the algorithm cannot
      // silently change without breaking reproducibility of generated levels.
      ulong[] expected =
      [
        0xBDD732262FEB6E95UL,
        0x28EFE333B266F103UL,
        0x47526757130F9F52UL,
        0x581CE1FF0E4AE394UL,
        0x09BC585A244823F2UL,
      ];
      Assert.Equal(expected, draws);
    }
  }

  /// <summary>Sub-suite covering bounded integer draws.</summary>
  public sealed class NextIntTests
  {
    [Fact]
    public void NextInt_WithSameSeed_ProducesIdenticalSequence()
    {
      // Arrange: Two generators seeded identically.
      var first = new SeededRandom(7);
      var second = new SeededRandom(7);

      // Act: Draw a short sequence from both.
      var firstSequence = Enumerable.Range(0, 50).Select(_ => first.NextInt(1000)).ToArray();
      var secondSequence = Enumerable.Range(0, 50).Select(_ => second.NextInt(1000)).ToArray();

      // Assert: The sequences match draw for draw.
      Assert.Equal(firstSequence, secondSequence);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NextInt_WithNonPositiveBound_ThrowsArgumentOutOfRangeException(int maxExclusive)
    {
      // Arrange: Generator with a valid seed.
      var random = new SeededRandom(1);

      // Act/Assert: A zero or negative upper bound is rejected naming the parameter.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(maxExclusive));
      Assert.Equal("maxExclusive", exception.ParamName);
    }

    [Fact]
    public void NextInt_WithAnyBound_StaysWithinRange()
    {
      // Arrange: Generator and observed value set.
      var random = new SeededRandom(1234);

      // Act: Draw many values over a small range.
      var draws = Enumerable.Range(0, 500).Select(_ => random.NextInt(6)).ToArray();

      // Assert: Every value is inside the range and every slot gets hit at least once.
      Assert.All(draws, value => Assert.InRange(value, 0, 5));
      Assert.Equal(Enumerable.Range(0, 6), draws.Distinct().OrderBy(value => value));
    }

    [Fact]
    public void NextInt_WithRange_ReturnsValuesInsideRange()
    {
      // Arrange: Generator with a closed lower bound.
      var random = new SeededRandom(99);

      // Act: Draw many values over [10, 20).
      var draws = Enumerable.Range(0, 500).Select(_ => random.NextInt(10, 20)).ToArray();

      // Assert: Every value is inside the inclusive-exclusive range.
      Assert.All(draws, value => Assert.InRange(value, 10, 19));
    }

    [Fact]
    public void NextInt_WithEqualBounds_ReturnsThatBound()
    {
      // Arrange: Generator and a degenerate range.
      var random = new SeededRandom(5);

      // Act/Assert: An empty range yields the bound without drawing.
      Assert.Equal(7, random.NextInt(7, 7));
    }

    [Fact]
    public void NextInt_WithInvertedBounds_ThrowsArgumentOutOfRangeException()
    {
      // Arrange: Generator with an upper bound below the lower bound.
      var random = new SeededRandom(5);

      // Act/Assert: The inverted range is rejected naming the parameter.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(10, 5));
      Assert.Equal("maxExclusive", exception.ParamName);
    }

    [Fact]
    public void NextInt_WithSeed42_MatchesPinnedSequence()
    {
      // Arrange: Generator with a known seed.
      var random = new SeededRandom(42);

      // Act: Draw ten bounded values.
      var draws = Enumerable.Range(0, 10).Select(_ => random.NextInt(100)).ToArray();

      // Assert: The sequence matches the pinned reference (reproducibility guard).
      int[] expected = [21, 19, 30, 48, 70, 38, 81, 12, 85, 66];
      Assert.Equal(expected, draws);
    }
  }

  /// <summary>Sub-suite covering bounded float draws.</summary>
  public sealed class NextFloatTests
  {
    [Fact]
    public void NextFloat_WithSameSeed_ProducesIdenticalSequence()
    {
      // Arrange: Two generators seeded identically.
      var first = new SeededRandom(7);
      var second = new SeededRandom(7);

      // Act: Draw short sequences from both overloads.
      var firstUnit = Enumerable.Range(0, 50).Select(_ => first.NextFloat(1f)).ToArray();
      var secondUnit = Enumerable.Range(0, 50).Select(_ => second.NextFloat(1f)).ToArray();
      var firstRange = Enumerable.Range(0, 50).Select(_ => first.NextFloat(-2f, 8f)).ToArray();
      var secondRange = Enumerable.Range(0, 50).Select(_ => second.NextFloat(-2f, 8f)).ToArray();

      // Assert: The sequences match draw for draw.
      Assert.Equal(firstUnit, secondUnit);
      Assert.Equal(firstRange, secondRange);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void NextFloat_WithNonPositiveBound_ThrowsArgumentOutOfRangeException(float maxExclusive)
    {
      // Arrange: Generator with a valid seed.
      var random = new SeededRandom(1);

      // Act/Assert: A zero or negative upper bound is rejected naming the parameter.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => random.NextFloat(maxExclusive));
      Assert.Equal("maxExclusive", exception.ParamName);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void NextFloat_WithNonFiniteBound_ThrowsArgumentOutOfRangeException(float maxExclusive)
    {
      // Arrange: Generator with a valid seed.
      var random = new SeededRandom(1);

      // Act/Assert: A NaN or infinite upper bound is rejected naming the parameter.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => random.NextFloat(maxExclusive));
      Assert.Equal("maxExclusive", exception.ParamName);
    }

    [Theory]
    [InlineData(float.NaN, 1f)]
    [InlineData(float.NegativeInfinity, 1f)]
    public void NextFloat_WithNonFiniteLowerBound_ThrowsArgumentOutOfRangeException(float minInclusive, float maxExclusive)
    {
      // Arrange: Generator with a valid seed.
      var random = new SeededRandom(1);

      // Act/Assert: A NaN or infinite lower bound is rejected naming the parameter.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => random.NextFloat(minInclusive, maxExclusive));
      Assert.Equal("minInclusive", exception.ParamName);
    }

    [Theory]
    [InlineData(0f, float.NaN)]
    [InlineData(0f, float.PositiveInfinity)]
    public void NextFloat_WithNonFiniteUpperBound_ThrowsArgumentOutOfRangeException(float minInclusive, float maxExclusive)
    {
      // Arrange: Generator with a valid seed.
      var random = new SeededRandom(1);

      // Act/Assert: A NaN or infinite upper bound is rejected naming the parameter.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => random.NextFloat(minInclusive, maxExclusive));
      Assert.Equal("maxExclusive", exception.ParamName);
    }

    [Fact]
    public void NextFloat_WithUnitBound_StaysWithinRange()
    {
      // Arrange: Generator with a valid seed.
      var random = new SeededRandom(1234);

      // Act: Draw many values over [0, 1).
      var draws = Enumerable.Range(0, 500).Select(_ => random.NextFloat(1f)).ToArray();

      // Assert: Every value is inside the half-open range and the draws are not constant.
      Assert.All(draws, value => Assert.True(value >= 0f && value < 1f, $"Value {value} is outside [0, 1)."));
      Assert.True(draws.Distinct().Count() > 1, "Expected more than one distinct value.");
    }

    [Fact]
    public void NextFloat_WithRange_ReturnsValuesInsideRange()
    {
      // Arrange: Generator with a closed lower bound.
      var random = new SeededRandom(99);

      // Act: Draw many values over [10, 20).
      var draws = Enumerable.Range(0, 500).Select(_ => random.NextFloat(10f, 20f)).ToArray();

      // Assert: Every value is inside the inclusive-exclusive range.
      Assert.All(draws, value => Assert.True(value >= 10f && value < 20f, $"Value {value} is outside [10, 20)."));
    }

    [Fact]
    public void NextFloat_WithNegativeRange_ReturnsValuesInsideRange()
    {
      // Arrange: Generator with a fully negative range.
      var random = new SeededRandom(42);

      // Act: Draw many values over [-5, -1).
      var draws = Enumerable.Range(0, 500).Select(_ => random.NextFloat(-5f, -1f)).ToArray();

      // Assert: Every value is inside the inclusive-exclusive range.
      Assert.All(draws, value => Assert.True(value >= -5f && value < -1f, $"Value {value} is outside [-5, -1)."));
    }

    [Fact]
    public void NextFloat_WithEqualBounds_ReturnsThatBound()
    {
      // Arrange: Generator and a degenerate range.
      var random = new SeededRandom(5);

      // Act/Assert: An empty range yields the bound without drawing.
      Assert.Equal(7.5f, random.NextFloat(7.5f, 7.5f));
    }

    [Fact]
    public void NextFloat_WithInvertedBounds_ThrowsArgumentOutOfRangeException()
    {
      // Arrange: Generator with an upper bound below the lower bound.
      var random = new SeededRandom(5);

      // Act/Assert: The inverted range is rejected naming the parameter.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => random.NextFloat(10f, 5f));
      Assert.Equal("maxExclusive", exception.ParamName);
    }

    [Fact]
    public void NextFloat_WithTinyRange_NeverReachesUpperBound()
    {
      // Arrange: A range one float step wide, where naive mapping rounds up to the bound.
      var random = new SeededRandom(3);
      const float minInclusive = 1.0f;
      const float maxExclusive = 1.0000001f;
      Assert.True(minInclusive < maxExclusive, "Range must span at least one float step.");

      // Act: Draw many values over the tiny range.
      var draws = Enumerable.Range(0, 1000).Select(_ => random.NextFloat(minInclusive, maxExclusive)).ToArray();

      // Assert: Rounding never lets a draw reach the exclusive upper bound.
      Assert.All(draws, value => Assert.True(value >= minInclusive && value < maxExclusive, $"Value {value} reached the upper bound."));
    }

    [Fact]
    public void NextFloat_WithSeed42_MatchesPinnedSequence()
    {
      // Arrange: Generator with a known seed.
      var random = new SeededRandom(42);

      // Act: Draw ten bounded values.
      var draws = Enumerable.Range(0, 10).Select(_ => random.NextFloat(10f)).ToArray();

      // Assert: The sequence matches the pinned reference (reproducibility guard).
      float[] expected = [7.4156485f, 1.5991038f, 2.7860112f, 3.4419065f, 0.38030148f, 8.682281f, 2.184052f, 8.006318f, 3.39931f, 6.1848207f];
      Assert.Equal(expected, draws);
    }

    [Fact]
    public void NextFloat_OverUnitRange_HasRoughlyHalfMean()
    {
      // Arrange: Generator with a valid seed.
      var random = new SeededRandom(1234);

      // Act: Draw ten thousand values over [0, 1).
      var draws = Enumerable.Range(0, 10_000).Select(_ => random.NextFloat(1f)).ToArray();

      // Assert: The mean stays near 0.5, so the distribution is not skewed to an edge.
      var mean = draws.Average();
      Assert.InRange(mean, 0.45, 0.55);
    }
  }

  /// <summary>Sub-suite covering probabilistic boolean draws.</summary>
  public sealed class NextBoolTests
  {
    [Fact]
    public void NextBool_WithSeed_AlwaysTrue()
    {
      // Arrange: Generator with a known seed.
      var random = new SeededRandom(42);

      // Act: Draw ten bounded values. True has a 100% chance.
      var draws = Enumerable.Range(0, 10).Select(_ => random.NextBool(1.0f)).ToArray();

      // Assert: All draws are true.
      CountBools(draws, 10);
    }

    [Fact]
    public void NextBool_WithSeed_AlwaysFalse()
    {
      // Arrange: Generator with a known seed.
      var random = new SeededRandom(42);

      // Act: Draw ten bounded values. True has a 0% chance.
      var draws = Enumerable.Range(0, 10).Select(_ => random.NextBool(0.0f)).ToArray();

      // Assert: All draws are false.
      CountBools(draws, 0);
    }

    [Fact]
    public void NextBool_WithSeed_RoughlyHalfTrue()
    {
      // Arrange: Generator with a seed that rolls exactly 5 true and 5 false at even odds.
      var random = new SeededRandom(2);

      // Act: Draw ten bounded values. True has a 50% chance.
      var draws = Enumerable.Range(0, 10).Select(_ => random.NextBool(0.5f)).ToArray();

      // Assert: Roughly half of the results are true.
      CountBools(draws, 5);
    }

    [Fact]
    public void NextBool_WithSameSeed_ProducesIdenticalSequence()
    {
      // Arrange: Two generators seeded identically.
      var first = new SeededRandom(42);
      var second = new SeededRandom(42);

      // Act: Draw a short sequence from both.
      var firstSequence = Enumerable.Range(0, 50).Select(_ => first.NextBool(0.3f)).ToArray();
      var secondSequence = Enumerable.Range(0, 50).Select(_ => second.NextBool(0.3f)).ToArray();

      // Assert: The sequences match draw for draw.
      Assert.Equal(firstSequence, secondSequence);
    }

    [Theory]
    [InlineData(-1.0f)]
    [InlineData(-5.0f)]
    public void NextBool_WithChanceBelowZero_AlwaysFalse(float chance)
    {
      // Arrange: Generator with a valid seed.
      var random = new SeededRandom(42);

      // Act: Draw ten values with a chance below the valid range.
      var draws = Enumerable.Range(0, 10).Select(_ => random.NextBool(chance)).ToArray();

      // Assert: Out-of-range chance is clamped to zero, so every draw is false.
      CountBools(draws, 0);
    }

    [Theory]
    [InlineData(1.5f)]
    [InlineData(5.0f)]
    public void NextBool_WithChanceAboveOne_AlwaysTrue(float chance)
    {
      // Arrange: Generator with a valid seed.
      var random = new SeededRandom(42);

      // Act: Draw ten values with a chance above the valid range.
      var draws = Enumerable.Range(0, 10).Select(_ => random.NextBool(chance)).ToArray();

      // Assert: Out-of-range chance is clamped to one, so every draw is true.
      CountBools(draws, 10);
    }

    [Fact]
    public void NextBool_WithNaNChance_ThrowsArgumentOutOfRangeException()
    {
      // Arrange: Generator with a valid seed.
      var random = new SeededRandom(1);

      // Act/Assert: A NaN chance is rejected naming the parameter.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => random.NextBool(float.NaN));
      Assert.Equal("chance", exception.ParamName);
    }

    [Fact]
    public void NextBool_WithLowChance_ObservesExpectedRate()
    {
      // Arrange: Generator with a valid seed.
      var random = new SeededRandom(1234);

      // Act: Draw ten thousand values with a 25% chance of true.
      var draws = Enumerable.Range(0, 10_000).Select(_ => random.NextBool(0.25f)).ToArray();

      // Assert: The true count stays within a broad band around 2 500, so neither
      // outcome can dominate (the placeholder bug always returned true).
      var trueCount = draws.Count(value => value);
      Assert.InRange(trueCount, 2_000, 3_000);
    }

    [Fact]
    public void NextBool_WithSeed42_MatchesPinnedSequence()
    {
      // Arrange: Generator with a known seed.
      var random = new SeededRandom(42);

      // Act: Draw ten boolean values at even odds.
      var draws = Enumerable.Range(0, 10).Select(_ => random.NextBool(0.5f)).ToArray();

      // Assert: The sequence matches the pinned reference (reproducibility guard).
      bool[] expected = [true, false, true, true, true, true, true, false, true, true];
      Assert.Equal(expected, draws);
    }

    /// <summary>
    /// Counts true values.
    /// </summary>
    /// <param name="result">Array of boolean values.</param>
    /// <param name="expectedTrueCount">How many true values are expected.</param>
    private void CountBools(bool[] result, int expectedTrueCount)
    {
      int actualTrueCount = 0;
      for (int i=0; i<result.Length; i++)
      {
        if (result[i] == true) actualTrueCount++;
      }
      Assert.Equal(expectedTrueCount, actualTrueCount);
    }
  }

  /// <summary>Sub-suite covering in-place shuffling.</summary>
  public sealed class ShuffleTests
  {
    [Fact]
    public void Shuffle_WithSameSeed_ProducesIdenticalOrder()
    {
      // Arrange: Two identical lists and two identically seeded generators.
      var first = Enumerable.Range(0, 50).ToList();
      var second = Enumerable.Range(0, 50).ToList();
      var firstRandom = new SeededRandom(3);
      var secondRandom = new SeededRandom(3);

      // Act: Shuffle both lists.
      firstRandom.Shuffle(first);
      secondRandom.Shuffle(second);

      // Assert: Both lists end up in the same order.
      Assert.Equal(first, second);
    }

    [Fact]
    public void Shuffle_WithValidSeed_PreservesElements()
    {
      // Arrange: List and generator.
      var list = Enumerable.Range(0, 40).ToList();
      var random = new SeededRandom(11);

      // Act: Shuffle the list.
      random.Shuffle(list);

      // Assert: The list still contains exactly the original elements.
      Assert.Equal(Enumerable.Range(0, 40), list.OrderBy(value => value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Shuffle_WithTinyList_DoesNotThrow(int count)
    {
      // Arrange: List too short to contain a swap.
      var list = Enumerable.Range(0, count).ToList();
      var random = new SeededRandom(2);

      // Act/Assert: Shuffling zero or one element is a no-op.
      random.Shuffle(list);
      Assert.Equal(count, list.Count);
    }

    [Fact]
    public void Shuffle_WithNullList_ThrowsArgumentNullException()
    {
      // Arrange: Generator without a list.
      var random = new SeededRandom(1);

      // Act/Assert: A null list is rejected naming the parameter.
      var exception = Assert.Throws<ArgumentNullException>(() => random.Shuffle<int>(null!));
      Assert.Equal("list", exception.ParamName);
    }
  }
}
