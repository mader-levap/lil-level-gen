namespace LostInLimbo.LevelGen.Data.Result.Graph;

/// <summary>
/// Styling data for given node.
/// </summary>
/// <remarks>
/// For now there is only Group.
/// </remarks>
public sealed record LilStyle
{
  /// <summary>
  /// Group identifier. If null, node is not in any group.
  /// </summary>
  public string? Group { get; init; } = null;
}