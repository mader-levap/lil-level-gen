using System;

namespace LostInLimbo.LevelGen.Data.Config.Graph;

/// <summary>
/// <para>Options of the graph finalization processors.</para>
/// </summary>
/// <remarks>
/// One section per processor. Sections left out of a config keep their defaults.
/// Builders that run no processors (for example <c>OneNode</c>) ignore this config entirely.
/// </remarks>
public sealed record CnfProcOpts
{
  /// <summary>Default instance.</summary>
  public static CnfProcOpts Default { get; } = new();

  /// <summary>Options of the secret-nodes processor.</summary>
  public CnfProcRelSecretsOpts RelSecrets { get; init; } = CnfProcRelSecretsOpts.Default;

  /// <summary>Options of the objective-POIs processor.</summary>
  public CnfProcNodeObjectivesOpts NodeObjectives { get; init; } = CnfProcNodeObjectivesOpts.Default;

  /// <summary>Options of the relative-size processor.</summary>
  public CnfProcNodeRelSizeOpts NodeRelSize { get; init; } = CnfProcNodeRelSizeOpts.Default;

  /// <summary>Options of the node-grouping processor.</summary>
  public CnfProcNodeStyleOpts NodeStyle { get; init; } = CnfProcNodeStyleOpts.Default;

  /// <summary>
  /// Checks that this config can be used for generation.
  /// </summary>
  /// <exception cref="ArgumentException">A section is null.</exception>
  /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
  /// <exception cref="ArgumentException">Some values contradict each other.</exception>
  public void Validate()
  {
    if (RelSecrets is null)
      throw new ArgumentException("RelSecrets config must not be null.", nameof(RelSecrets));
    if (NodeObjectives is null)
      throw new ArgumentException("NodeObjectives config must not be null.", nameof(NodeObjectives));
    if (NodeRelSize is null)
      throw new ArgumentException("NodeRelSize config must not be null.", nameof(NodeRelSize));
    if (NodeStyle is null)
      throw new ArgumentException("NodeStyle config must not be null.", nameof(NodeStyle));

    RelSecrets.Validate();
    NodeObjectives.Validate();
    NodeRelSize.Validate();
    NodeStyle.Validate();
  }
}
