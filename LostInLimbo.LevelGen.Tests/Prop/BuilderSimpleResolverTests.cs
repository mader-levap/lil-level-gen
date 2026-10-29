using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Resolvers.Graph;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Tests.Prop;

/// <summary>
/// Tests simple graph builder option resolution. Values are currently fixed placeholders
/// until the planned randomization lands.
/// </summary>
public sealed class BuilderSimpleResolverTests
{
  /// <summary>Creates a resolver over a property dataset and a fixed random stream.</summary>
  private static BuilderSimpleResolver CreateResolver(long propSeed)
  {
    return new BuilderSimpleResolver(PropMain.Default, new PropData { Seed = propSeed }, new SeededRandom(propSeed));
  }

  [Fact]
  public void Execute_ReturnsPinnedValues()
  {
    // Act: Resolve simple builder options.
    var config = CreateResolver(42).Execute();

    // Assert: Values match the pinned placeholders, so the planned conversion to
    // randomized values shows up as an intentional test update.
    Assert.Equal(0.5f, config.ChokepointChance);
    Assert.Equal(4, config.MaxExtraDegree);
    Assert.Equal(7, config.MaxExtraDegreeStar);
  }

  [Fact]
  public void Execute_IsDeterministic()
  {
    // Act: Resolve twice from the same inputs.
    var first = CreateResolver(42).Execute();
    var second = CreateResolver(42).Execute();

    // Assert: Identical inputs produce identical configs.
    Assert.Equal(first, second);
  }

  [Fact]
  public void Execute_ResultPassesValidate()
  {
    // Act: Resolve simple builder options.
    var config = CreateResolver(42).Execute();

    // Act/Assert: Validation passes without throwing.
    config.Validate();
  }
}
