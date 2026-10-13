using System;

namespace LostInLimbo.LevelGen.Data.Config;

/// <summary>
/// Configuration for generating a level. This is the root of the JSON configuration files.
/// </summary>
/// <remarks>
/// Same seed with the same configuration always produces the same level. The shape maps one-to-one to JSON
/// via <c>LostInLimbo.LevelGen.Api.LevelGenerationSerializer</c>.
/// </remarks>
public sealed record CnfLevelGeneration
{
  /// <summary>Version of the configuration format currently written by the serializer.</summary>
  public const int CurrentConfigVersion = 1;

  //

  /// <summary>Default instance.</summary>
  public static CnfLevelGeneration Default { get; } = new();

  /// <summary>
  /// Schema version of this configuration. Increment when the JSON format changes in way that
  /// introduces incompatibility so that older files can be detected and migrated.
  /// </summary>
  public int ConfigVersion { get; init; } = CurrentConfigVersion;

  /// <summary>General configuration.</summary>
  public CnfGeneral General { get; init; } = CnfGeneral.Default;

  /// <summary>Debug configuration. Ignored if null.</summary>
  public CnfDebug? Debug { get; init; } = null;

  //

  /// <summary>
  /// Validates every invariant of this configuration, including its sections.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">A numeric setting is out of range.</exception>
  /// <exception cref="ArgumentException">A section reference is <see langword="null"/>.</exception>
  public void Validate()
  {
    if (ConfigVersion < 1)
      throw new ArgumentOutOfRangeException(nameof(ConfigVersion), ConfigVersion, "Config version must be at least 1.");

    if (General is null)
      throw new ArgumentException("General config must not be null.", nameof(General));

    General.Validate();
  }
}
