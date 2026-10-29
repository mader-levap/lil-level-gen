using System;
using LostInLimbo.LevelGen.Data.Config;

namespace LostInLimbo.LevelGen.Data.Prop;

/// <summary>
/// <para>
/// Main property class. Data here will be used to generate full configuration.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Same properties can and WILL produce different configuration, and so different level.
/// </para>
/// <para>
/// This is the root of the JSON configuration file. The shape maps one-to-one to JSON via <see cref="LevelGenerationSerializer"/>.
/// </para>
/// </remarks>
public sealed record PropMain
{
  // Defaults.

  /// <summary>Default instance.</summary>
  public static PropMain Default { get; } = new();

  // Fields.

  /// <summary>
  /// Schema version of this configuration. Increment when the JSON format changes in a way that
  /// introduces incompatibility so that older files can be detected and migrated.
  /// </summary>
  public int ConfigVersion { get; init; } = CnfLevelGeneration.CurrentConfigVersion;

  /// <summary>
  /// Provided text seed that will be hashed into actual seed. If null, empty or whitespace,
  /// current time is used as seed.
  /// </summary>
  public string? TextSeed { get; init; } = null;

  /// <summary>
  /// Difficulty. In game these would be descriptive texts like "easy" or "hard", but here we represent it as number. Smallest is 0.
  /// </summary>
  public int Difficulty { get; init; } = 0;

  // Helpers.

  /// <summary>
  /// Validates the invariants of these properties.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">A numeric setting is out of range, or a section carries an invalid value.</exception>
  /// <exception cref="ArgumentException">A section is <see langword="null"/>.</exception>
  public void Validate()
  {
    if (ConfigVersion < 1)
      throw new ArgumentOutOfRangeException(nameof(ConfigVersion), ConfigVersion, "Config version must be at least 1.");

    if (Difficulty < 0)
      throw new ArgumentOutOfRangeException(nameof(ConfigVersion), ConfigVersion, "Difficulty must be at least 0.");

    // Validate all sections.
    // PLACEHOLDER
  }
}