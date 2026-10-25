using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Tests.Data.Result.Graph;

/// <summary>
/// Tests the graph data structures: default state, record equality, with-expressions and
/// multi-POI / multi-condition construction.
/// </summary>
/// <remarks>
/// These tests cover data shapes only; generation algorithms and validation are covered elsewhere.
/// Note: record equality compares collection properties by reference, so assertions on
/// lists check counts and contents element-wise instead of whole-graph Assert.Equal.
/// </remarks>
public sealed class LilGraphDataTests
{
  /// <summary>Relative size used by test nodes; only required to satisfy <see cref="LilNode.RelSize"/>.</summary>
  private const int NodeRelSize = 1;

  /// <summary>Edge cap used by test nodes; negative means no limit.</summary>
  private const int NodeMaxEdges = -1;

  /// <summary>Tests <see cref="LilGraph"/>: default state and built-graph contents.</summary>
  public sealed class GraphTests
  {
    [Fact]
    public void LilGraph_WithDefaultConstruction_HasEmptyCollections()
    {
      // Arrange/Act: Create graph without any data.
      var graph = new LilGraph();

      // Assert: Collections are present and empty, never null.
      Assert.NotNull(graph.Nodes);
      Assert.NotNull(graph.Edges);
      Assert.Empty(graph.Nodes);
      Assert.Empty(graph.Edges);
    }

    [Fact]
    public void LilGraph_WithBuiltGraph_ExposesNodesAndEdges()
    {
      // Arrange: Small graph: start room, key room, exit behind door requiring the key.
      var startId = Guid.NewGuid();
      var keyNodeId = Guid.NewGuid();
      var keyPoiId = Guid.NewGuid();
      var exitId = Guid.NewGuid();
      var graph = new LilGraph
      {
        Nodes =
        [
          new LilNode
          {
            Id = startId,
            RelSize = NodeRelSize,
            MaxEdges = NodeMaxEdges,
            Pois = [new LilPoi { Id = Guid.NewGuid(), Type = EnPoiType.Start }],
          },
          new LilNode
          {
            Id = keyNodeId,
            RelSize = NodeRelSize,
            MaxEdges = NodeMaxEdges,
            Pois = [new LilPoi { Id = keyPoiId, Type = EnPoiType.Solution, Name = "Key" }],
          },
          new LilNode
          {
            Id = exitId,
            RelSize = NodeRelSize,
            MaxEdges = NodeMaxEdges,
            Pois = [new LilPoi { Id = Guid.NewGuid(), Type = EnPoiType.Exit }],
          },
        ],
        Edges =
        [
          new LilEdge { Id = Guid.NewGuid(), From = startId, To = keyNodeId },
          new LilEdge
          {
            Id = Guid.NewGuid(),
            From = keyNodeId,
            To = exitId,
            Conditions = [new LilCondition { Type = EnConditionType.RequiresSolution, RequiresPoi = keyPoiId }],
          },
        ],
      };

      // Act: Nothing; verifying the built graph.

      // Assert: Nodes and edges hold the expected counts and key references.
      Assert.Equal(3, graph.Nodes.Count);
      Assert.Equal(2, graph.Edges.Count);
      Assert.Equal(startId, graph.Nodes[0].Id);
      Assert.Equal(keyNodeId, graph.Nodes[1].Id);
      Assert.Equal(exitId, graph.Nodes[2].Id);
      Assert.Empty(graph.Edges[0].Conditions);
      Assert.Single(graph.Edges[1].Conditions);
      Assert.Equal(keyPoiId, graph.Edges[1].Conditions[0].RequiresPoi);
    }
  }

  /// <summary>Tests <see cref="LilNode"/>: required members, multiple POIs, equality and with-expressions.</summary>
  public sealed class NodeTests
  {
    [Fact]
    public void LilNode_WithRequiredMembersOnly_HasEmptyPoisList()
    {
      // Arrange/Act: Create node with only the required members set.
      var node = new LilNode { Id = Guid.NewGuid(), RelSize = NodeRelSize, MaxEdges = NodeMaxEdges };

      // Assert: Required members hold given values; Pois list is present and empty, never null.
      Assert.NotNull(node.Pois);
      Assert.Empty(node.Pois);
      Assert.NotEqual(Guid.Empty, node.Id);
      Assert.Equal(NodeRelSize, node.RelSize);
      Assert.Equal(NodeMaxEdges, node.MaxEdges);
    }

    [Fact]
    public void LilNode_WithRequiredMembersOnly_HasEmptyReach()
    {
      // Arrange/Act: Create node with only the required members set.
      var node = new LilNode { Id = Guid.NewGuid(), RelSize = NodeRelSize, MaxEdges = NodeMaxEdges };

      // Assert: Reach dictionary is present and empty, never null; the metadata pass fills
      // it later (and it is mutable in place, like Pois).
      Assert.NotNull(node.Reach);
      Assert.Empty(node.Reach);
    }

