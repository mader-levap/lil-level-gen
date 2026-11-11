using LostInLimbo.LevelGen.Data.Result.Stats;
using LostInLimbo.LevelGen.Internal.Status;

namespace LostInLimbo.LevelGen.Tests.Status;

/// <summary>
/// Tests <see cref="GenerationProgress"/>: phase weights and the pure completion computation.
/// </summary>
public sealed class GenerationProgressTests
{
  /// <summary>Precision (decimal places) used when comparing floats.</summary>
  private const int Precision = 5;

  /// <summary>Phases a run can be inside of, in generation order.</summary>
  private static readonly EnPhase[] ActivePhases = [EnPhase.Prepass, EnPhase.Graph, EnPhase.Map, EnPhase.Finalization];

  [Fact]
  public void Weights_SumToOne()
  {
    // Arrange/Act: Sum of all phase weights.
    var sum = GenerationProgress.PrepassWeight + GenerationProgress.GraphWeight
      + GenerationProgress.MapWeight + GenerationProgress.FinalizationWeight;

    // Assert: Phases cover the whole range.
    Assert.Equal(1.0f, sum, Precision);
  }

  [Fact]
  public void Compute_AtStartOfPrepass_ReturnsZero()
  {
    // Act/Assert: Nothing done yet.
    Assert.Equal(0.0f, GenerationProgress.Compute(EnPhase.Prepass, 0.0f));
  }

  [Theory]
  [InlineData(0.0f)]
  [InlineData(0.5f)]
  [InlineData(1.0f)]
  public void Compute_ForCompleted_ReturnsExactlyOne(float phaseFraction)
  {
    // Act/Assert: Completed is always full progress, regardless of the fraction.
    Assert.Equal(1.0f, GenerationProgress.Compute(EnPhase.Completed, phaseFraction));
  }

  [Fact]
  public void Compute_EndOfPhase_EqualsStartOfNextPhase()
  {
    // Act/Assert: Phase boundaries line up, so there is no jump between phases.
    for (var index = 0; index < ActivePhases.Length - 1; index++)
    {
      var end = GenerationProgress.Compute(ActivePhases[index], 1.0f);
      var nextStart = GenerationProgress.Compute(ActivePhases[index + 1], 0.0f);
      Assert.Equal(nextStart, end, Precision);
    }

    // Assert: The end of the last phase reaches full progress.
    Assert.Equal(1.0f, GenerationProgress.Compute(EnPhase.Finalization, 1.0f), Precision);
  }

  [Fact]
  public void Compute_InsideMap_IsWeightedByMapShare()
  {
    // Act: Halfway through the map.
    var completion = GenerationProgress.Compute(EnPhase.Map, 0.5f);

    // Assert: Prepass and graph are done, plus half of the map share.
    var expected = GenerationProgress.PrepassWeight + GenerationProgress.GraphWeight + GenerationProgress.MapWeight * 0.5f;
    Assert.Equal(expected, completion, Precision);
  }

  [Fact]
  public void Compute_OverPhasesAndFractions_IsNonDecreasing()
  {
    // Arrange: Fractions sampled across each phase.
    float[] fractions = [0.0f, 0.25f, 0.5f, 0.75f, 1.0f];
    var previous = 0.0f;

    // Act/Assert: Walking forward through the run never lowers progress.
    foreach (var phase in ActivePhases)
    {
      foreach (var fraction in fractions)
      {
        var completion = GenerationProgress.Compute(phase, fraction);
        Assert.True(completion >= previous, $"Completion dropped at {phase} {fraction}: {previous} -> {completion}.");
        previous = completion;
      }
    }
  }

  [Theory]
  [InlineData(-1.0f, 0.0f)]
  [InlineData(2.0f, 1.0f)]
  [InlineData(float.NaN, 0.0f)]
  [InlineData(float.PositiveInfinity, 1.0f)]
  public void Compute_WithOutOfRangeFraction_ClampsFraction(float phaseFraction, float clampedFraction)
  {
    // Act: Fraction outside [0, 1].
    var completion = GenerationProgress.Compute(EnPhase.Map, phaseFraction);

    // Assert: Same result as the clamped fraction.
    Assert.Equal(GenerationProgress.Compute(EnPhase.Map, clampedFraction), completion, Precision);
  }

  [Fact]
  public void Compute_WithUndefinedPhase_ThrowsArgumentOutOfRangeException()
  {
    // Act/Assert: Unknown phase is rejected with the parameter name.
    var exception = Assert.Throws<ArgumentOutOfRangeException>(() => GenerationProgress.Compute((EnPhase)999, 0.0f));
    Assert.Equal("phase", exception.ParamName);
  }
}
