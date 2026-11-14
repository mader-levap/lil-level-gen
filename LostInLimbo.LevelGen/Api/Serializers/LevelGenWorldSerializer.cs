using System;
using System.IO;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Internal.Serialization;

namespace LostInLimbo.LevelGen.Api.Serializers;

/// <summary>
/// Saves and loads a <see cref="LilWorld"/> in a compact binary format.
/// </summary>
/// <remarks>
/// <para>
/// Worlds get large once they carry a map, so they use a binary format instead of the JSON of
/// <see cref="LevelGenSerializer"/>. The format starts with the <c>"LILW"</c> signature and a
/// format version, followed by length-prefixed sections (world values, statistics, graph, map).
/// All values are little-endian, so a saved world loads identically on every platform and engine.
/// </para>
/// <para>
/// Only streams cross this boundary: opening files, and optionally wrapping the stream in a
/// compressing stream such as <see cref="System.IO.Compression.GZipStream"/>, stays with the
/// caller. Both methods leave the stream open; <see cref="Read"/> stops right after the world,
/// so other data may follow it in the same stream.
/// </para>
/// <para>
/// Deterministic: the same world always produces the same bytes. A file is accepted only by a
/// library with the same format version. Loading does not validate the graph, because worlds
/// that failed validation are kept for debugging too; run
/// <see cref="LevelGraphValidation.Validate"/> where a beatable graph is required.
/// </para>
/// </remarks>
public static class LevelGenWorldSerializer
{
  /// <summary>
  /// Writes the world into the stream.
  /// </summary>
  /// <param name="world">World to save.</param>
  /// <param name="stream">Writable stream; written from its current position and left open.</param>
  /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
  /// <exception cref="ArgumentException">
  /// The stream is not writable, or the world holds a <see langword="null"/> container
  /// (graph, node list, POI list, ...) or an undefined enum value.
  /// </exception>
  public static void Write(LilWorld world, Stream stream)
  {
    if (world is null) throw new ArgumentNullException(nameof(world));
    if (stream is null) throw new ArgumentNullException(nameof(stream));
    if (!stream.CanWrite) throw new ArgumentException("Stream must be writable.", nameof(stream));

    WorldBinaryWriter.Write(world, stream);
  }

  /// <summary>
  /// Reads a world from the stream.
  /// </summary>
  /// <param name="stream">Readable stream positioned at the start of a saved world; left open.</param>
  /// <returns>The loaded world.</returns>
  /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
  /// <exception cref="ArgumentException">The stream is not readable.</exception>
  /// <exception cref="InvalidDataException">
  /// The data is not a saved world, has another format version, is truncated or is corrupt.
  /// </exception>
  public static LilWorld Read(Stream stream)
  {
    if (stream is null) throw new ArgumentNullException(nameof(stream));
    if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));

    return WorldBinaryReader.Read(stream);
  }
}
