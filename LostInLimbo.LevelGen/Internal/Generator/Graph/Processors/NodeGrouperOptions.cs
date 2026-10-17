namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;

/// <summary>
/// Parameters of <see cref="NodeGrouper"/>.
/// </summary>
internal sealed record NodeGrouperOptions
{
  /// <summary>
  /// Default instance used by builders that do not pass custom options.
  /// </summary>
  public static NodeGrouperOptions Default { get; } = new()
  {
  };
}
