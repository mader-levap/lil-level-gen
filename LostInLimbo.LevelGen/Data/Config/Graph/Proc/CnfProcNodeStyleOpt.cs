using System;

namespace LostInLimbo.LevelGen.Data.Config.Graph.Proc;

/// <summary>
/// <para>Configuration of the node-grouping processor (<c>ProcNodeStyle</c>).</para>
/// </summary>
public sealed record CnfProcNodeStyleOpt
{
  /// <summary>
  /// Default instance: three groups.
  /// </summary>
  public static CnfProcNodeStyleOpt Default { get; } = new()
  {
    GroupCount = 3,
  };

  /// <summary>
  /// Requested number of groups. Soft target: reduced to the node count when the graph
  /// is smaller. Must be at least 1.
  /// </summary>
  public int GroupCount { get; init; } = 3;

  // Helpers.

  /// <summary>
  /// Checks that this config can be used for generation.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
  public void Validate()
  {
    if (GroupCount < 1)
      throw new ArgumentOutOfRangeException(nameof(GroupCount), GroupCount, "GroupCount must be at least 1.");
  }
}
