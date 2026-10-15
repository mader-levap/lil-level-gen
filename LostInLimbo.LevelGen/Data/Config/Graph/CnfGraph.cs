using System;

namespace LostInLimbo.LevelGen.Data.Config.Graph;

/// <summary>
/// <para>Graph configuration data.</para>
/// <para>
/// Note: Some builders can ignore some configuration variables. Also it is up to builder to interpret
/// these variables, especially vaguer ones like Connectivity.
/// </para>
/// </summary>
public sealed record CnfGraph
{
  /// <summary>Default builder - simplest possible.</summary>
  public const EnGraphBuilder DefaultBuilder = EnGraphBuilder.OneNode;

  /// <summary>Default size - smallest possible.</summary>
  public const int DefaultSize = 1;

  /// <summary>Default connectivity - smallest possible.</summary>
  public const int DefaultConnectivity = 0;

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
  /// Size of graph. Usually just number of nodes. Soft cap - builder is allowed to add more nodes
  /// if small number of nodes makes it impossible to make graph while fulfilling other settings.
  /// </summary>
  public int Size { get; init; } = DefaultSize;

  /// <summary>
  /// General connectivity of graph. It is not amount of relationships between nodes, at least not directly.
  /// Zero simply means graph generator will try to make as few relationships as possible.
  /// </summary>
  public int Connectivity { get; init; } = DefaultConnectivity;

  /// <summary>
  /// General complexity of graph. Usually number of obstacles.
  /// </summary>
  public int Complexity { get; init; } = DefaultComplexity;

  //

  /// <summary>
  /// Checks that this config can be used for generation.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">The builder is not a defined enum value, or the complexity is out of range.</exception>
  public void Validate()
  {
    if (!Enum.IsDefined(typeof(EnGraphBuilder), Builder))
      throw new ArgumentOutOfRangeException(nameof(Builder), Builder, "Unknown graph builder.");
    if (Size < 1)
      throw new ArgumentOutOfRangeException(nameof(Size), Size, "Size must be at least 1.");
    if (Connectivity < 0)
      throw new ArgumentOutOfRangeException(nameof(Connectivity), Connectivity, "Connectivity must be at least 0.");
    if (Complexity < 0)
      throw new ArgumentOutOfRangeException(nameof(Complexity), Complexity, "Complexity must be at least 0.");
  }
}