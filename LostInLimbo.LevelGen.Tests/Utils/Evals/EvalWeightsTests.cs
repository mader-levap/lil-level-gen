using LostInLimbo.LevelGen.Internal.Utils.Evals;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Tests.Utils.Evals;

/// <summary>
/// Tests <see cref="EvalWeights"/>: reproducibility, range compliance, and boundary behavior.
/// </summary>
public sealed class EvalWeightsTests
{
  /// <summary>Tests of weighted picks over int values.</summary>
  public sealed class IntTests
  {
    [Fact]
    public void Pick_Int_WithSameSeed_ReturnsSameValue()
    {
      // Act: Evaluate the same range twice with identically seeded generators.
      var first = EvalWeights.Pick(new SeededRandom(42), [22, 33, 44], [1, 1, 1]);
      var second = EvalWeights.Pick(new SeededRandom(42), [22, 33, 44], [1, 1, 1]);

      // Assert: Same seed and inputs produce identical results.
      Assert.Equal(first, second);
    }

    [Fact]
    public void Pick_Int_WithDifferentSeeds_ProducesDifferentValues()
    {
      // Act: Evaluate the same range twice with differently seeded generators.
      var first = EvalWeights.Pick(new SeededRandom(42), [22, 33, 44], [1, 1, 1]);
      var second = EvalWeights.Pick(new SeededRandom(43), [22, 33, 44], [1, 1, 1]);

      // Assert: Different seeds produce different results.
      Assert.NotEqual(first, second);
    }

    [Fact]
    public void Pick_Int_TwoCallsInRow_ProducesDifferentValues()
    {
      // Act: Evaluate the same range with same generator.
      var random = new SeededRandom(42);
      var first = EvalWeights.Pick(random, [22, 33, 44], [1, 1, 1]);
      var second = EvalWeights.Pick(random, [22, 33, 44], [1, 1, 1]);

      // Assert: Calling same SeededRandom twice produces different results.
      Assert.NotEqual(first, second);
    }

    [Fact]
    public void Pick_Int_SingleValue_ReturnsThatValue()
    {
      // Act: Evaluate a one-element table.
      var value = EvalWeights.Pick(new SeededRandom(42), [7], [1]);

      // Assert: Only one value is available.
      Assert.Equal(7, value);
    }

    [Fact]
    public void Pick_Int_WithManyDraws_FollowsWeights()
    {
      // Arrange: Fixed seed keeps the test deterministic.
      var random = new SeededRandom(42);

      int[] values = [22, 33, 44];
      int[] weights = [5, 3, 2]; // in total 10

      const int SampleCount = 1000;
      int[] samples = new int[SampleCount];
      int[] counts = new int[values.Length];

      // Act: Draw samples and tally distribution statistics.
      for (int i = 0; i < SampleCount; i++)
      {
        int value = EvalWeights.Pick(random, values, weights);
        samples[i] = value;
        for (int j=0; j<values.Length; j++)
        {
          if (value == values[j]) {
            counts[j]++;
            break;
          }
        }
      }

      // Assert: counts roughly follow weights.
      Assert.InRange(counts[0], 450, 550); // 22 has weight 5 out of 10
      Assert.InRange(counts[1], 275, 325); // 33 has weight 3 out of 10
      Assert.InRange(counts[2], 175, 225); // 44 has weight 2 out of 10
    }

    // Exceptions.

    [Fact]
    public void Pick_Int_EmptyArray_ThrowsArgumentException()
    {
      // Act/Assert: Empty array is rejected.
      var exception = Assert.Throws<ArgumentException>(
        () => EvalWeights.Pick(new SeededRandom(7), Array.Empty<int>(), [1, 2]));
      Assert.Equal("values", exception.ParamName);
    }

    [Fact]
    public void Pick_Int_DifferentSizeArray_ThrowsArgumentException()
    {
      // Act/Assert: Arrays with different lengths are rejected.
      var exception = Assert.Throws<ArgumentException>(
        () => EvalWeights.Pick(new SeededRandom(7), [1, 2, 3], [1, 2]));
      Assert.Equal("weights", exception.ParamName);
    }
  }
  /// <summary>Tests of weighted picks over float values.</summary>
  public sealed class FloatTests
  {
    [Fact]
    public void Pick_Float_WithSameSeed_ReturnsSameValue()
    {
      // Act: Evaluate the same range twice with identically seeded generators.
      var first = EvalWeights.Pick(new SeededRandom(42), [1.0f, 1.5f, 2.0f], [1, 1, 1]);
      var second = EvalWeights.Pick(new SeededRandom(42), [1.0f, 1.5f, 2.0f], [1, 1, 1]);

      // Assert: Same seed and inputs produce identical results.
      Assert.Equal(first, second);
    }

    [Fact]
    public void Pick_Float_WithDifferentSeeds_ProducesDifferentValues()
    {
      // Act: Evaluate the same range twice with differently seeded generators.
      var first = EvalWeights.Pick(new SeededRandom(42), [1.0f, 1.5f, 2.0f], [1, 1, 1]);
      var second = EvalWeights.Pick(new SeededRandom(43), [1.0f, 1.5f, 2.0f], [1, 1, 1]);

      // Assert: Different seeds produce different results.
      Assert.NotEqual(first, second);
    }

