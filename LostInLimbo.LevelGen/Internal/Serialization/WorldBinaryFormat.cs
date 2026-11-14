using System.Text;

namespace LostInLimbo.LevelGen.Internal.Serialization;

/// <summary>
/// Constants of the binary world format shared by <see cref="WorldBinaryWriter"/> and
/// <see cref="WorldBinaryReader"/>.
/// </summary>
/// <remarks>
/// <para>Layout (all multi-byte values little-endian, as <c>BinaryWriter</c> always writes):</para>
/// <list type="bullet">
/// <item>Header: <see cref="Magic"/> (4 bytes), <see cref="FormatVersion"/> (<c>ushort</c>).</item>
/// <item>Sections, each exactly once and in <see cref="EnSection"/> order: section id (<c>byte</c>),
/// payload length in bytes (<c>int</c>), payload.</item>
/// <item>End marker: <see cref="EnSection.End"/> (<c>byte</c>).</item>
/// </list>
/// <para>
/// Any change to the layout or to a payload requires bumping <see cref="FormatVersion"/>; readers
/// accept only their own version.
/// </para>
/// </remarks>
internal static class WorldBinaryFormat
{
  /// <summary>File signature: ASCII <c>"LILW"</c>.</summary>
  public static readonly byte[] Magic = [0x4C, 0x49, 0x4C, 0x57];

  /// <summary>Version of the format written and accepted by this library.</summary>
  public const ushort FormatVersion = 1;

  /// <summary>Size of a serialized <see cref="System.Guid"/> in bytes.</summary>
  public const int GuidSize = 16;

  /// <summary>
  /// Largest initial capacity reserved for a list read from data. Larger lists still load, but
  /// grow as elements actually arrive, so a corrupt count cannot force a huge allocation.
  /// </summary>
  public const int MaxInitialCapacity = 1024;

  /// <summary>Byte value of a <see langword="false"/> flag.</summary>
  public const byte FlagFalse = 0;

  /// <summary>Byte value of a <see langword="true"/> flag.</summary>
  public const byte FlagTrue = 1;

  /// <summary>
  /// UTF-8 without byte order mark that throws on invalid bytes, so corrupt strings are reported
  /// instead of silently replaced.
  /// </summary>
  public static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

  /// <summary>Section identifiers, in the order the sections appear in the data.</summary>
  public enum EnSection : byte
  {
    /// <summary>End marker after the last section; carries no length or payload.</summary>
    End = 0,
    /// <summary>World-level values: <c>LilWorld.Phase</c>.</summary>
    World = 1,
    /// <summary>Statistics: <c>LilStats.Steps</c>.</summary>
    Stats = 2,
    /// <summary>Level graph: nodes with their POIs and reach metadata, then edges.</summary>
    Graph = 3,
    /// <summary>Map; empty payload until <c>LilMap</c> carries data.</summary>
    Map = 4,
  }
}
