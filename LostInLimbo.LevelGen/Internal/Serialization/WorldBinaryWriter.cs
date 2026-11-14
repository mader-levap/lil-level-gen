using System;
using System.Collections.Generic;
using System.IO;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Data.Result.Graph;
using static LostInLimbo.LevelGen.Internal.Serialization.WorldBinaryFormat;

namespace LostInLimbo.LevelGen.Internal.Serialization;

/// <summary>
/// Writes a <see cref="LilWorld"/> in the binary world format (see <see cref="WorldBinaryFormat"/>).
/// </summary>
/// <remarks>
/// Deterministic: the same world always produces the same bytes. Reach entries are written sorted
/// by key, so the output never depends on dictionary insertion order. Each section payload is
/// buffered in memory so its length can precede it.
/// </remarks>
internal static class WorldBinaryWriter
{
  /// <summary>Parameter name reported for invalid world content.</summary>
  private const string WorldParam = "world";

  #region Container

  /// <summary>
  /// Writes the whole world: header, every section and the end marker. Leaves the stream open.
  /// </summary>
  /// <param name="world">World to write.</param>
  /// <param name="stream">Writable target stream.</param>
  /// <exception cref="ArgumentException">The world holds a null container or an undefined enum value.</exception>
  public static void Write(LilWorld world, Stream stream)
  {
    using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
    writer.Write(Magic);
    writer.Write(FormatVersion);

    var stats = Require(world.Stats, "Stats");
    var graph = Require(world.Graph, "Graph");
    Require(world.Map, "Map");
    WriteSection(writer, EnSection.World, payload => WriteEnum(payload, world.Phase, "Phase"));
    WriteSection(writer, EnSection.Stats, payload => payload.Write(stats.Steps));
    WriteSection(writer, EnSection.Graph, payload => WriteGraph(payload, graph));
    // LilMap is a placeholder without data: empty payload until the map model exists (with a
    // format version bump).
    WriteSection(writer, EnSection.Map, _ => { });
    writer.Write((byte)EnSection.End);
  }

  /// <summary>
  /// Writes one section: its id, the payload length and the payload produced by <paramref name="writePayload"/>.
  /// </summary>
  /// <param name="writer">Writer of the target stream.</param>
  /// <param name="section">Section to write.</param>
  /// <param name="writePayload">Writes the payload into the given buffer writer.</param>
  private static void WriteSection(BinaryWriter writer, EnSection section, Action<BinaryWriter> writePayload)
  {
    using var buffer = new MemoryStream();
    using (var payload = new BinaryWriter(buffer, Utf8, leaveOpen: true))
    {
      writePayload(payload);
    }

    writer.Write((byte)section);
    writer.Write((int)buffer.Length);
    writer.Write(buffer.GetBuffer(), 0, (int)buffer.Length);
  }

  #endregion

  #region Graph

  /// <summary>Writes the graph: node count and nodes, then edge count and edges.</summary>
  /// <param name="writer">Payload writer.</param>
  /// <param name="graph">Graph to write.</param>
  private static void WriteGraph(BinaryWriter writer, LilGraph graph)
  {
    var nodes = Require(graph.Nodes, "Graph.Nodes");
    writer.Write(nodes.Count);
    foreach (var node in nodes) WriteNode(writer, Require(node, "Graph.Nodes item"));

    var edges = Require(graph.Edges, "Graph.Edges");
    writer.Write(edges.Count);
    foreach (var edge in edges) WriteEdge(writer, Require(edge, "Graph.Edges item"));
  }

  /// <summary>Writes one node with its style, POIs and reach metadata.</summary>
  /// <param name="writer">Payload writer.</param>
  /// <param name="node">Node to write.</param>
  private static void WriteNode(BinaryWriter writer, LilNode node)
  {
    WriteGuid(writer, node.Id);
    writer.Write(node.RelSize);
    writer.Write(node.MaxEdges);
    WriteFlag(writer, node.Secret);
    WriteNullableString(writer, Require(node.Style, "LilNode.Style").Group);

    var pois = Require(node.Pois, "LilNode.Pois");
    writer.Write(pois.Count);
    foreach (var poi in pois)
    {
      var checkedPoi = Require(poi, "LilNode.Pois item");
      WriteGuid(writer, checkedPoi.Id);
      WriteEnum(writer, checkedPoi.Type, "LilPoi.Type");
      WriteNullableString(writer, checkedPoi.Name);
    }

    WriteReach(writer, Require(node.Reach, "LilNode.Reach"));
    WriteGuidList(writer, Require(node.OpenReach, "LilNode.OpenReach"));
  }

