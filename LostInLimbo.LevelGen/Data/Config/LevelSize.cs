using System;

namespace LostInLimbo.LevelGen.Data.Config;

/// <summary>
/// Size of a generated level, expressed in whole chunks along the X, Y and Z grid axes.
/// </summary>
/// <remarks>
/// The level is laid out as a top-down X/Y/Z grid of chunks. All values are counts of chunks,
/// not cells. Use <see cref="ChunkSize"/> to control how many cells a single chunk contains.
/// </remarks>
public sealed record LevelSize
{
  /// <summary>Default number of chunks along the X axis.</summary>
  public const int DefaultX = 3;

  /// <summary>Default number of chunks along the Y axis.</summary>
  public const int DefaultY = 3;

  /// <summary>Default number of chunks along the Z axis.</summary>
  public const int DefaultZ = 3;

  /// <summary>Shared default instance (3x3x3 chunks).</summary>
  public static LevelSize Default { get; } = new();

  /// <summary>Number of chunks along the X axis. Must be at least 1.</summary>
  public int X { get; init; } = DefaultX;

  /// <summary>Number of chunks along the Y axis. Must be at least 1.</summary>
  public int Y { get; init; } = DefaultY;

  /// <summary>Number of chunks along the Z axis. Must be at least 1.</summary>
  public int Z { get; init; } = DefaultZ;

  /// <summary>
  /// Checks that this size can be used for generation.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">A size is less than 1 chunk.</exception>
  public void Validate()
  {
    if (X < 1)
      throw new ArgumentOutOfRangeException(nameof(X), X, "Level size along X must be at least 1 chunk.");

    if (Y < 1)
      throw new ArgumentOutOfRangeException(nameof(Y), Y, "Level size along Y must be at least 1 chunk.");

    if (Z < 1)
      throw new ArgumentOutOfRangeException(nameof(Z), Z, "Level size along Z must be at least 1 chunk.");
  }
}