    [Fact]
    public void Pick_Float_TwoCallsInRow_ProducesDifferentValues()
    {
      // Act: Evaluate the same range with same generator.
      var random = new SeededRandom(42);
      var first = EvalWeights.Pick(random, [1.0f, 1.5f, 2.0f], [1, 1, 1]);
      var second = EvalWeights.Pick(random, [1.0f, 1.5f, 2.0f], [1, 1, 1]);

      // Assert: Calling same SeededRandom twice produces different results.
      Assert.NotEqual(first, second);
    }

    [Fact]
    public void Pick_Float_SingleValue_ReturnsThatValue()
    {
      // Act: Evaluate a one-element table.
      var value = EvalWeights.Pick(new SeededRandom(42), [7.0f], [7]);

      // Assert: Only one value is available.
      Assert.Equal(7.0f, value);
    }

    [Fact]
    public void Pick_Float_WithManyDraws_FollowsWeights()
    {
      // Arrange: Fixed seed keeps the test deterministic.
      var random = new SeededRandom(42);

      float[] values = [1.0f, 1.5f, 2.0f];
      int[] weights = [5, 3, 2]; // in total 10

      const int sampleCount = 1000;
      float[] samples = new float[sampleCount];
      int[] counts = new int[values.Length];

      // Act: Draw samples and tally distribution statistics.
      for (int i = 0; i < sampleCount; i++)
      {
        float value = EvalWeights.Pick(random, values, weights);
        samples[i] = value;
        for (int j=0; j<values.Length; j++)
        {
          // we can get away with straight comparison of floats here
          if (value == values[j]) {
            counts[j]++;
            break;
          }
        }
      }

      // Assert: counts roughly follow weights.
      Assert.InRange(counts[0], 450, 550); // 1.0 has weight 5 out of 10
      Assert.InRange(counts[1], 275, 325); // 1.5 has weight 3 out of 10
      Assert.InRange(counts[2], 175, 225); // 2.0 has weight 2 out of 10
    }

    // Exceptions.

    [Fact]
    public void Pick_Float_EmptyArray_ThrowsArgumentException()
    {
      // Act/Assert: Empty array is rejected.
      var exception = Assert.Throws<ArgumentException>(
        () => EvalWeights.Pick(new SeededRandom(7), Array.Empty<float>(), [1, 2]));
      Assert.Equal("values", exception.ParamName);
    }

    [Fact]
    public void Pick_Float_DifferentSizeArray_ThrowsArgumentException()
    {
      // Act/Assert: Arrays with different lengths are rejected.
      var exception = Assert.Throws<ArgumentException>(
        () => EvalWeights.Pick(new SeededRandom(7), [1.0f, 1.5f, 2.0f], [1, 2]));
      Assert.Equal("weights", exception.ParamName);
    }
  }

  /// <summary>Tests of the constant-time input checks and the documented weight contract.</summary>
  public sealed class ValidationTests
  {
    [Fact]
    public void Pick_NullRandom_ThrowsArgumentNullException()
    {
      // Act/Assert: A missing generator is rejected with its parameter name.
      var exception = Assert.Throws<ArgumentNullException>(
        () => EvalWeights.Pick(null!, [1, 2], [1, 1]));
      Assert.Equal("random", exception.ParamName);
    }

    [Fact]
    public void Pick_NullValues_ThrowsArgumentNullException()
    {
      // Act/Assert: Missing values are rejected with their parameter name.
      var exception = Assert.Throws<ArgumentNullException>(
        () => EvalWeights.Pick<int>(new SeededRandom(7), null!, [1, 1]));
      Assert.Equal("values", exception.ParamName);
    }

    [Fact]
    public void Pick_NullWeights_ThrowsArgumentNullException()
    {
      // Act/Assert: Missing weights are rejected with their parameter name.
      var exception = Assert.Throws<ArgumentNullException>(
        () => EvalWeights.Pick(new SeededRandom(7), [1, 2], null!));
      Assert.Equal("weights", exception.ParamName);
    }

    [Fact]
    public void Pick_WeightTotalAtIntMax_PicksValue()
    {
      // Act: Draw with weights summing exactly to the largest allowed total.
      var value = EvalWeights.Pick(new SeededRandom(7), [1, 2], [int.MaxValue - 1, 1]);

      // Assert: The draw succeeds and returns one of the values.
      Assert.Contains(value, new[] { 1, 2 });
    }

    [Fact]
    public void Pick_WithZeroWeight_NeverPicksThatValue()
    {
      // Arrange: Fixed seed keeps the test deterministic.
      var random = new SeededRandom(42);

      // Act/Assert: Across many draws, the zero-weight value is never returned.
      for (int i = 0; i < 1000; i++)
        Assert.NotEqual(33, EvalWeights.Pick(random, [22, 33, 44], [1, 0, 1]));
    }

    [Fact]
    public void Pick_WithAllZeroWeights_ThrowsArgumentOutOfRangeException()
    {
      // Act/Assert: A zero total cannot be drawn from; the documented caller error surfaces
      // from the draw itself rather than from per-weight validation.
      Assert.Throws<ArgumentOutOfRangeException>(
        () => EvalWeights.Pick(new SeededRandom(7), [1, 2], [0, 0]));
    }
  }
}
