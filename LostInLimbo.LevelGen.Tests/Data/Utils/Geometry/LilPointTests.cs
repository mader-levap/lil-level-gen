using LostInLimbo.LevelGen.Data.Utils.Geometry;

namespace LostInLimbo.LevelGen.Tests.Data.Utils.Geometry;

/// <summary>
/// Tests the cell-based point utility used by map generation.
/// </summary>
public sealed class LilPointTests
{
  [Theory]
  [InlineData(EnDimension.X, 3, 5, 7)]
  [InlineData(EnDimension.Y, 3, 5, 7)]
  [InlineData(EnDimension.Z, 3, 5, 7)]
  public void Move_WithPositiveAmount_IncrementsOnlyThatAxis(EnDimension dimension, int x, int y, int z)
  {
    // Arrange: Point with distinct coordinates per axis.
    var point = new LilPoint(x, y, z);

    // Act: Move the point along the given dimension.
    point = point.Move(dimension, 1);

    // Assert: Only the moved axis changed; the others kept their coordinates.
    switch (dimension)
    {
      case EnDimension.X:
        Assert.Equal(new LilPoint(x + 1, y, z), point);
        break;
      case EnDimension.Y:
        Assert.Equal(new LilPoint(x, y + 1, z), point);
        break;
      case EnDimension.Z:
        Assert.Equal(new LilPoint(x, y, z + 1), point);
        break;
    }
  }

  [Theory]
  [InlineData(EnDimension.X)]
  [InlineData(EnDimension.Y)]
  [InlineData(EnDimension.Z)]
  public void Move_WithNegativeAmount_DecrementsOnlyThatAxis(EnDimension dimension)
  {
    // Arrange: Point at positive coordinates so a decrement stays representable.
    var point = new LilPoint(3, 5, 7);

    // Act: Move the point against the given dimension.
    point = point.Move(dimension, -2);

    // Assert: Only the moved axis changed, by the negative amount.
    switch (dimension)
    {
      case EnDimension.X:
        Assert.Equal(new LilPoint(1, 5, 7), point);
        break;
      case EnDimension.Y:
        Assert.Equal(new LilPoint(3, 3, 7), point);
        break;
      case EnDimension.Z:
        Assert.Equal(new LilPoint(3, 5, 5), point);
        break;
    }
  }

  [Fact]
  public void Move_WithZeroAmount_LeavesPointUnchanged()
  {
    // Arrange: Point that must not move.
    var point = new LilPoint(1, 2, 3);

    // Act: Move along an axis by zero.
    point = point.Move(EnDimension.Y, 0);

    // Assert: The point still has its original coordinates.
    Assert.Equal(new LilPoint(1, 2, 3), point);
  }

  [Fact]
  public void LilPoint_WithSameCoordinates_IsValueEqual()
  {
    // Arrange: Two instances carrying the same coordinates.
    var point1 = new LilPoint(1, 2, 3);
    var point2 = new LilPoint(1, 2, 3);

    // Assert: The record struct compares by value, never by reference.
    Assert.Equal(point1, point2);
    Assert.True(point1 == point2);
  }

  [Fact]
  public void LilPoint_WithDifferentCoordinates_IsNotValueEqual()
  {
    // Arrange: Two instances differing in one axis.
    var point1 = new LilPoint(1, 2, 3);
    var point2 = new LilPoint(1, 2, 4);

    // Assert: A single differing axis makes the points unequal.
    Assert.NotEqual(point1, point2);
    Assert.True(point1 != point2);
  }
}
