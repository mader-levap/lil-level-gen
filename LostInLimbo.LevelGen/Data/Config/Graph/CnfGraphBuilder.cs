using System;

namespace LostInLimbo.LevelGen.Data.Config.Graph;

/// <summary>
/// <para>Contains graph builder-specific configuration used to generate the graph.</para>
/// </summary>
public sealed record CnfGraphBuilder
{
  // Defaults.

  /// <summary>Default instance.</summary>
  public static CnfGraphBuilder Default { get; } = new();

  // Fields.

  /// <summary>
  /// Graph builder to use.
  /// </summary>
  public EnGraphBuilder Builder { get; init; } = EnGraphBuilder.OneNode;
}
