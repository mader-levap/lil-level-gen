using LostInLimbo.LevelGen.Data.Config.Graph.Validator;

namespace LostInLimbo.LevelGen.Tests.Data.Config.Graph.Validator;

/// <summary>
/// Tests validation of the validator option records <see cref="CnfValidGraphOpt"/> and
/// <see cref="CnfValidMapOpt"/>: every flag combination is a usable configuration.
/// </summary>
public sealed class CnfValidOptTests
{
  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public void CnfValidGraphOpt_Validate_WithAnyFlags_DoesNotThrow(bool enabled, bool allowOneWayDeadEnds)
  {
    // Arrange: Graph validator options with the given flags.
    var options = new CnfValidGraphOpt { Enabled = enabled, AllowOneWayDeadEnds = allowOneWayDeadEnds };

    // Act/Assert: Every combination validates.
    options.Validate();
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void CnfValidMapOpt_Validate_WithAnyFlag_DoesNotThrow(bool enabled)
  {
    // Arrange: Map validator options with the given flag.
    var options = new CnfValidMapOpt { Enabled = enabled };

    // Act/Assert: Every value validates.
    options.Validate();
  }
}
