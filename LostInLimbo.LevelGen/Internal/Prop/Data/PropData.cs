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

  /// <summary>Internal seed. Independent from seed used in configuration.</summary>
  public long Seed { get; init; } = DefaultSeed;

  /// <summary>Size of the graph. Should be pre-calculated early as it is needed for a lot of other calculations.</summary>
  public int Size { get; init; } = DefaultSize;
}
