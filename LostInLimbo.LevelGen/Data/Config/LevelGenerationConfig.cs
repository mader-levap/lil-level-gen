using System;

namespace LostInLimbo.LevelGen.Data.Config;

/// <summary>
/// Configuration for generating a level: the seed and the layout knobs that feed the
/// generation passes. This is the root of the JSON configuration files.
/// </summary>
/// <remarks>
/// Pure data; no randomness is performed here. Every per-chunk random generator is derived
/// deterministically from <see cref="Seed"/>, so the same seed with the same configuration
/// always produces the same level. The shape maps one-to-one to JSON via
/// <c>LostInLimbo.LevelGen.Api.LevelGenerationConfigSerializer</c>; future settings are added
/// as new nested section records to keep the root small as the number of knobs grows.
/// </remarks>
public sealed record LevelGenerationConfig
{
  /// <summary>Version of the configuration format currently written by the serializer.</summary>
  public const int CurrentConfigVersion = 1;

  /// <summary>
  /// Schema version of this configuration. Increment when the JSON format changes in way that
  /// introduces incompatibility so that older files can be detected and migrated.
  /// </summary>
  public int ConfigVersion { get; init; } = CurrentConfigVersion;

  /// <summary>
  /// World seed from which all per-chunk random generators are derived deterministically.
  /// Two runs with the same seed and configuration produce identical output.
  /// </summary>
  public long Seed { get; init; }

  /// <summary>Size of the whole level, in chunks.</summary>
  public LevelSize LevelSize { get; init; } = LevelSize.Default;

  /// <summary>Size of a single chunk, in cells.</summary>
  public ChunkSize ChunkSize { get; init; } = ChunkSize.Default;

  /// <summary>
  /// Validates every invariant of this configuration, including its sections.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">A numeric setting is out of range.</exception>
  /// <exception cref="ArgumentException">A section reference is <see langword="null"/>.</exception>
  public void Validate()
  {
    if (ConfigVersion < 1)
      throw new ArgumentOutOfRangeException(nameof(ConfigVersion), ConfigVersion, "Config version must be at least 1.");

    if (LevelSize is null)
      throw new ArgumentException("Level size must not be null.", nameof(LevelSize));

    if (ChunkSize is null)
      throw new ArgumentException("Chunk size must not be null.", nameof(ChunkSize));

    LevelSize.Validate();
    ChunkSize.Validate();
  }
}
