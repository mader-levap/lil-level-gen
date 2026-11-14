using System.Buffers.Binary;
using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Api.Serializers;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Data.Result.Stats;
using LostInLimbo.LevelGen.Internal.Generator.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Prepass;

namespace LostInLimbo.LevelGen.Tests.Api.Serializers;

/// <summary>
/// Tests the binary world format of <see cref="LevelGenWorldSerializer"/>: lossless round trips of
/// generated worlds, deterministic bytes, the stream contract and rejection of invalid data.
/// </summary>
public sealed class LevelGenWorldSerializerTests
{
  #region Offsets

  // Byte offsets in the data of a world (see WorldBinaryFormat): header, then sections with
  // a 1-byte id and a 4-byte payload length.

  /// <summary>Offset of the format version (after the 4-byte signature).</summary>
  private const int VersionOffset = 4;
  /// <summary>Offset of the World section id.</summary>
  private const int WorldSectionOffset = 6;
  /// <summary>Offset of the World section payload length.</summary>
  private const int WorldLengthOffset = 7;
  /// <summary>Offset of the phase byte (World section payload).</summary>
  private const int PhaseOffset = 11;
  /// <summary>Offset of the Stats section id.</summary>
  private const int StatsSectionOffset = 12;
  /// <summary>Offset of the node count (first value of the Graph section payload).</summary>
  private const int NodeCountOffset = 30;
  /// <summary>Offset of the first node's secret flag (after its id, RelSize and MaxEdges).</summary>
  private const int FirstNodeSecretOffset = 58;

  #endregion

  #region Helpers

  /// <summary>Generates a world with a filled graph (including reach metadata) from a text seed.</summary>
  private static LilWorld GenerateWorld(string textSeed, int difficulty)
  {
    var config = LevelGenConfig.Execute(new PropMain { TextSeed = textSeed, Difficulty = difficulty });
    var world = new LilWorld { Phase = EnPhase.Map };
    world.Stats.Steps = 7;
    new GraphGenerator(config, new PrepassGenerator(config).Create(), world).Execute();
    return world;
  }

  /// <summary>Creates a small hand-built world exercising every serialized field.</summary>
  private static LilWorld CreateTinyWorld()
  {
    var nodeA = new Guid("00000000-0000-0000-0000-00000000000a");
    var nodeB = new Guid("00000000-0000-0000-0000-00000000000b");
    var poiStart = new Guid("00000000-0000-0000-0000-000000000001");
    var poiKey = new Guid("00000000-0000-0000-0000-000000000002");

    var world = new LilWorld { Phase = EnPhase.Graph };
    world.Stats.Steps = 3;
    world.Graph.Nodes.Add(new LilNode
    {
      Id = nodeA,
      RelSize = 2,
      MaxEdges = -1,
      Style = new LilStyle { Group = "g1" },
      Pois = [new LilPoi { Id = poiStart, Type = EnPoiType.Start }],
      Reach = { [poiStart] = [nodeA] },
      OpenReach = [nodeA],
    });
    world.Graph.Nodes.Add(new LilNode
    {
      Id = nodeB,
      RelSize = 1,
      MaxEdges = 4,
      Secret = true,
      Pois = [new LilPoi { Id = poiKey, Type = EnPoiType.Solution, Name = "key" }],
    });
    world.Graph.Edges.Add(new LilEdge
    {
      Id = new Guid("00000000-0000-0000-0000-0000000000e1"),
      From = nodeA,
      To = nodeB,
      Direction = EnPassageDirection.OneWay,
      Chokepoint = true,
      Conditions = [new LilCondition { Type = EnConditionType.RequiresSolution, RequiresPoi = poiKey }],
    });
    return world;
  }

  /// <summary>Saves the world into a byte array.</summary>
  private static byte[] Save(LilWorld world)
  {
    using var stream = new MemoryStream();
    LevelGenWorldSerializer.Write(world, stream);
    return stream.ToArray();
  }

  /// <summary>Loads a world from a byte array.</summary>
  private static LilWorld Load(byte[] data)
  {
    using var stream = new MemoryStream(data);
    return LevelGenWorldSerializer.Read(stream);
  }

