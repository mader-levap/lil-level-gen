namespace LostInLimbo.LevelGen.Internal.Prop.Data;

/// <summary>
/// Internal data supporting config generation.
/// </summary>
public sealed record PropData
{
  /// <summary>Default seed.</summary>
  public const long DefaultSeed = 0L;

  // Defaults.

  /// <summary>Default instance.</summary>
  public static PropData Default { get; } = new();

  // Fields.

  /// <summary>Internal seed. Independent from seed used in configuration.</summary>
  public long Seed { get; init; } = DefaultSeed;
}
