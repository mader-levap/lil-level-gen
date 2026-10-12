namespace LostInLimbo.LevelGen.Data.Other;

/// <summary>
/// Provides current status of generation and other related data that allow tracking progress.
/// </summary>
/// <remarks>
/// Immutable snapshot: replace the whole instance instead of mutating it, so the status can be
/// handed to another thread safely.
/// </remarks>
public sealed record GenerationStatus
{
  /// <summary>Default instance.</summary>
  public static GenerationStatus Default { get; } = new();
  
  /// <summary>
  /// If true, generation is running at this moment.
  /// </summary>
  public bool Running { get; init; }
}