  /// <summary>Asserts that two worlds carry the same data; reach maps compare as key-to-list maps.</summary>
  private static void AssertSameWorld(LilWorld expected, LilWorld actual)
  {
    Assert.Equal(expected.Phase, actual.Phase);
    Assert.Equal(expected.Stats.Steps, actual.Stats.Steps);
    Assert.Equal(expected.Graph.Nodes.Count, actual.Graph.Nodes.Count);
    for (var i = 0; i < expected.Graph.Nodes.Count; i++)
    {
      var left = expected.Graph.Nodes[i];
      var right = actual.Graph.Nodes[i];
      // Records compare list members by reference, so scalars and lists are compared separately.
      Assert.Equal(left.Id, right.Id);
      Assert.Equal(left.RelSize, right.RelSize);
      Assert.Equal(left.MaxEdges, right.MaxEdges);
      Assert.Equal(left.Secret, right.Secret);
      Assert.Equal(left.Style, right.Style);
      Assert.Equal(left.Pois, right.Pois);
      Assert.Equal(left.OpenReach, right.OpenReach);
      Assert.Equal(left.Reach.Keys.Order(), right.Reach.Keys.Order());
      foreach (var key in left.Reach.Keys) Assert.Equal(left.Reach[key], right.Reach[key]);
    }

    Assert.Equal(expected.Graph.Edges.Count, actual.Graph.Edges.Count);
    for (var i = 0; i < expected.Graph.Edges.Count; i++)
    {
      var left = expected.Graph.Edges[i];
      var right = actual.Graph.Edges[i];
      Assert.Equal(left.Id, right.Id);
      Assert.Equal(left.From, right.From);
      Assert.Equal(left.To, right.To);
      Assert.Equal(left.Direction, right.Direction);
      Assert.Equal(left.Chokepoint, right.Chokepoint);
      Assert.Equal(left.Secret, right.Secret);
      Assert.Equal(left.Conditions, right.Conditions);
    }
  }

  #endregion

  #region Tests

  /// <summary>Tests of lossless save/load round trips.</summary>
  public sealed class RoundTripTests
  {
    [Theory]
    [InlineData("mordor", 1)]
    [InlineData("mordor", 7)]
    [InlineData("gondor", 4)]
    [InlineData("rohan", 10)]
    [InlineData("shire", 2)]
    public void Read_OfWrittenGeneratedWorld_PreservesAllData(string textSeed, int difficulty)
    {
      // Arrange: A generated world with a full graph and reach metadata.
      var world = GenerateWorld(textSeed, difficulty);

      // Act: Save and load it.
      var loaded = Load(Save(world));

      // Assert: Every value survived; the graph really carries data to compare.
      Assert.NotEmpty(world.Graph.Nodes);
      Assert.NotEmpty(world.Graph.Edges);
      AssertSameWorld(world, loaded);
    }

    [Fact]
    public void Read_OfWrittenGeneratedWorlds_CoversBothBuilders()
    {
      // Arrange: Generated worlds across a seed sweep.
      var builders = new HashSet<string>();
      for (var seed = 0; seed < 32; seed++)
      {
        var world = GenerateWorld($"sweep-{seed}", 5);
        builders.Add(world.Graph.Edges.Any(edge => edge.Conditions.Count > 0) ? "conditional" : "open");

        // Act/Assert: Every world round-trips, conditional (Linear) and open (Simple) graphs alike.
        AssertSameWorld(world, Load(Save(world)));
      }

      // Assert: The sweep covered graphs with and without conditions.
      Assert.Equal(2, builders.Count);
    }

    [Fact]
    public void Write_OfLoadedWorld_ReproducesIdenticalBytes()
    {
      // Arrange: Bytes of a generated world.
      var data = Save(GenerateWorld("mordor", 6));

      // Act: Load and save again.
      var again = Save(Load(data));

      // Assert: The format is canonical, so a round trip changes no byte.
      Assert.Equal(data, again);
    }

    [Fact]
    public void Read_OfWrittenDefaultWorld_ReturnsEmptyWorld()
    {
      // Act: Round-trip a freshly created world.
      var loaded = Load(Save(new LilWorld()));

      // Assert: Empty graph, default phase and statistics.
      AssertSameWorld(new LilWorld(), loaded);
      Assert.NotNull(loaded.Map);
    }

    [Fact]
    public void Read_OfWrittenTinyWorld_PreservesNullAndNonNullStrings()
    {
      // Arrange: Hand-built world with both null and set optional strings.
      var world = CreateTinyWorld();

      // Act: Save and load it.
      var loaded = Load(Save(world));

      // Assert: Nulls stay null, values stay values.
      AssertSameWorld(world, loaded);
      Assert.Equal("g1", loaded.Graph.Nodes[0].Style.Group);
      Assert.Null(loaded.Graph.Nodes[1].Style.Group);
      Assert.Null(loaded.Graph.Nodes[0].Pois[0].Name);
      Assert.Equal("key", loaded.Graph.Nodes[1].Pois[0].Name);
    }
  }

  /// <summary>Tests of deterministic, canonical output.</summary>
  public sealed class DeterminismTests
  {
    [Fact]
    public void Write_SameWorldTwice_ProducesIdenticalBytes()
    {
      // Arrange: One generated world.
      var world = GenerateWorld("mordor", 5);

      // Act: Save it twice.
      var first = Save(world);
      var second = Save(world);

      // Assert: Same world, same bytes.
      Assert.Equal(first, second);
    }

