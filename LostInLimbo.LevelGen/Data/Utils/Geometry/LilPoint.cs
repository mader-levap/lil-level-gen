using System;

namespace LostInLimbo.LevelGen.Data.Utils.Geometry;

/// <summary>
/// Cell-based point coordinates. Immutable; compares by value.
/// </summary>
/// <param name="X">X coordinate. Right (plus) - left (minus).</param>
/// <param name="Y">Y coordinate. Far (plus) - near (minus).</param>
/// <param name="Z">Z coordinate. Up (plus) - down (minus).</param>
public readonly record struct LilPoint(int X, int Y, int Z)
{
  /// <summary>
  /// Move the point in the given direction.
  /// </summary>
  /// <param name="dimension">The dimension (axis) to move along.</param>
  /// <param name="amount">The amount of cells; negative moves against the axis.</param>
  /// <returns>A new point moved along <paramref name="dimension"/>; this point is unchanged.</returns>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="dimension"/> is not a defined <see cref="EnDimension"/> value.</exception>
  public LilPoint Move(EnDimension dimension, int amount)
  {
    return dimension switch
    {
      EnDimension.X => this with { X = X + amount },
      EnDimension.Y => this with { Y = Y + amount },
      EnDimension.Z => this with { Z = Z + amount },
      _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Unknown EnDimension value."),
    };
  }
}
