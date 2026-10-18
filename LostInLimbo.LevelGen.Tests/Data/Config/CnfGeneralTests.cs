using LostInLimbo.LevelGen.Data.Config;

namespace LostInLimbo.LevelGen.Tests.Data.Config;

/// <summary>
/// Tests the invariants of the general configuration section.
/// </summary>
public sealed class CnfGeneralTests
{
  [Theory]
  [InlineData(-1.0f)]
  [InlineData(float.NaN)]
  [InlineData(float.PositiveInfinity)]
  [InlineData(float.NegativeInfinity)]
  public void Validate_WithCorruptionOutsideValidRange_ThrowsNamingDegeneration(float degeneration)
  {
    // Arrange: General config with a degeneration level that is negative or not finite.
    var general = new CnfGeneral { Degeneration = degeneration };

    // Act/Assert: The out-of-range value is rejected naming the offending property.
    var exception = Assert.Throws<ArgumentOutOfRangeException>(() => general.Validate());
    Assert.Equal("Degeneration", exception.ParamName);
  }

  [Theory]
  [InlineData(0.0f)]
  [InlineData(0.5f)]
  [InlineData(1.5f)]
  public void Validate_WithFiniteNonNegativeDegeneration_DoesNotThrow(float degeneration)
  {
    // Arrange: General config with a degeneration level inside the valid range.
    var general = new CnfGeneral { Degeneration = degeneration };

    // Assert: Validation completes normally for any finite value >= 0.
    general.Validate();
  }

  [Fact]
  public void Validate_WithDefaultInstance_DoesNotThrow()
  {
    // Arrange/Act: The default instance uses the default seed and corruption.
    var general = CnfGeneral.Default;

    // Assert: No invariant is violated, so validation completes normally.
    general.Validate();
  }
}
