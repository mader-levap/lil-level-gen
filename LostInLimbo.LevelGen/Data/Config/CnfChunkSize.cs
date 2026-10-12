using System;

namespace LostInLimbo.LevelGen.Data.Config;

/// <summary>
/// Size of a single chunk, expressed in cells along the X, Y and Z grid axes.
/// </summary>
/// <remarks>
/// Cells are the smallest unit of the generation grid: room and corridor placement happens in
/// cells. The cell grid of a chunk is local to that chunk; world position is derived from the
/// chunk's coordinates in the level (see <see cref="CnfLevelSize"/>).
/// </remarks>
public sealed record CnfChunkSize
{
  /// <summary>Default number of cells along the X axis.</summary>
  public const int DefaultX = 16;

  /// <summary>Default number of cells along the Y axis.</summary>
  public const int DefaultY = 16;

  /// <summary>Default number of cells along the Z axis.</summary>
  public const int DefaultZ = 16;

  /// <summary>Shared default instance (16x16x16 cells).</summary>
  public static CnfChunkSize Default { get; } = new();

  /// <summary>Number of cells along the X axis. Must be at least 1.</summary>
  public int X { get; init; } = DefaultX;

  /// <summary>Number of cells along the Y axis. Must be at least 1.</summary>
  public int Y { get; init; } = DefaultY;

  /// <summary>Number of cells along the Z axis. Must be at least 1.</summary>
  public int Z { get; init; } = DefaultZ;

  /// <summary>
  /// Checks that this size can be used for generation.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">A size is less than 1 cell.</exception>
  public void Validate()
  {
    if (X < 1)
      throw new ArgumentOutOfRangeException(nameof(X), X, "Chunk size along X must be at least 1 cell.");

    if (Y < 1)
      throw new ArgumentOutOfRangeException(nameof(Y), Y, "Chunk size along Y must be at least 1 cell.");

    if (Z < 1)
      throw new ArgumentOutOfRangeException(nameof(Z), Z, "Chunk size along Z must be at least 1 cell.");
  }
}