    [Fact]
    public void Write_WorldsDifferingOnlyInReachInsertionOrder_ProduceIdenticalBytes()
    {
      // Arrange: Two copies of a world whose reach entries are inserted in opposite orders.
      var poi1 = new Guid("00000000-0000-0000-0000-000000000001");
      var poi2 = new Guid("00000000-0000-0000-0000-000000000002");
      var node = new Guid("00000000-0000-0000-0000-00000000000a");
      LilWorld Create(bool reversed)
      {
        var world = new LilWorld();
        var reach = new Dictionary<Guid, List<Guid>>();
        if (reversed) { reach[poi2] = [node]; reach[poi1] = [node]; }
        else { reach[poi1] = [node]; reach[poi2] = [node]; }
        world.Graph.Nodes.Add(new LilNode { Id = node, RelSize = 1, MaxEdges = -1, Reach = reach });
        return world;
      }

      // Act: Save both.
      var forward = Save(Create(reversed: false));
      var backward = Save(Create(reversed: true));

      // Assert: Reach entries are written sorted, so insertion order leaves no trace.
      Assert.Equal(forward, backward);
    }

    [Fact]
    public void Write_TinyWorld_MatchesPinnedBytes()
    {
      // Act: Save the hand-built world.
      var hex = Convert.ToHexString(Save(CreateTinyWorld()));

      // Assert: The bytes match the pinned format version 1, so the format cannot change
      // without a deliberate test update (and a format version bump).
      Assert.Equal(PinnedTinyWorldHex, hex);
    }

    /// <summary>Pinned bytes of <see cref="CreateTinyWorld"/> in format version 1.</summary>
    private const string PinnedTinyWorldHex =
      "4C494C5701000101000000010208000000030000000000000003FB000000020000000000000000000000000000000000" +
      "000A02000000FFFFFFFF0001026731010000000000000000000000000000000000000100000100000000000000000000" +
      "000000000000000001010000000000000000000000000000000000000A01000000000000000000000000000000000000" +
      "0A0000000000000000000000000000000B01000000040000000100010000000000000000000000000000000000000204" +
      "01036B6579000000000000000001000000000000000000000000000000000000E1000000000000000000000000000000" +
      "0A0000000000000000000000000000000B010100010000000000000000000000000000000000000002040000000000";
  }

  /// <summary>Tests of the stream contract.</summary>
  public sealed class StreamTests
  {
    [Fact]
    public void WriteAndRead_LeaveStreamOpenAndPositionedAfterWorld()
    {
      // Arrange: A stream holding a world followed by one trailing byte.
      const byte trailer = 0x5A;
      using var stream = new MemoryStream();
      LevelGenWorldSerializer.Write(CreateTinyWorld(), stream);
      stream.WriteByte(trailer);
      stream.Position = 0;

      // Act: Read the world.
      var loaded = LevelGenWorldSerializer.Read(stream);

      // Assert: The stream is still usable and the next byte is the trailer.
      Assert.Equal(2, loaded.Graph.Nodes.Count);
      Assert.Equal(trailer, stream.ReadByte());
      Assert.Equal(-1, stream.ReadByte());
    }
  }

  /// <summary>Tests of argument validation.</summary>
  public sealed class ArgumentTests
  {
    [Fact]
    public void Write_WithNullArguments_ThrowsArgumentNullException()
    {
      // Act/Assert: Each missing argument is named.
      using var stream = new MemoryStream();
      Assert.Equal("world", Assert.Throws<ArgumentNullException>(() => LevelGenWorldSerializer.Write(null!, stream)).ParamName);
      Assert.Equal("stream", Assert.Throws<ArgumentNullException>(() => LevelGenWorldSerializer.Write(new LilWorld(), null!)).ParamName);
    }

    [Fact]
    public void Read_WithNullStream_ThrowsArgumentNullException()
    {
      // Act/Assert: The missing stream is named.
      Assert.Equal("stream", Assert.Throws<ArgumentNullException>(() => LevelGenWorldSerializer.Read(null!)).ParamName);
    }

    [Fact]
    public void Write_WithReadOnlyStream_ThrowsArgumentException()
    {
      // Arrange: A stream that cannot be written.
      using var stream = new MemoryStream([], writable: false);

      // Act/Assert: The stream is rejected.
      Assert.Equal("stream", Assert.Throws<ArgumentException>(() => LevelGenWorldSerializer.Write(new LilWorld(), stream)).ParamName);
    }

