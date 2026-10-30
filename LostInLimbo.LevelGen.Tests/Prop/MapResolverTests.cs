using LostInLimbo.LevelGen.Data.Config.Graph.Validator;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Data;
using LostInLimbo.LevelGen.Internal.Prop.Resolvers.Map;

namespace LostInLimbo.LevelGen.Tests.Prop;

/// <summary>
/// Tests map configuration resolution: validator enablement and defaults.
/// </summary>
public sealed class MapResolverTests
{
  /// <summary>Creates a resolver over a property dataset with the given internal seed.</summary>
  private static MapResolver CreateResolver(long propSeed)
  {
    return new MapResolver(PropMain.Default, new PropData { Seed = propSeed });
  }

  [Fact]
  public void Execute_EnablesValidatorOpts()
  {
    // Act: Resolve map configuration.
    var config = CreateResolver(42).Execute();

    // Assert: Map validation is enabled and everything else stays at default.
    Assert.Equal(CnfValidMapOpt.DefEnabled, config.ValidatorOpts);
    Assert.True(config.ValidatorOpts.Enabled);
  }

  [Fact]
  public void Execute_IsDeterministic()
  {
    // Act: Resolve twice from the same internal seed.
    var first = CreateResolver(42).Execute();
    var second = CreateResolver(42).Execute();

    // Assert: Identical inputs produce identical map configs.
    Assert.Equal(first, second);
  }

  [Fact]
  public void Execute_ResultPassesValidate()
  {
    // Act: Resolve map configuration.
    var config = CreateResolver(42).Execute();

    // Act/Assert: Validation passes without throwing.
    config.Validate();
  }
}
