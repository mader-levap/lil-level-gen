namespace LostInLimbo.LevelGen.Data.Result.Stats;

/// <summary>
/// <para>Statistics about the created world.</para>
/// </summary>
public sealed record LilStats
{
  /// <summary>How many steps were made during generation so far.</summary>
  public long Steps { get; set; } = 0;
}