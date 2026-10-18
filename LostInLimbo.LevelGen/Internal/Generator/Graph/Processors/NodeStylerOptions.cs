using System;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;

/// <summary>
/// Parameters of <see cref="NodeStyler"/>.
/// </summary>
/// <remarks>
/// Currently placeholder.
/// </remarks>
internal sealed record NodeStylerOptions
{
  /// <summary>
  /// Default instance used by builders that do not pass custom options.
  /// </summary>
  public static NodeStylerOptions Default { get; } = new()
  {
    GroupCount = 3,
  };

  /// <summary>Count of groups.</summary>
  public int GroupCount { get; init; } = 1;

  /// <summary>
  /// Checks that these options are valid.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">
  /// Some parameter has invalid value.
  /// </exception>
  /// <exception cref="ArgumentException">
  /// The parameters contradict each other.
  /// </exception>
  public void Validate()
  {
    if (GroupCount < 1)
      throw new ArgumentOutOfRangeException(nameof(GroupCount), GroupCount, "GroupCount must be at least 1.");
  }
}
