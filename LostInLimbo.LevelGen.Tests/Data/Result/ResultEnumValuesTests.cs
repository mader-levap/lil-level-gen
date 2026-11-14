using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Data.Result.Stats;

namespace LostInLimbo.LevelGen.Tests.Data.Result;

/// <summary>
/// Pins the numeric values of the result enums. The binary world format stores them as bytes,
/// so a changed value would silently corrupt saved worlds; changing one requires a deliberate
/// test update and a format version bump.
/// </summary>
public sealed class ResultEnumValuesTests
{
  [Fact]
  public void EnPhase_HasPinnedValues()
  {
    // Assert: Every member keeps its stored value.
    Assert.Equal(0, (int)EnPhase.Prepass);
    Assert.Equal(1, (int)EnPhase.Graph);
    Assert.Equal(2, (int)EnPhase.Map);
    Assert.Equal(3, (int)EnPhase.Finalization);
    Assert.Equal(4, (int)EnPhase.Completed);
    Assert.Equal(5, Enum.GetValues<EnPhase>().Length);
  }

  [Fact]
  public void EnPoiType_HasPinnedValues()
  {
    // Assert: Every member keeps its stored value.
    Assert.Equal(0, (int)EnPoiType.Start);
    Assert.Equal(1, (int)EnPoiType.Exit);
    Assert.Equal(2, (int)EnPoiType.Goal);
    Assert.Equal(3, (int)EnPoiType.OptionalGoal);
    Assert.Equal(4, (int)EnPoiType.Solution);
    Assert.Equal(5, Enum.GetValues<EnPoiType>().Length);
  }

  [Fact]
  public void EnPassageDirection_HasPinnedValues()
  {
    // Assert: Every member keeps its stored value.
    Assert.Equal(0, (int)EnPassageDirection.Bidirectional);
    Assert.Equal(1, (int)EnPassageDirection.OneWay);
    Assert.Equal(2, Enum.GetValues<EnPassageDirection>().Length);
  }

  [Fact]
  public void EnConditionType_HasPinnedValues()
  {
    // Assert: Every member keeps its stored value.
    Assert.Equal(0, (int)EnConditionType.RequiresSolution);
    Assert.Single(Enum.GetValues<EnConditionType>());
  }
}
