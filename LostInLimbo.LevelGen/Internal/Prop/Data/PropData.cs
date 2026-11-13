namespace LostInLimbo.LevelGen.Internal.Prop.Data;

/// <summary>
/// Internal data supporting config generation.
/// </summary>
public sealed record PropData
{
  /// <summary>Default seed.</summary>
  public const long DefaultSeed = 0L;

  /// <summary>Default size.</summary>
  public const int DefaultSize = 2;

  // Defaults.

  /// <summary>Default instance.</summary>
  public static PropData Default { get; } = new();

  // Fields.

  /// <summary>
  /// Internal seed driving config-generation draws. Derived from <see cref="WorldSeed"/> with its
  /// own salt, so it differs from the configuration's world seed.
  /// </summary>
  public long Seed { get; init; } = DefaultSeed;

  /// <summary>
  /// World seed reported as <c>CnfGeneral.Seed</c>: the text-seed hash, or the clock when no text
  /// seed is given. Resolved once by <c>ConfigGenerator</c>, so every resolver sees the same value.
  /// </summary>
  public long WorldSeed { get; init; } = DefaultSeed;

  /// <summary>Size of the graph. Should be pre-calculated early as it is needed for a lot of other calculations.</summary>
  public int Size { get; init; } = DefaultSize;
}
