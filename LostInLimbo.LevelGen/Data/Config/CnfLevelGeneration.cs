using System;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Map;
using LostInLimbo.LevelGen.Data.Config.Style;

namespace LostInLimbo.LevelGen.Data.Config;

/// <summary>
/// <para>
/// Very detailed configuration for generating a level. This is the lowest level. In the future, you will be
/// able to generate this config using less detailed settings.
/// </para>
/// <para>This is the root of the JSON configuration file.</para>
/// </summary>
/// <remarks>
/// <para>The same seed with the same configuration always produces the same level. The shape maps one-to-one to
/// JSON via <c>LostInLimbo.LevelGen.Api.LevelGenerationSerializer</c>.</para>
/// </remarks>
public sealed record CnfLevelGeneration
{
  /// <summary>Version of the configuration format currently written by the serializer.</summary>
  public const int CurrentConfigVersion = 1;

  // Defaults.

  /// <summary>Default instance.</summary>
  public static CnfLevelGeneration Default { get; } = new();

  // Fields.

  /// <summary>
  /// Schema version of this configuration. Increment when the JSON format changes in a way that
  /// introduces incompatibility so that older files can be detected and migrated.
  /// </summary>
  public int ConfigVersion { get; init; } = CurrentConfigVersion;

  /// <summary>General configuration.</summary>
  public CnfGeneral General { get; init; } = CnfGeneral.Default;

  /// <summary>Style configuration.</summary>
  public CnfStyle Style { get; init; } = CnfStyle.Default;

  /// <summary>Graph configuration.</summary>
  public CnfGraph Graph { get; init; } = CnfGraph.Default;

  /// <summary>Map configuration.</summary>
  public CnfMap Map { get; init; } = CnfMap.Default;

  /// <summary>Debug configuration. Ignored if null.</summary>
  public CnfDebug? Debug { get; init; } = null;

  // Helpers.

  /// <summary>
  /// Validates the invariants of this configuration and its general, graph and debug sections.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">A numeric setting is out of range, or a section carries an invalid value.</exception>
  /// <exception cref="ArgumentException">The <see cref="General"/> or <see cref="Graph"/> section is <see langword="null"/>.</exception>
  public void Validate()
  {
    if (ConfigVersion < 1)
      throw new ArgumentOutOfRangeException(nameof(ConfigVersion), ConfigVersion, "Config version must be at least 1.");
    if (General is null)
      throw new ArgumentException("General config must not be null.", nameof(General));
    if (Style is null)
      throw new ArgumentException("Style config must not be null.", nameof(Style));
    if (Graph is null)
      throw new ArgumentException("Graph config must not be null.", nameof(Graph));
    if (Map is null)
      throw new ArgumentException("Map config must not be null.", nameof(Map));
    // Debug can be null.

    General.Validate();
    Style.Validate();
    Graph.Validate();
    Map.Validate();
    Debug?.Validate();
  }
}
