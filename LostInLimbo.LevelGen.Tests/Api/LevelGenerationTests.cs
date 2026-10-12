using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Other;

namespace LostInLimbo.LevelGen.Tests.Api;

public sealed class LevelGenerationTests
{
  [Fact]
  public void Status_Default_ReturnsNotRunning()
  {
    // Arrange: No generation is ongoing.
    
    // Act: Read status without starting any generation.
    var status = LevelGeneration.Status();

    // Assert: No generation is reported as running.
    Assert.False(status.Running);
  }

  [Fact]
  public void Start_Placeholder_ReturnsFalseAndLeavesStatusNotRunning()
  {
    // Arrange: Default configuration.
    var config = CnfLevelGeneration.Default;

    // Act: Begin a full-level run (placeholder implementation).
    var hasMoreWork = LevelGeneration.Start(EnRunKind.All, config);

    // Assert: There is nothing to do yet and status reports no running generation.
    Assert.False(hasMoreWork);
    Assert.False(LevelGeneration.Status().Running);
  }

  [Fact]
  public void Continue_Placeholder_ReturnsFalseAndLeavesStatusNotRunning()
  {
    // Arrange: No generation is ongoing.

    // Act: Continue generation one step.
    var hasMoreWork = LevelGeneration.Continue(EnRunKind.Step);

    // Assert: There is nothing to do and status reports no running generation.
    Assert.False(hasMoreWork);
    Assert.False(LevelGeneration.Status().Running);
  }
}
