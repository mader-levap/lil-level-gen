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
  /// Move point in given direction.
  /// </summary>
  /// <param name="dimension">Dimension.</param>
  /// <param name="amount">Amount.</param>
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