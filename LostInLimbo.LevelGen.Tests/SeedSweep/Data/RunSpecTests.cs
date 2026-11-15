using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.SeedSweep.Data;

namespace LostInLimbo.LevelGen.Tests.SeedSweep.Data;

/// <summary>Tests <see cref="RunSpec"/>: the index to props mapping.</summary>
public sealed class RunSpecTests
{
  [Fact]
  public void ForIndex_SameIndex_ReturnsEqualSpecs()
  {
    // Arrange: Default options.
    var options = new SweepOptions();

    // Act: Map the same index twice.
    var first = RunSpec.ForIndex(924, options);
    var second = RunSpec.ForIndex(924, options);

    // Assert: Identical.
    Assert.Equal(first, second);
  }

  [Theory]
  [InlineData(0, 0, false)]
  [InlineData(20, 20, false)]
  [InlineData(21, 0, true)]
  [InlineData(41, 20, true)]
  [InlineData(42, 0, false)]
  [InlineData(924, 0, false)]
  public void ForIndex_DefaultOptions_CyclesDifficultyAndAlternatesThemes(int index, int difficulty, bool manyThemes)
  {
    // Act: Map with defaults.
    var spec = RunSpec.ForIndex(index, new SweepOptions());

    // Assert: Difficulty cycles 0-20, many themes flips every cycle, seed follows the index.
    Assert.Equal(difficulty, spec.Difficulty);
    Assert.Equal(manyThemes, spec.ManyThemes);
    Assert.Equal($"sweep-{index}", spec.TextSeed);
  }

  [Fact]
  public void ForIndex_DifficultyRange_StaysInRange()
  {
    // Arrange: Range 5-7, themes always on.
    var options = new SweepOptions { DifficultyMin = 5, DifficultyMax = 7, ManyThemes = EnManyThemesMode.On };

    // Act: Map a run of indices.
    var specs = Enumerable.Range(0, 30).Select(i => RunSpec.ForIndex(i, options)).ToList();

    // Assert: Only 5-7, themes on, every difficulty used.
    Assert.All(specs, s => Assert.InRange(s.Difficulty, 5, 7));
    Assert.All(specs, s => Assert.True(s.ManyThemes));
    Assert.Equal([5, 6, 7], specs.Select(s => s.Difficulty).Distinct().Order());
  }

  [Fact]
  public void ForIndex_NegativeIndex_Throws()
  {
    // Act: Map an invalid index.
    var act = () => RunSpec.ForIndex(-1, new SweepOptions());

    // Assert: Rejected.
    Assert.Throws<ArgumentOutOfRangeException>(act);
  }

  [Fact]
  public void ToProps_CopiesValues()
  {
    // Arrange: A spec.
    var spec = new RunSpec(3, 5, true, "148");

    // Act: Build props.
    PropMain props = spec.ToProps();

    // Assert: Values copied.
    Assert.Equal(5, props.Difficulty);
    Assert.True(props.ManyThemes);
    Assert.Equal("148", props.TextSeed);
  }
}
