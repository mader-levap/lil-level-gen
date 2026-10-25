using System;

namespace LostInLimbo.LevelGen.Data.Config.Graph.Builder;

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

  /// <summary>
  /// Configuration for Simple graph builder.
  /// </summary>
  public CnfGraphBuilderSimple? CnfSimple { get; init; } = null;

  /// <summary>
  /// Configuration for Linear graph builder.
  /// </summary>
  public CnfGraphBuilderLinear? CnfLinear { get; init; } = null;

  /// <summary>
  /// 
  /// </summary>
  public void Validate()
  {
    if (!Enum.IsDefined(typeof(EnGraphBuilder), Builder))
      throw new ArgumentOutOfRangeException(nameof(Builder), Builder, "Unknown graph builder.");

    switch (Builder)
    {
      case EnGraphBuilder.Simple:
        if (CnfSimple is null)
          throw new ArgumentException("CnfSimple config must not be null.", nameof(CnfSimple));
        break;
      case EnGraphBuilder.Linear:
        if (CnfLinear is null)
          throw new ArgumentException("CnfLinear config must not be null.", nameof(CnfLinear));
        break;
      default:
        break;
    }
  }
}
