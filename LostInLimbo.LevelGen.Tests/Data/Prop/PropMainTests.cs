using LostInLimbo.LevelGen.Data.Prop;

namespace LostInLimbo.LevelGen.Tests.Data.Prop;

/// <summary>
/// Tests <see cref="PropMain"/> invariants: defaults pass validation, and out-of-range
/// values throw with the correct parameter names.
/// </summary>
public sealed class PropMainTests
{
  [Fact]
  public void Validate_WithDefaultInstance_DoesNotThrow()
  {
    // Arrange: Default instance carries a valid config version and zero difficulty.
    var propMain = PropMain.Default;

    // Act/Assert: Validation passes without throwing.
    propMain.Validate();
  }

  [Fact]
  public void Validate_WithZeroConfigVersion_ThrowsArgumentOutOfRangeExceptionNamingConfigVersion()
  {
    // Arrange: Config version below the minimum of 1.
    var propMain = PropMain.Default with { ConfigVersion = 0 };

    // Act/Assert: Validation rejects the version and names the offending parameter.
    var exception = Assert.Throws<ArgumentOutOfRangeException>(() => propMain.Validate());
    Assert.Equal(nameof(PropMain.ConfigVersion), exception.ParamName);
  }

  [Fact]
  public void Validate_WithZeroDifficulty_DoesNotThrow()
  {
    // Arrange: Difficulty exactly at the allowed minimum.
    var propMain = PropMain.Default with { Difficulty = 0 };

    // Act/Assert: Validation passes at the lower boundary.
    propMain.Validate();
  }

  [Fact]
  public void Validate_WithNegativeDifficulty_ThrowsArgumentOutOfRangeExceptionNamingDifficulty()
  {
    // Arrange: Negative difficulty is below the allowed minimum of 0.
    var propMain = PropMain.Default with { Difficulty = -1 };

    // Act/Assert: Validation rejects the difficulty and names the offending parameter.
    var exception = Assert.Throws<ArgumentOutOfRangeException>(() => propMain.Validate());
    Assert.Equal(nameof(PropMain.Difficulty), exception.ParamName);
  }

  [Fact]
  public void Validate_WithMaxDifficulty_DoesNotThrow()
  {
    // Arrange: Difficulty exactly at the allowed maximum.
    var propMain = PropMain.Default with { Difficulty = PropMain.MaxDifficulty };

    // Act/Assert: Validation passes at the upper boundary.
    propMain.Validate();
  }

  [Fact]
  public void Validate_WithDifficultyAboveMax_ThrowsArgumentOutOfRangeExceptionNamingDifficulty()
  {
    // Arrange: Difficulty above the allowed maximum.
    var propMain = PropMain.Default with { Difficulty = PropMain.MaxDifficulty + 1 };

    // Act/Assert: Validation rejects the difficulty and names the offending parameter.
    var exception = Assert.Throws<ArgumentOutOfRangeException>(() => propMain.Validate());
    Assert.Equal(nameof(PropMain.Difficulty), exception.ParamName);
  }
}
