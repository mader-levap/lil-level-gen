using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Data.Result.Stats;
using static LostInLimbo.LevelGen.Internal.Serialization.WorldBinaryFormat;

namespace LostInLimbo.LevelGen.Internal.Serialization;

/// <summary>
/// Reads a <see cref="LilWorld"/> written in the binary world format (see <see cref="WorldBinaryFormat"/>).
/// </summary>
/// <remarks>
/// Strict: the signature and the format version must match, every section must appear exactly
/// once in order and consume exactly its declared length, and every count, flag and enum value
/// must be valid. Any violation is reported as <see cref="InvalidDataException"/>; graph
/// semantics (references, beatability) are not checked.
/// </remarks>
internal static class WorldBinaryReader
{
  #region Container

  /// <summary>
  /// Reads a whole world and stops right after its end marker. Leaves the stream open.
  /// </summary>
  /// <param name="stream">Readable source stream.</param>
  /// <returns>The world read from the stream.</returns>
  /// <exception cref="InvalidDataException">The data is not a valid saved world of this format version.</exception>
  public static LilWorld Read(Stream stream)
  {
    try
    {
      return ReadWorld(stream);
    }
    catch (EndOfStreamException exception)
    {
      throw Fail("the data ends unexpectedly.", exception);
    }
    catch (FormatException exception)
    {
      throw Fail("a string length is malformed.", exception);
    }
    catch (DecoderFallbackException exception)
    {
      throw Fail("a string is not valid UTF-8.", exception);
    }
  }

  /// <summary>Reads the header, every section in order and the end marker.</summary>
  /// <param name="stream">Readable source stream.</param>
  /// <returns>The world read from the stream.</returns>
  private static LilWorld ReadWorld(Stream stream)
  {
    using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
    ReadHeader(reader);

    var world = new LilWorld();
    ReadSection(reader, EnSection.World, payload => world.Phase = ReadEnum<EnPhase>(payload, "phase"));
    ReadSection(reader, EnSection.Stats, payload => world.Stats.Steps = payload.ReadInt64());
    ReadSection(reader, EnSection.Graph, payload => world.Graph = ReadGraph(payload));
    // LilMap is a placeholder without data: its payload is empty.
    ReadSection(reader, EnSection.Map, _ => { });

    var end = reader.ReadByte();
    if (end != (byte)EnSection.End) throw Fail($"expected the end marker, found section id {end}.");
    return world;
  }

  /// <summary>Checks the signature and the format version.</summary>
  /// <param name="reader">Reader of the source stream.</param>
  private static void ReadHeader(BinaryReader reader)
  {
    Span<byte> magic = stackalloc byte[Magic.Length];
    ReadExactly(reader, magic);
    if (!magic.SequenceEqual(Magic)) throw Fail("the signature does not match; the data is not a saved world.");

    var version = reader.ReadUInt16();
    if (version != FormatVersion)
      throw Fail($"format version {version} is not supported; this library reads only version {FormatVersion}.");
  }

  /// <summary>
  /// Reads one section: checks its id and length, then reads its payload through a view bounded
  /// to that length and requires the payload to consume it exactly.
  /// </summary>
  /// <param name="reader">Reader of the source stream.</param>
  /// <param name="expected">Section that must come next.</param>
  /// <param name="readPayload">Reads the payload from the given bounded reader.</param>
  private static void ReadSection(BinaryReader reader, EnSection expected, Action<BinaryReader> readPayload)
  {
    var id = reader.ReadByte();
    if (id != (byte)expected)
    {
      throw Fail(Enum.IsDefined(typeof(EnSection), id)
        ? $"expected section {expected}, found section {(EnSection)id}."
        : $"unknown section id {id} where section {expected} was expected.");
    }

    var length = reader.ReadInt32();
    if (length < 0) throw Fail($"section {expected} has a negative length {length}.");

    using var bounded = new BoundedReadStream(reader.BaseStream, length);
    using var payload = new BinaryReader(bounded, Utf8, leaveOpen: true);
    try
    {
      readPayload(payload);
    }
    catch (EndOfStreamException exception) when (bounded.Remaining == 0)
    {
      // The bound was hit, not the end of the data: the declared length is too short.
      throw Fail($"section {expected} is shorter than its content.", exception);
    }

    if (bounded.Remaining != 0)
      throw Fail($"section {expected} has {bounded.Remaining} unread bytes; its length does not match its content.");
  }

