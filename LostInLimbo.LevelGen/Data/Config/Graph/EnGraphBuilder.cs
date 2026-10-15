namespace LostInLimbo.LevelGen.Data.Config.Graph;

/// <summary>
/// Enumerates known graph builders.
/// </summary>
public enum EnGraphBuilder
{
  /// <summary>Only one node with start and exit.</summary>
  Simple,
  /// <summary>Creates linear chain of nodes.</summary>
  Linear,
}