using LostInLimbo.LevelGen.Internal.Utils.Eval;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Tests.Utils.Eval;

/// <summary>
/// Tests <see cref="EvalWeights"/>: reproducibility, range compliance, and boundary behavior.
/// </summary>
public sealed class EvalWeightsTests
{
  /// <summary>Tests of Int weights.</summary>
  public sealed class IntTests
  {
    [Fact]
    public void Int_WithSameSeed_ReturnsSameValue()
    {
      // Act: Evaluate the same range twice with identically seeded generators.
      var first = EvalWeights.Int(new SeededRandom(42), [22, 33, 44], [1, 1, 1]);
      var second = EvalWeights.Int(new SeededRandom(42), [22, 33, 44], [1, 1, 1]);

      // Assert: Same seed and inputs produce identical results.
      Assert.Equal(first, second);
    }

    [Fact]
    public void Int_WithDifferentSeeds_ProducesDifferentValues()
    {
      // Act: Evaluate the same range twice with differently seeded generators.
      var first = EvalWeights.Int(new SeededRandom(42), [22, 33, 44], [1, 1, 1]);
      var second = EvalWeights.Int(new SeededRandom(43), [22, 33, 44], [1, 1, 1]);

      // Assert: Different seeds produce different results.
      Assert.NotEqual(first, second);
    }
    
    [Fact]
    public void Int_TwoCallsInRow_ProducesDifferentValues()
    {
      // Act: Evaluate the same range with same generator.
      var random = new SeededRandom(42);
      var first = EvalWeights.Int(random, [22, 33, 44], [1, 1, 1]);
      var second = EvalWeights.Int(random, [22, 33, 44], [1, 1, 1]);

      // Assert: Calling same SeededRandom twice produces different results.
      Assert.NotEqual(first, second);
    }

    [Fact]
    public void Int_SingleValue_ReturnsThatValue()
    {
      // Act: Evaluate a one-element table.
      var value = EvalWeights.Int(new SeededRandom(42), [7], [1]);

      // Assert: Only one value is available.
      Assert.Equal(7, value);
    }

    [Fact]
    public void Int_WithManyDraws_FollowsWeights()
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
        int value = EvalWeights.Int(random, values, weights);
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
    public void Int_EmptyArray_ThrowsArgumentException()
    {
      // Act/Assert: Empty array is rejected.
      var exception = Assert.Throws<ArgumentException>(
        () => EvalWeights.Int(new SeededRandom(7), [], [1, 2]));
      Assert.Equal("values", exception.ParamName);
    }

    [Fact]
    public void Int_DifferentSizeArray_ThrowsArgumentException()
    {
      // Act/Assert: Arrays with different lengths are rejected.
      var exception = Assert.Throws<ArgumentException>(
        () => EvalWeights.Int(new SeededRandom(7), [1, 2, 3], [1, 2]));
    }
  }
  /// <summary>Tests of Float weights.</summary>
  public sealed class FloatTests
  {
    [Fact]
    public void Float_WithSameSeed_ReturnsSameValue()
    {
      // Act: Evaluate the same range twice with identically seeded generators.
      var first = EvalWeights.Float(new SeededRandom(42), [1.0f, 1.5f, 2.0f], [1, 1, 1]);
      var second = EvalWeights.Float(new SeededRandom(42), [1.0f, 1.5f, 2.0f], [1, 1, 1]);

      // Assert: Same seed and inputs produce identical results.
      Assert.Equal(first, second);
    }

    [Fact]
    public void Float_WithDifferentSeeds_ProducesDifferentValues()
    {
      // Act: Evaluate the same range twice with differently seeded generators.
      var first = EvalWeights.Float(new SeededRandom(42), [1.0f, 1.5f, 2.0f], [1, 1, 1]);
      var second = EvalWeights.Float(new SeededRandom(43), [1.0f, 1.5f, 2.0f], [1, 1, 1]);

      // Assert: Different seeds produce different results.
      Assert.NotEqual(first, second);
    }
    
    [Fact]
    public void Float_TwoCallsInRow_ProducesDifferentValues()
    {
      // Act: Evaluate the same range with same generator.
      var random = new SeededRandom(42);
      var first = EvalWeights.Float(random, [1.0f, 1.5f, 2.0f], [1, 1, 1]);
      var second = EvalWeights.Float(random, [1.0f, 1.5f, 2.0f], [1, 1, 1]);

      // Assert: Calling same SeededRandom twice produces different results.
      Assert.NotEqual(first, second);
    }

    [Fact]
    public void Float_SingleValue_ReturnsThatValue()
    {
      // Act: Evaluate a one-element table.
      var value = EvalWeights.Float(new SeededRandom(42), [7.0f], [7]);

      // Assert: Only one value is available.
      Assert.Equal(7.0f, value);
    }

    [Fact]
    public void Float_WithManyDraws_FollowsWeights()
    {
      // Arrange: Fixed seed keeps the test deterministic.
      var random = new SeededRandom(42);

      float[] values = [1.0f, 1.5f, 2.0f];
      int[] weights = [5, 3, 2]; // in total 10

      const int SampleCount = 1000;
      float[] samples = new float[SampleCount];
      int[] counts = new int[values.Length];

      // Act: Draw samples and tally distribution statistics.
      for (int i = 0; i < SampleCount; i++)
      {
        float value = EvalWeights.Float(random, values, weights);
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
    public void Float_EmptyArray_ThrowsArgumentException()
    {
      // Act/Assert: Empty array is rejected.
      var exception = Assert.Throws<ArgumentException>(
        () => EvalWeights.Float(new SeededRandom(7), [], [1, 2]));
      Assert.Equal("values", exception.ParamName);
    }

    [Fact]
    public void Float_DifferentSizeArray_ThrowsArgumentException()
    {
      // Act/Assert: Arrays with different lengths are rejected.
      var exception = Assert.Throws<ArgumentException>(
        () => EvalWeights.Float(new SeededRandom(7), [1.0f, 1.5f, 2.0f], [1, 2]));
    }
  }
}