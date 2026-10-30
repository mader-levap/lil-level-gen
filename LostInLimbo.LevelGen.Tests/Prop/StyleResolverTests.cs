using LostInLimbo.LevelGen.Data.Config.Style;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Data;
using LostInLimbo.LevelGen.Internal.Prop.Resolvers;

namespace LostInLimbo.LevelGen.Tests.Prop;

/// <summary>
/// Tests style configuration resolution. Style is currently a placeholder pass that
/// returns the default section unchanged.
/// </summary>
public sealed class StyleResolverTests
{
  /// <summary>Creates a resolver over a property dataset with the given internal seed.</summary>
  private static StyleResolver CreateResolver(long propSeed)
  {
    return new StyleResolver(PropMain.Default, new PropData { Seed = propSeed });
  }

  [Fact]
  public void Execute_ReturnsStyleDefault()
  {
    // Act: Resolve style configuration.
    var config = CreateResolver(42).Execute();

    // Assert: Style resolution is still a placeholder returning the default section.
    Assert.Equal(CnfStyle.Default, config);
  }

  [Fact]
  public void Execute_IsDeterministic()
  {
    // Act: Resolve twice from the same internal seed.
    var first = CreateResolver(42).Execute();
    var second = CreateResolver(42).Execute();

    // Assert: Identical inputs produce identical style configs.
    Assert.Equal(first, second);
  }

  [Fact]
  public void Execute_ResultPassesValidate()
  {
    // Act: Resolve style configuration.
    var config = CreateResolver(42).Execute();

    // Act/Assert: Validation passes without throwing.
    config.Validate();
  }
}
