namespace LostInLimbo.LevelGen.Data.Config.Graph;

/// <summary>
/// Enumerates known graph builders.
/// </summary>
public enum EnGraphBuilder
{
  /// <summary>Only one node with start and exit.</summary>
  OneNode,
  /// <summary>A bunch of interconnected nodes without any obstacles.</summary>
  Simple,
  /// <summary>Creates a linear chain of nodes.</summary>
  Linear,
}