  /// <summary>Writes the reach map with entries sorted by key; each list keeps its order.</summary>
  /// <param name="writer">Payload writer.</param>
  /// <param name="reach">Reach map to write.</param>
  private static void WriteReach(BinaryWriter writer, Dictionary<Guid, List<Guid>> reach)
  {
    var keys = new List<Guid>(reach.Keys);
    keys.Sort();

    writer.Write(keys.Count);
    foreach (var key in keys)
    {
      WriteGuid(writer, key);
      WriteGuidList(writer, Require(reach[key], "LilNode.Reach list"));
    }
  }

  /// <summary>Writes one edge with its conditions.</summary>
  /// <param name="writer">Payload writer.</param>
  /// <param name="edge">Edge to write.</param>
  private static void WriteEdge(BinaryWriter writer, LilEdge edge)
  {
    WriteGuid(writer, edge.Id);
    WriteGuid(writer, edge.From);
    WriteGuid(writer, edge.To);
    WriteEnum(writer, edge.Direction, "LilEdge.Direction");
    WriteFlag(writer, edge.Chokepoint);
    WriteFlag(writer, edge.Secret);

    var conditions = Require(edge.Conditions, "LilEdge.Conditions");
    writer.Write(conditions.Count);
    foreach (var condition in conditions)
    {
      var checkedCondition = Require(condition, "LilEdge.Conditions item");
      WriteEnum(writer, checkedCondition.Type, "LilCondition.Type");
      WriteGuid(writer, checkedCondition.RequiresPoi);
    }
  }

  #endregion

  #region Primitives

  /// <summary>Writes a list of identifiers: count, then each identifier.</summary>
  /// <param name="writer">Payload writer.</param>
  /// <param name="ids">Identifiers to write.</param>
  private static void WriteGuidList(BinaryWriter writer, List<Guid> ids)
  {
    writer.Write(ids.Count);
    foreach (var id in ids) WriteGuid(writer, id);
  }

  /// <summary>Writes an identifier as its 16-byte representation.</summary>
  /// <param name="writer">Payload writer.</param>
  /// <param name="id">Identifier to write.</param>
  private static void WriteGuid(BinaryWriter writer, Guid id)
  {
    Span<byte> bytes = stackalloc byte[GuidSize];
    id.TryWriteBytes(bytes);
    writer.Write(bytes);
  }

  /// <summary>Writes a flag as one byte, <see cref="FlagFalse"/> or <see cref="FlagTrue"/>.</summary>
  /// <param name="writer">Payload writer.</param>
  /// <param name="value">Flag to write.</param>
  private static void WriteFlag(BinaryWriter writer, bool value)
  {
    writer.Write(value ? FlagTrue : FlagFalse);
  }

  /// <summary>Writes a presence flag, then the string when present.</summary>
  /// <param name="writer">Payload writer.</param>
  /// <param name="value">String to write, or <see langword="null"/>.</param>
  private static void WriteNullableString(BinaryWriter writer, string? value)
  {
    WriteFlag(writer, value is not null);
    if (value is not null) writer.Write(value);
  }

  /// <summary>Writes a defined enum value as one byte.</summary>
  /// <typeparam name="T">Enum type; its values fit into a byte.</typeparam>
  /// <param name="writer">Payload writer.</param>
  /// <param name="value">Value to write.</param>
  /// <param name="path">Location of the value, for the error message.</param>
  /// <exception cref="ArgumentException">The value is not a defined member.</exception>
  private static void WriteEnum<T>(BinaryWriter writer, T value, string path) where T : struct, Enum
  {
    if (!Enum.IsDefined(typeof(T), value))
      throw new ArgumentException($"World holds an undefined {typeof(T).Name} value {value} at {path}.", WorldParam);
    writer.Write(Convert.ToByte(value, System.Globalization.CultureInfo.InvariantCulture));
  }

  /// <summary>Returns the value or reports a null container in the world.</summary>
  /// <typeparam name="T">Reference type of the value.</typeparam>
  /// <param name="value">Value to check.</param>
  /// <param name="path">Location of the value, for the error message.</param>
  /// <returns>The non-null value.</returns>
  /// <exception cref="ArgumentException">The value is <see langword="null"/>.</exception>
  private static T Require<T>(T? value, string path) where T : class
  {
    return value ?? throw new ArgumentException($"World holds a null {path}.", WorldParam);
  }

  #endregion
}
