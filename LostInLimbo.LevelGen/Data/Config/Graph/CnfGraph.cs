using System;

namespace LostInLimbo.LevelGen.Data.Config.Graph;

/// <summary>
/// Graph configuration data.
/// </summary>
public sealed record CnfGraph
{
  /// <summary>Default builder - simplest possible.</summary>
  public const EnGraphBuilder DefaultBuilder = EnGraphBuilder.Simple;

  /// <summary>Default complexity - simplest possible.</summary>
  public const int DefaultComplexity = 0;

  //

  /// <summary>Default instance.</summary>
  public static CnfGraph Default { get; } = new();

  /// <summary>
  /// Graph builder to use.
  /// </summary>
  public EnGraphBuilder Builder { get; init; } = DefaultBuilder;

  /// <summary>
  /// General complexity of graph.
  /// </summary>
  public int Complexity { get; init; } = DefaultComplexity;

  //

  /// <summary>
  /// Checks that this config can be used for generation.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">Number is outside range.</exception>
  public void Validate()
  {
    if (!Enum.IsDefined(typeof(EnGraphBuilder), Builder))
      throw new ArgumentOutOfRangeException(nameof(Builder), Builder, "Unknown graph builder.");
    if (Complexity < 0)
      throw new ArgumentOutOfRangeException(nameof(Complexity), Complexity, "Complexity must be at least 0.");
  }
}