  #endregion

  #region Graph

  /// <summary>Reads the graph: nodes, then edges.</summary>
  /// <param name="reader">Payload reader.</param>
  /// <returns>The graph.</returns>
  private static LilGraph ReadGraph(BinaryReader reader)
  {
    var nodeCount = ReadCount(reader, "node");
    var nodes = new List<LilNode>(Capacity(nodeCount));
    for (var i = 0; i < nodeCount; i++) nodes.Add(ReadNode(reader));

    var edgeCount = ReadCount(reader, "edge");
    var edges = new List<LilEdge>(Capacity(edgeCount));
    for (var i = 0; i < edgeCount; i++) edges.Add(ReadEdge(reader));

    return new LilGraph { Nodes = nodes, Edges = edges };
  }

  /// <summary>Reads one node with its style, POIs and reach metadata.</summary>
  /// <param name="reader">Payload reader.</param>
  /// <returns>The node.</returns>
  private static LilNode ReadNode(BinaryReader reader)
  {
    var id = ReadGuid(reader);
    var relSize = reader.ReadInt32();
    var maxEdges = reader.ReadInt32();
    var secret = ReadFlag(reader, "node secret");
    var group = ReadNullableString(reader, "style group");

    var poiCount = ReadCount(reader, "POI");
    var pois = new List<LilPoi>(Capacity(poiCount));
    for (var i = 0; i < poiCount; i++)
    {
      var poiId = ReadGuid(reader);
      var type = ReadEnum<EnPoiType>(reader, "POI type");
      pois.Add(new LilPoi { Id = poiId, Type = type, Name = ReadNullableString(reader, "POI name") });
    }

    var reach = ReadReach(reader);
    return new LilNode
    {
      Id = id,
      RelSize = relSize,
      MaxEdges = maxEdges,
      Secret = secret,
      Style = new LilStyle { Group = group },
      Pois = pois,
      Reach = reach,
      OpenReach = ReadGuidList(reader, "open reach"),
    };
  }

  /// <summary>Reads the reach map; every key must be unique.</summary>
  /// <param name="reader">Payload reader.</param>
  /// <returns>The reach map.</returns>
  private static Dictionary<Guid, List<Guid>> ReadReach(BinaryReader reader)
  {
    var count = ReadCount(reader, "reach entry");
    var reach = new Dictionary<Guid, List<Guid>>(Capacity(count));
    for (var i = 0; i < count; i++)
    {
      var key = ReadGuid(reader);
      if (!reach.TryAdd(key, ReadGuidList(reader, "reach")))
        throw Fail($"reach key {key} appears twice in one node.");
    }

    return reach;
  }

  /// <summary>Reads one edge with its conditions.</summary>
  /// <param name="reader">Payload reader.</param>
  /// <returns>The edge.</returns>
  private static LilEdge ReadEdge(BinaryReader reader)
  {
    var id = ReadGuid(reader);
    var from = ReadGuid(reader);
    var to = ReadGuid(reader);
    var direction = ReadEnum<EnPassageDirection>(reader, "passage direction");
    var chokepoint = ReadFlag(reader, "edge chokepoint");
    var secret = ReadFlag(reader, "edge secret");

    var conditionCount = ReadCount(reader, "condition");
    var conditions = new List<LilCondition>(Capacity(conditionCount));
    for (var i = 0; i < conditionCount; i++)
    {
      var type = ReadEnum<EnConditionType>(reader, "condition type");
      conditions.Add(new LilCondition { Type = type, RequiresPoi = ReadGuid(reader) });
    }

    return new LilEdge
    {
      Id = id,
      From = from,
      To = to,
      Direction = direction,
      Chokepoint = chokepoint,
      Secret = secret,
      Conditions = conditions,
    };
  }

  #endregion

  #region Primitives

