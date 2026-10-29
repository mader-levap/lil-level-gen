using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Resolvers.Graph;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Tests.Prop;

/// <summary>
/// Tests linear graph builder option resolution. Values are currently fixed placeholders
/// until the planned randomization lands.
/// </summary>
public sealed class BuilderLinearResolverTests
{
  /// <summary>Creates a resolver over a property dataset and a fixed random stream.</summary>
  private static BuilderLinearResolver CreateResolver(long propSeed)
  {
    return new BuilderLinearResolver(PropMain.Default, new PropData { Seed = propSeed }, new SeededRandom(propSeed));
  }

  [Fact]
  public void Execute_ReturnsPinnedValues()
  {
    // Act: Resolve linear builder options.
    var config = CreateResolver(42).Execute();

    // Assert: Values match the pinned placeholders, so the planned conversion to
    // randomized values shows up as an intentional test update.
    Assert.Equal(5, config.MaxEdges);
    Assert.Equal(2, config.MaxExtraDegree);
    Assert.Equal(3, config.MaxConditionPerRel);
    Assert.Equal(3, config.MaxConditionDistance);
    Assert.Equal(2, config.MaxSkipDistance);
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
    // Act: Resolve linear builder options.
    var config = CreateResolver(42).Execute();

    // Act/Assert: Validation passes without throwing.
    config.Validate();
  }
}
