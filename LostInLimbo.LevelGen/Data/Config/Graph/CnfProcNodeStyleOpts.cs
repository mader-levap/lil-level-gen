using System;

namespace LostInLimbo.LevelGen.Data.Config.Graph;

/// <summary>
/// <para>Configuration of the node-grouping processor (<c>ProcNodeStyle</c>).</para>
/// </summary>
public sealed record CnfProcNodeStyleOpts
{
  /// <summary>
  /// Default instance: three groups.
  /// </summary>
  public static CnfProcNodeStyleOpts Default { get; } = new()
  {
    GroupCount = 3,
  };

  /// <summary>
  /// Requested amount of groups. Soft target: reduced to the node count when the graph
  /// is smaller. Must be at least 1.
  /// </summary>
  public int GroupCount { get; init; } = 3;

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