    [Fact]
    public void LilNode_WithMultiplePois_ContainsAllPois()
    {
      // Arrange: Node with red and blue key in the same room.
      var redKeyId = Guid.NewGuid();
      var blueKeyId = Guid.NewGuid();
      var node = new LilNode
      {
        Id = Guid.NewGuid(),
        RelSize = NodeRelSize,
        MaxEdges = NodeMaxEdges,
        Pois =
        [
          new LilPoi { Id = redKeyId, Type = EnPoiType.Solution, Name = "Red Key" },
          new LilPoi { Id = blueKeyId, Type = EnPoiType.Solution, Name = "Blue Key" },
        ],
      };

      // Act: Nothing; verifying construction.

      // Assert: Both POIs are present in order, each with its own id.
      Assert.Equal(2, node.Pois.Count);
      Assert.Equal(redKeyId, node.Pois[0].Id);
      Assert.Equal(EnPoiType.Solution, node.Pois[0].Type);
      Assert.Equal("Red Key", node.Pois[0].Name);
      Assert.Equal(blueKeyId, node.Pois[1].Id);
      Assert.Equal(EnPoiType.Solution, node.Pois[1].Type);
      Assert.Equal("Blue Key", node.Pois[1].Name);
      Assert.NotEqual(node.Pois[0].Id, node.Pois[1].Id);
    }

    [Fact]
    public void LilNode_WithSameData_ExposesEqualContents()
    {
      // Arrange: Same id and same POIs in two instances (separate list instances).
      var id = Guid.NewGuid();
      var poiId = Guid.NewGuid();
      var node1 = new LilNode
      {
        Id = id,
        RelSize = NodeRelSize,
        MaxEdges = NodeMaxEdges,
        Pois = [new LilPoi { Id = poiId, Type = EnPoiType.Start }],
      };
      var node2 = new LilNode
      {
        Id = id,
        RelSize = NodeRelSize,
        MaxEdges = NodeMaxEdges,
        Pois = [new LilPoi { Id = poiId, Type = EnPoiType.Start }],
      };

      // Act/Assert: Record equality compares the list property by reference, so two nodes
      // with equal-content but distinct list instances are NOT record-equal. Verify field
      // equality instead; this documents the known equality caveat for collection properties.
      Assert.NotEqual(node1, node2);
      Assert.Equal(node1.Id, node2.Id);
      Assert.Equal(node1.Pois.Count, node2.Pois.Count);
      Assert.Equal(node1.Pois[0], node2.Pois[0]);
    }

    [Fact]
    public void LilNode_WithExpression_ProducesModifiedCopy()
    {
      // Arrange: Node with a single POI.
      var node = new LilNode
      {
        Id = Guid.NewGuid(),
        RelSize = NodeRelSize,
        MaxEdges = NodeMaxEdges,
        Pois = [new LilPoi { Id = Guid.NewGuid(), Type = EnPoiType.Goal }],
      };

      // Act: Add another POI via with-expression.
      var copy = node with
      {
        Pois = [.. node.Pois, new LilPoi { Id = Guid.NewGuid(), Type = EnPoiType.Exit }],
      };

      // Assert: Copy has both POIs; original is unchanged.
      Assert.Equal(2, copy.Pois.Count);
      Assert.Single(node.Pois);
    }
  }

  /// <summary>Tests <see cref="LilEdge"/>: defaults, multi-condition scenarios and equality.</summary>
  public sealed class EdgeTests
  {
    [Fact]
    public void LilEdge_WithRequiredMembersOnly_IsOpenBidirectional()
    {
      // Arrange/Act: Create edge with only the required members set.
      var id = Guid.NewGuid();
      var from = Guid.NewGuid();
      var to = Guid.NewGuid();
      var edge = new LilEdge { Id = id, From = from, To = to };

      // Assert: Required members hold given values; defaults describe an open, bidirectional passage.
      Assert.Equal(id, edge.Id);
      Assert.Equal(from, edge.From);
      Assert.Equal(to, edge.To);
      Assert.Equal(EnPassageDirection.Bidirectional, edge.Direction);
      Assert.NotNull(edge.Conditions);
      Assert.Empty(edge.Conditions);
    }

