using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Resolvers.Graph;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Tests.Prop;

/// <summary>
/// Tests simple graph builder option resolution: ChokepointChance is randomized inside
/// its planned range, while the Max* caps are still fixed placeholders.
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

    // Assert: The still-fixed caps match the pinned placeholders, so their planned
    // conversion to randomized values shows up as an intentional test update.
    Assert.Equal(4, config.MaxExtraDegree);
    Assert.Equal(7, config.MaxExtraDegreeStar);
  }

  [Fact]
  public void Execute_RandomizesChokepointChance_MatchesPinnedValue()
  {
    // Act: Resolve simple builder options from a known stream.
    var config = CreateResolver(42).Execute();

    // Assert: The value matches the pinned draw, so the resolver salt and stream order
    // cannot silently change, and it stays inside the planned randomization range.
    Assert.Equal(0.75617367f, config.ChokepointChance);
    Assert.InRange(config.ChokepointChance, 0.2f, 0.95f);
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