    [Fact]
    public void Read_WithUnreadableStream_ThrowsArgumentException()
    {
      // Arrange: A disposed stream reports that it cannot be read.
      var stream = new MemoryStream();
      stream.Dispose();

      // Act/Assert: The stream is rejected.
      Assert.Equal("stream", Assert.Throws<ArgumentException>(() => LevelGenWorldSerializer.Read(stream)).ParamName);
    }

    [Fact]
    public void Write_WorldWithNullContainer_ThrowsArgumentException()
    {
      // Arrange: Worlds with a null graph, node list and POI list.
      var nullGraph = new LilWorld { Graph = null! };
      var nullNodes = new LilWorld();
      nullNodes.Graph.Nodes = null!;
      var nullPois = new LilWorld();
      nullPois.Graph.Nodes.Add(new LilNode { Id = Guid.NewGuid(), RelSize = 1, MaxEdges = -1, Pois = null! });

      // Act/Assert: Each is rejected naming the world.
      foreach (var world in new[] { nullGraph, nullNodes, nullPois })
      {
        using var stream = new MemoryStream();
        Assert.Equal("world", Assert.Throws<ArgumentException>(() => LevelGenWorldSerializer.Write(world, stream)).ParamName);
      }
    }

    [Fact]
    public void Write_WorldWithUndefinedEnum_ThrowsArgumentException()
    {
      // Arrange: World with a phase outside the enum members.
      var world = new LilWorld { Phase = (EnPhase)99 };
      using var stream = new MemoryStream();

      // Act/Assert: The value is rejected instead of producing unreadable data.
      Assert.Equal("world", Assert.Throws<ArgumentException>(() => LevelGenWorldSerializer.Write(world, stream)).ParamName);
    }
  }

  /// <summary>Tests of rejecting data that is not a valid saved world.</summary>
  public sealed class InvalidDataTests
  {
    /// <summary>Saves the tiny world, lets <paramref name="corrupt"/> change the bytes and asserts the load fails.</summary>
    private static void AssertCorruptionRejected(Action<byte[]> corrupt)
    {
      var data = Save(CreateTinyWorld());
      corrupt(data);
      Assert.Throws<InvalidDataException>(() => Load(data));
    }

    [Fact]
    public void Read_WithWrongSignature_ThrowsInvalidDataException()
    {
      // Act/Assert: Data not starting with "LILW" is rejected.
      AssertCorruptionRejected(data => data[0] = (byte)'X');
    }

    [Fact]
    public void Read_WithOtherFormatVersion_ThrowsInvalidDataException()
    {
      // Act/Assert: Only the library's own version is accepted.
      AssertCorruptionRejected(data => data[VersionOffset] = 2);
    }

    [Fact]
    public void Read_WithUnknownSectionId_ThrowsInvalidDataException()
    {
      // Act/Assert: An undefined section id is rejected.
      AssertCorruptionRejected(data => data[WorldSectionOffset] = 9);
    }

    [Fact]
    public void Read_WithRepeatedSection_ThrowsInvalidDataException()
    {
      // Act/Assert: A World section where Stats must follow is rejected.
      AssertCorruptionRejected(data => data[StatsSectionOffset] = data[WorldSectionOffset]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public void Read_WithWrongPayloadLength_ThrowsInvalidDataException(int length)
    {
      // Act/Assert: A payload length that does not match the payload is rejected.
      AssertCorruptionRejected(data => BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(WorldLengthOffset), length));
    }

    [Fact]
    public void Read_WithUndefinedEnumValue_ThrowsInvalidDataException()
    {
      // Act/Assert: A phase byte outside the enum members is rejected.
      AssertCorruptionRejected(data => data[PhaseOffset] = 200);
    }

    [Fact]
    public void Read_WithNegativeCount_ThrowsInvalidDataException()
    {
      // Act/Assert: A negative node count is rejected.
      AssertCorruptionRejected(data => BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(NodeCountOffset), -1));
    }

    [Fact]
    public void Read_WithInvalidFlagByte_ThrowsInvalidDataException()
    {
      // Act/Assert: A flag byte other than 0 or 1 is rejected.
      AssertCorruptionRejected(data => data[FirstNodeSecretOffset] = 2);
    }

    [Fact]
    public void Read_WithWrongEndMarker_ThrowsInvalidDataException()
    {
      // Act/Assert: Data not closed by the end marker is rejected.
      AssertCorruptionRejected(data => data[^1] = 7);
    }

    [Fact]
    public void Read_WithTruncatedData_ThrowsInvalidDataException()
    {
      // Arrange: Bytes of a world.
      var data = Save(CreateTinyWorld());

      // Act/Assert: Every truncation point is rejected - never another exception type.
      for (var length = 0; length < data.Length; length++)
      {
        var truncated = data[..length];
        Assert.Throws<InvalidDataException>(() => Load(truncated));
      }
    }
  }

  #endregion
}