    [Fact]
    public void LilEdge_WithMultipleConditions_RequiresBothKeys()
    {
      // Arrange: Two key POIs (red and blue) plus a door requiring both.
      var redKeyId = Guid.NewGuid();
      var blueKeyId = Guid.NewGuid();
      var edge = new LilEdge
      {
        Id = Guid.NewGuid(),
        From = Guid.NewGuid(),
        To = Guid.NewGuid(),
        Direction = EnPassageDirection.OneWay,
        Conditions =
        [
          new LilCondition { Type = EnConditionType.RequiresSolution, RequiresPoi = redKeyId },
          new LilCondition { Type = EnConditionType.RequiresSolution, RequiresPoi = blueKeyId },
        ],
      };

      // Act: Nothing; verifying construction.

      // Assert: Both conditions are present and reference the correct POIs.
      Assert.Equal(2, edge.Conditions.Count);
      Assert.Equal(EnConditionType.RequiresSolution, edge.Conditions[0].Type);
      Assert.Equal(redKeyId, edge.Conditions[0].RequiresPoi);
      Assert.Equal(EnConditionType.RequiresSolution, edge.Conditions[1].Type);
      Assert.Equal(blueKeyId, edge.Conditions[1].RequiresPoi);
      Assert.Equal(EnPassageDirection.OneWay, edge.Direction);
    }

    [Fact]
    public void LilEdge_WithTwoKeysInOneNode_EachDoorRequiresItsOwnKey()
    {
      // Arrange: Single node holding both keys; two doors, each unlocked by a different key.
      // This is the motivating case for POI-level references: node-level would be ambiguous.
      var redKeyId = Guid.NewGuid();
      var blueKeyId = Guid.NewGuid();
      var keyNode = new LilNode
      {
        Id = Guid.NewGuid(),
        RelSize = NodeRelSize,
        MaxEdges = NodeMaxEdges,
        Pois =
        [
          new LilPoi { Id = redKeyId, Type = EnPoiType.Solution, Name = "Red Key" },
          new LilPoi { Id = blueKeyId, Type = EnPoiType.Solution, Name = "Blue Key" },
        ],
      };
      var doorA = new LilEdge
      {
        Id = Guid.NewGuid(),
        From = keyNode.Id,
        To = Guid.NewGuid(),
        Conditions = [new LilCondition { Type = EnConditionType.RequiresSolution, RequiresPoi = redKeyId }],
      };
      var doorB = new LilEdge
      {
        Id = Guid.NewGuid(),
        From = keyNode.Id,
        To = Guid.NewGuid(),
        Conditions = [new LilCondition { Type = EnConditionType.RequiresSolution, RequiresPoi = blueKeyId }],
      };

      // Act: Nothing; verifying construction.

      // Assert: Each door references its distinct key POI inside the same node.
      Assert.Single(doorA.Conditions);
      Assert.Single(doorB.Conditions);
      Assert.Equal(redKeyId, doorA.Conditions[0].RequiresPoi);
      Assert.Equal(blueKeyId, doorB.Conditions[0].RequiresPoi);
      Assert.NotEqual(doorA.Conditions[0].RequiresPoi, doorB.Conditions[0].RequiresPoi);
    }

    [Fact]
    public void LilEdge_WithSameData_ExposesEqualContents()
    {
      // Arrange: Same field values in two instances (separate list instances).
      var id = Guid.NewGuid();
      var from = Guid.NewGuid();
      var to = Guid.NewGuid();
      var edge1 = new LilEdge
      {
        Id = id,
        From = from,
        To = to,
        Conditions = [new LilCondition { Type = EnConditionType.RequiresSolution, RequiresPoi = Guid.NewGuid() }],
      };
      var edge2 = new LilEdge
      {
        Id = id,
        From = from,
        To = to,
        Conditions = [.. edge1.Conditions],
      };

      // Act/Assert: Record equality compares the list property by reference, so two edges
      // with equal-content but distinct list instances are NOT record-equal. Verify field
      // equality instead; this documents the known equality caveat for collection properties.
      Assert.NotEqual(edge1, edge2);
      Assert.Equal(edge1.Id, edge2.Id);
      Assert.Equal(edge1.From, edge2.From);
      Assert.Equal(edge1.To, edge2.To);
      Assert.Equal(edge1.Conditions.Count, edge2.Conditions.Count);
      Assert.Equal(edge1.Conditions[0], edge2.Conditions[0]);
    }
  }

  /// <summary>Tests <see cref="LilCondition"/>: record equality.</summary>
  public sealed class ConditionTests
  {
    [Fact]
    public void LilCondition_WithSameData_IsRecordEqual()
    {
      // Arrange: Same type and target POI.
      var poi = Guid.NewGuid();
      var condition1 = new LilCondition { Type = EnConditionType.RequiresSolution, RequiresPoi = poi };
      var condition2 = new LilCondition { Type = EnConditionType.RequiresSolution, RequiresPoi = poi };

      // Act/Assert: Record equality holds for instances with the same values.
      Assert.Equal(condition1, condition2);
    }
  }
}
