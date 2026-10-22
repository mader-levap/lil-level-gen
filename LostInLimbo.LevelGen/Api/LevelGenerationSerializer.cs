using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using LostInLimbo.LevelGen.Data.Config;

namespace LostInLimbo.LevelGen.Api;

/// <summary>
/// Serializes and deserializes <see cref="CnfLevelGeneration"/> to and from JSON strings.
/// </summary>
/// <remarks>
/// This type defines the single canonical JSON format shared by all consumers (editor, game):
/// camelCase property names, indented output for hand-editable files. Reading is case-insensitive
/// to be forgiving of hand-written files, but everything this type writes is camelCase.
/// Enum values cross this boundary as PascalCase member names (e.g. <c>"Linear"</c>); numeric
/// enum values are rejected during parsing so a file can never depend on the order of enum members.
/// Only strings cross this boundary; file reading and writing stay with the callers.
/// </remarks>
public static class LevelGenerationSerializer
{
  /// <summary>Serializer options shared by reading and writing, so the format stays consistent.</summary>
  private static readonly JsonSerializerOptions Options = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
  };

  /// <summary>
  /// Serializes a configuration into canonical JSON.
  /// </summary>
  /// <param name="config">Configuration to write. Validated before writing.</param>
  /// <returns>JSON text representing <paramref name="config"/>.</returns>
  /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
  /// <exception cref="ArgumentException">The configuration violates its own invariants.</exception>
  public static string SerializeConfig(CnfLevelGeneration config)
  {
    if (config is null) throw new ArgumentNullException(nameof(config));

    config.Validate();

    return JsonSerializer.Serialize(config, Options);
  }

  /// <summary>
  /// Deserializes canonical JSON into a configuration and validates the result.
  /// </summary>
  /// <param name="json">JSON text previously produced by <see cref="SerializeConfig"/> or written by hand.</param>
  /// <returns>The deserialized, validated configuration. Properties missing from the JSON keep their defaults.</returns>
  /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
  /// <exception cref="ArgumentException">
  /// The JSON is empty, malformed, describes a newer <c>ConfigVersion</c> than this library
  /// supports, or violates configuration invariants.
  /// </exception>
  public static CnfLevelGeneration DeserializeConfig(string json)
  {
    if (json is null) throw new ArgumentNullException(nameof(json));

    if (string.IsNullOrWhiteSpace(json))
      throw new ArgumentException("JSON must not be empty.", nameof(json));

    CnfLevelGeneration? config;
    try
    {
      config = JsonSerializer.Deserialize<CnfLevelGeneration>(json, Options);
    }
    catch (JsonException exception)
    {
      throw new ArgumentException(
        $"JSON is not a valid {nameof(CnfLevelGeneration)}: {exception.Message}",
        nameof(json),
        exception);
    }

    if (config is null)
      throw new ArgumentException("JSON deserialized to null.", nameof(json));

    if (config.ConfigVersion != CnfLevelGeneration.CurrentConfigVersion)
    {
      throw new ArgumentException(
        $"Config version {config.ConfigVersion} is different than the supported version " +
        $"{CnfLevelGeneration.CurrentConfigVersion}. Update the library or the config file.",
        nameof(json));
    }

    config.Validate();
    return config;
  }
}
