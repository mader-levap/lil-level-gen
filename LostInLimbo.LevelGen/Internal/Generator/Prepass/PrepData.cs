namespace LostInLimbo.LevelGen.Internal.Generator.Prepass;

/// <summary>
/// Holds data derived from config before actual generation.
/// </summary>
internal sealed record PrepData
{
  /// <summary>Sub-seed for graph generation.</summary>
  public long SeedGraph { get; set; } = 0;
  /// <summary>Sub-seed for map generation.</summary>
  public long SeedMap { get; set; } = 0;
}