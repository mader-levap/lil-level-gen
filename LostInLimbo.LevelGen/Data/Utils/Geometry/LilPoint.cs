using System.Runtime.CompilerServices;

namespace LostInLimbo.LevelGen.Data.Utils.Geometry;

/// <summary>
/// Cell-based point coordinates.
/// </summary>
/// <param name="X">X coordinate. Right (plus) - left (minus).</param>
/// <param name="Y">Y coordinate. Far (plus) - near (minus).</param>
/// <param name="Z">Z coordinate. Up (plus) - down (minus).</param>
public record struct LilPoint(int X, int Y, int Z)
{
  /// <summary>
  /// Move the point in the given direction.
  /// </summary>
  /// <param name="dimension">The dimension.</param>
  /// <param name="amount">The amount.</param>
  public void Move(EnDimension dimension, int amount)
  {
    switch (dimension)
    {
      case EnDimension.X :
        X += amount;
        break;
      case EnDimension.Y :
        Y += amount;
        break;
      case EnDimension.Z :
        Z += amount;
        break;
    }
  }
}