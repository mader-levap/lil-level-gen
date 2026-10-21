using System;

namespace LostInLimbo.LevelGen.Data.Config.Graph;

/// <summary>
/// <para>Graph configuration data.</para>
/// </summary>
/// <remarks>
/// <para>
/// Many parameters here are soft constraints. Builders are allowed to reject them to a degree to minimize
/// amount of failed builds. Also it is up to builder to interpret these variables, especially vaguer ones
/// like Connectivity.
/// </para>
/// </remarks>
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

  /// <summary>Maximum allowed size - keeps generation cost bounded.</summary>
  public const int MaxSize = 100;

  //

  /// <summary>Default instance.</summary>
  public static CnfGraph Default { get; } = new();

  /// <summary>
  /// Graph builder to use.
  /// </summary>
  public EnGraphBuilder Builder { get; init; } = DefaultBuilder;

  /// <summary>
  /// Size of graph. Usually just number of nodes. Hard cap is <see cref="MaxSize"/>.
  /// </summary>
  public int Size { get; init; } = DefaultSize;

  /// <summary>
  /// General connectivity of graph. It is not amount of relationships between nodes, at least not directly.
  /// Zero simply means graph generator will try to make as few relationships as possible.
  /// </summary>
  public int Connectivity { get; init; } = DefaultConnectivity;

  /// <summary>
  /// General complexity of graph. Usually number of obstacles. Ignored by builders that do not make obstacles.
  /// </summary>
  public int Complexity { get; init; } = DefaultComplexity;

  /// <summary>
  /// Options of the graph finalisation processors. Ignored by builders that run no processors.
  /// </summary>
  public CnfProcOpts ProcOpts { get; init; } = CnfProcOpts.Default;

  //

  /// <summary>
  /// Checks that this config can be used for generation.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">The builder is not a defined enum value, or a value is out of range.</exception>
  /// <exception cref="ArgumentException">ProcOpts is null, or its values are invalid.</exception>
  public void Validate()
  {
    if (!Enum.IsDefined(typeof(EnGraphBuilder), Builder))
      throw new ArgumentOutOfRangeException(nameof(Builder), Builder, "Unknown graph builder.");
    if (Size < 1)
      throw new ArgumentOutOfRangeException(nameof(Size), Size, "Size must be at least 1.");
    if (Size > MaxSize)
      throw new ArgumentOutOfRangeException(nameof(Size), Size, $"Size must not exceed {MaxSize}.");
    if (Connectivity < 0)
      throw new ArgumentOutOfRangeException(nameof(Connectivity), Connectivity, "Connectivity must be at least 0.");
    if (Complexity < 0)
      throw new ArgumentOutOfRangeException(nameof(Complexity), Complexity, "Complexity must be at least 0.");
    if (ProcOpts is null)
      throw new ArgumentException("ProcOpts config must not be null.", nameof(ProcOpts));
    ProcOpts.Validate();
  }
}