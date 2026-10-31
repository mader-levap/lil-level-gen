using LostInLimbo.LevelGen.Internal.Utils.Eval;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Tests.Utils.Eval;

/// <summary>
/// Tests <see cref="EvalCurves"/>: reproducibility, range compliance, and boundary behavior.
/// </summary>
public sealed class EvalCurvesTests
{
  /// <summary>Tests of Gaussian curve.</summary>
  public sealed class GaussianTests
  {
    [Fact]
    public void Gaussian_WithSameSeed_ReturnsSameValue()
    {
      // Act: Evaluate the same range twice with identically seeded generators.
      var first = EvalCurves.Gaussian(new SeededRandom(42), 0, 100);
      var second = EvalCurves.Gaussian(new SeededRandom(42), 0, 100);

      // Assert: Same seed and inputs produce identical results.
      Assert.Equal(first, second);
    }

    [Fact]
    public void Gaussian_WithDifferentSeeds_ProducesDifferentValues()
    {
      // Act: Evaluate the same range with differently seeded generators.
      var first = EvalCurves.Gaussian(new SeededRandom(42), 0, 100);
      var second = EvalCurves.Gaussian(new SeededRandom(43), 0, 100);

      // Assert: Different seeds produce different results.
      Assert.NotEqual(first, second);
    }

    [Fact]
    public void Gaussian_TwoCallsInRow_ProducesDifferentValues()
    {
      // Act: Evaluate the same range with same generator.
      var random = new SeededRandom(42);
      var first = EvalCurves.Gaussian(random, 0, 100);
      var second = EvalCurves.Gaussian(random, 0, 100);

      // Assert: Calling same SeededRandom twice produces different results.
      Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-50, 50)]
    [InlineData(5, 5.5f)]
    [InlineData(0, 1)]
    [InlineData(-100, -50)]
    public void Gaussian_WithValidRange_ReturnsValueInsideRange(float from, float to)
    {
      // Act: Evaluate a gaussian curve over the range with a fixed seed.
      var value = EvalCurves.Gaussian(new SeededRandom(7), from, to);

      // Assert: The result is always clamped inside the inclusive bounds.
      Assert.InRange(value, from, to);
    }

    [Fact]
    public void Gaussian_WithEqualBounds_ReturnsThatValue()
    {
      // Act: Evaluate a degenerate range where from equals to.
      var value = EvalCurves.Gaussian(new SeededRandom(7), 7, 7);

      // Assert: The only representable value is the bound itself.
      Assert.Equal(7, value);
    }

    [Fact]
    public void Gaussian_WithManyDraws_FollowsBellCurve()
    {
      // Arrange: Fixed seed keeps the test deterministic; range 0..100 gives mean=50, sigma=25.
      var random = new SeededRandom(42);
      const int SampleCount = 1000;
      var samples = new float[SampleCount];
      int edgeCount = 0;
      int withinOneSigma = 0;

      // Act: Draw samples and tally distribution statistics.
      for (int i = 0; i < SampleCount; i++)
      {
        var value = EvalCurves.Gaussian(random, 0f, 100f);
        samples[i] = value;
        if (value == 0f || value == 100f) edgeCount++;
        if (value >= 25f && value <= 75f) withinOneSigma++;
      }

      var mean = samples.Average();
      var variance = samples.Average(value => (value - mean) * (value - mean));
      var stdDev = Math.Sqrt(variance);

      // Assert: Centered on the range midpoint (rules out skew).
      Assert.InRange(mean, 47.5, 52.5);

      // Assert: Spread matches sigma (clamped normal ≈ 0.96·σ = 24; rules out uniform ≈ 28.9).
      Assert.InRange(stdDev, 21.0, 27.0);

      // Assert: ~68% of values fall within one sigma of the mean (normal-distribution signature).
      var withinOneSigmaShare = (double)withinOneSigma / SampleCount;
      Assert.InRange(withinOneSigmaShare, 0.63, 0.73);

      // Assert: Tails are clamped to the bounds, confirming edge pile-up of a clamped Gaussian.
      Assert.True(edgeCount > 0, "No values clamped to the range edges.");
    }

    // Exceptions.

    [Fact]
    public void Gaussian_WithFromGreaterThanTo_ThrowsArgumentException()
    {
      // Act/Assert: An inverted range is rejected and names the offending parameter.
      var exception = Assert.Throws<ArgumentException>(
        () => EvalCurves.Gaussian(new SeededRandom(7), 10, 0));
      Assert.Equal("from", exception.ParamName);
    }
  }
}
