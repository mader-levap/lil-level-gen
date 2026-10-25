using LostInLimbo.LevelGen.Data.Config;

namespace LostInLimbo.LevelGen.Tests.Data.Config;

/// <summary>
/// Tests the invariants of the debug configuration section.
/// </summary>
public sealed class CnfDebugTests
{
  [Fact]
  public void Validate_WithNegativePlaceholderSleep_ThrowsNamingPlaceholderSleep()
  {
    // Arrange: Debug config asking the placeholder loop to sleep for a negative time.
    var debug = new CnfDebug { PlaceholderSleep = -1 };

    // Act/Assert: The out-of-range value is rejected naming the offending property.
    var exception = Assert.Throws<ArgumentOutOfRangeException>(() => debug.Validate());
    Assert.Equal("PlaceholderSleep", exception.ParamName);
  }

  [Fact]
  public void Validate_WithPlaceholderStepsBelowOne_ThrowsNamingPlaceholderSteps()
  {
    // Arrange: Debug config asking the placeholder loop for less than one step.
    var debug = new CnfDebug { PlaceholderSteps = 0 };

    // Act/Assert: The out-of-range value is rejected naming the offending property.
    var exception = Assert.Throws<ArgumentOutOfRangeException>(() => debug.Validate());
    Assert.Equal("PlaceholderSteps", exception.ParamName);
  }

  [Fact]
  public void Validate_WithBoundaries_DoesNotThrow()
  {
    // Arrange: Debug config at the lowest valid values of both numeric settings.
    var debug = new CnfDebug { PlaceholderSleep = 0, PlaceholderSteps = 1 };

    // Assert: The documented boundaries are inclusive, so validation completes normally.
    debug.Validate();
  }

  [Fact]
  public void Validate_WithDefaultInstance_DoesNotThrow()
  {
    // Arrange/Act: The default instance uses the documented defaults.
    var debug = CnfDebug.Default;

    // Assert: No invariant is violated, so validation completes normally.
    debug.Validate();
  }

  [Fact]
  public void Defaults_WithFreshInstance_HaveFailureHooksDisabled()
  {
    // Arrange/Act: A freshly constructed debug config.
    var debug = new CnfDebug();

    // Assert: Neither deliberate failure hook is active unless a test turns it on.
    Assert.False(debug.FailAtStart);
    Assert.False(debug.FailGraphBuild);
    Assert.False(debug.FailGraphValidation);
  }
}