  /// <summary>Reads a list of identifiers: count, then each identifier.</summary>
  /// <param name="reader">Payload reader.</param>
  /// <param name="what">Name of the list, for error messages.</param>
  /// <returns>The identifiers in stored order.</returns>
  private static List<Guid> ReadGuidList(BinaryReader reader, string what)
  {
    var count = ReadCount(reader, what);
    var ids = new List<Guid>(Capacity(count));
    for (var i = 0; i < count; i++) ids.Add(ReadGuid(reader));
    return ids;
  }

  /// <summary>Reads an element count; it must not be negative.</summary>
  /// <param name="reader">Payload reader.</param>
  /// <param name="what">Name of the counted elements, for error messages.</param>
  /// <returns>The count.</returns>
  private static int ReadCount(BinaryReader reader, string what)
  {
    var count = reader.ReadInt32();
    if (count < 0) throw Fail($"the {what} count {count} is negative.");
    return count;
  }

  /// <summary>Initial list capacity for a count read from data, capped at <see cref="MaxInitialCapacity"/>.</summary>
  /// <param name="count">Count read from data.</param>
  /// <returns>Capacity to reserve.</returns>
  private static int Capacity(int count) => Math.Min(count, MaxInitialCapacity);

  /// <summary>Reads an identifier from its 16-byte representation.</summary>
  /// <param name="reader">Payload reader.</param>
  /// <returns>The identifier.</returns>
  private static Guid ReadGuid(BinaryReader reader)
  {
    Span<byte> bytes = stackalloc byte[GuidSize];
    ReadExactly(reader, bytes);
    return new Guid(bytes);
  }

  /// <summary>Reads a flag byte; only <see cref="FlagFalse"/> and <see cref="FlagTrue"/> are valid.</summary>
  /// <param name="reader">Payload reader.</param>
  /// <param name="what">Name of the flag, for error messages.</param>
  /// <returns>The flag.</returns>
  private static bool ReadFlag(BinaryReader reader, string what)
  {
    var value = reader.ReadByte();
    return value switch
    {
      FlagFalse => false,
      FlagTrue => true,
      _ => throw Fail($"the {what} flag byte {value} is neither {FlagFalse} nor {FlagTrue}."),
    };
  }

  /// <summary>Reads a presence flag, then the string when present.</summary>
  /// <param name="reader">Payload reader.</param>
  /// <param name="what">Name of the string, for error messages.</param>
  /// <returns>The string, or <see langword="null"/> when absent.</returns>
  private static string? ReadNullableString(BinaryReader reader, string what)
  {
    return ReadFlag(reader, what + " presence") ? reader.ReadString() : null;
  }

  /// <summary>Reads an enum byte; it must be a defined member.</summary>
  /// <typeparam name="T">Enum type.</typeparam>
  /// <param name="reader">Payload reader.</param>
  /// <param name="what">Name of the value, for error messages.</param>
  /// <returns>The enum value.</returns>
  private static T ReadEnum<T>(BinaryReader reader, string what) where T : struct, Enum
  {
    var raw = reader.ReadByte();
    var value = (T)Enum.ToObject(typeof(T), raw);
    if (!Enum.IsDefined(typeof(T), value)) throw Fail($"the {what} value {raw} is not a defined {typeof(T).Name}.");
    return value;
  }

  /// <summary>Fills the buffer completely from the reader.</summary>
  /// <param name="reader">Reader to read from.</param>
  /// <param name="buffer">Buffer to fill.</param>
  /// <exception cref="EndOfStreamException">The data ends before the buffer is full.</exception>
  private static void ReadExactly(BinaryReader reader, Span<byte> buffer)
  {
    var filled = 0;
    while (filled < buffer.Length)
    {
      var read = reader.Read(buffer.Slice(filled));
      if (read == 0) throw new EndOfStreamException();
      filled += read;
    }
  }

  /// <summary>Creates the exception reporting invalid data.</summary>
  /// <param name="reason">What is wrong, as a sentence fragment ending with a period.</param>
  /// <param name="inner">Underlying exception, if any.</param>
  /// <returns>The exception to throw.</returns>
  private static InvalidDataException Fail(string reason, Exception? inner = null)
  {
    return new InvalidDataException($"Invalid saved world: {reason}", inner);
  }

  #endregion
}
