using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Data.Result.Stats;

namespace LostInLimbo.LevelGen.Tests.Data.Result;

/// <summary>
/// Tests <see cref="LilWorldCopy"/>: value preservation, detachment of every mutable part
/// and public input validation.
/// </summary>
public sealed class LilWorldCopyTests
{
  /// <summary>Relative size used by test nodes; only required to satisfy <see cref="LilNode.RelSize"/>.</summary>
  private const int NodeRelSize = 1;

  /// <summary>Edge cap used by test nodes; negative means no limit.</summary>
  private const int NodeMaxEdges = -1;

  /// <summary>
  /// Builds a populated world covering every mutable surface: map phase, non-zero stats,
  /// nodes with POIs and style, and an edge with a condition.
  /// </summary>
  /// <returns>World with data on all copy-relevant members.</returns>
  private static LilWorld PopulatedWorld()
  {
    var keyPoiId = Guid.NewGuid();
    return new LilWorld
    {
      Phase = EnPhase.Map,
      Graph = new LilGraph
      {
        Nodes =
        [
          new LilNode
          {
            Id = Guid.NewGuid(),
            RelSize = NodeRelSize,
            MaxEdges = NodeMaxEdges,
            Style = new LilStyle { Group = "Lobby" },
            Pois = [new LilPoi { Id = Guid.NewGuid(), Type = EnPoiType.Start }],
          },
          new LilNode
          {
            Id = Guid.NewGuid(),
            RelSize = NodeRelSize,
            MaxEdges = NodeMaxEdges,
            Secret = true,
            Pois = [new LilPoi { Id = keyPoiId, Type = EnPoiType.Solution, Name = "Key" }],
          },
          new LilNode
          {
            Id = Guid.NewGuid(),
            RelSize = NodeRelSize,
            MaxEdges = NodeMaxEdges,
            Pois = [new LilPoi { Id = Guid.NewGuid(), Type = EnPoiType.Exit }],
          },
        ],
        Edges =
        [
          new LilEdge
          {
            Id = Guid.NewGuid(),
            From = Guid.NewGuid(),
            To = Guid.NewGuid(),
            Direction = EnPassageDirection.OneWay,
            Chokepoint = true,
            Conditions = [new LilCondition { Type = EnConditionType.RequiresSolution, RequiresPoi = keyPoiId }],
          },
        ],
      },
      Stats = new LilStats { Steps = 42 },
    };
  }

  [Fact]
  public void Copy_WithNullSource_ReturnsNull()
  {
    // Act: Copy a null source.
    var copy = LilWorldCopy.Copy(null);

    // Assert: A null source produces a null copy instead of an exception.
    Assert.Null(copy);
  }

  [Fact]
  public void Copy_OfDefaultWorld_ProducesIndependentEmptyWorld()
  {
    // Arrange: A pristine world in its initial state.
    var source = new LilWorld();

    // Act: Copy the pristine world.
    var copy = LilWorldCopy.Copy(source);

    // Assert: The copy starts from the same initial state with fresh containers.
    Assert.NotNull(copy);
    Assert.NotSame(source, copy);
    Assert.NotSame(source.Graph, copy.Graph);
    Assert.NotSame(source.Map, copy.Map);
    Assert.NotSame(source.Stats, copy.Stats);
    Assert.NotSame(source.Graph.Nodes, copy.Graph.Nodes);
    Assert.NotSame(source.Graph.Edges, copy.Graph.Edges);
    Assert.Equal(EnPhase.Prepass, copy.Phase);
    Assert.Equal(0L, copy.Stats.Steps);
    Assert.Empty(copy.Graph.Nodes);
    Assert.Empty(copy.Graph.Edges);
  }

  [Fact]
  public void Copy_OfPopulatedWorld_PreservesValues()
  {
    // Arrange: A world with data on every copy-relevant surface.
    var source = PopulatedWorld();

    // Act: Copy the populated world.
    var copy = LilWorldCopy.Copy(source);

    // Assert: Top-level values are preserved.
    Assert.NotNull(copy);
    Assert.Equal(source.Phase, copy.Phase);
    Assert.Equal(source.Stats.Steps, copy.Stats.Steps);

    // Assert: Node contents are preserved element-wise (record equality would compare the
    // lists by reference, so the assertions check members explicitly).
    Assert.Equal(source.Graph.Nodes.Count, copy.Graph.Nodes.Count);
    for (var i = 0; i < source.Graph.Nodes.Count; i++)
    {
      var expected = source.Graph.Nodes[i];
      var actual = copy.Graph.Nodes[i];
      Assert.Equal(expected.Id, actual.Id);
      Assert.Equal(expected.RelSize, actual.RelSize);
      Assert.Equal(expected.MaxEdges, actual.MaxEdges);
      Assert.Equal(expected.Secret, actual.Secret);
      Assert.Equal(expected.Style.Group, actual.Style.Group);
      Assert.Equal(expected.Pois.Count, actual.Pois.Count);
      Assert.Equal(expected.Pois[0].Id, actual.Pois[0].Id);
      Assert.Equal(expected.Pois[0].Type, actual.Pois[0].Type);
      Assert.Equal(expected.Pois[0].Name, actual.Pois[0].Name);
    }

    // Assert: Edge contents are preserved element-wise.
    var expectedEdge = Assert.Single(source.Graph.Edges);
    var actualEdge = Assert.Single(copy.Graph.Edges);
    Assert.Equal(expectedEdge.Id, actualEdge.Id);
    Assert.Equal(expectedEdge.From, actualEdge.From);
    Assert.Equal(expectedEdge.To, actualEdge.To);
    Assert.Equal(expectedEdge.Direction, actualEdge.Direction);
    Assert.Equal(expectedEdge.Chokepoint, actualEdge.Chokepoint);
    Assert.Equal(expectedEdge.Secret, actualEdge.Secret);
    var expectedCondition = Assert.Single(expectedEdge.Conditions);
    var actualCondition = Assert.Single(actualEdge.Conditions);
    Assert.Equal(expectedCondition.Type, actualCondition.Type);
    Assert.Equal(expectedCondition.RequiresPoi, actualCondition.RequiresPoi);
  }

  [Fact]
  public void Copy_CopiesNodeAndEdgeInstances()
  {
    // Arrange: A world with data on every copy-relevant surface.
    var source = PopulatedWorld();

    // Act: Copy the populated world.
    var copy = LilWorldCopy.Copy(source);
    Assert.NotNull(copy);

    // Assert: Nodes, edges and their nested lists are fresh instances, not shared references.
    for (var i = 0; i < source.Graph.Nodes.Count; i++)
    {
      Assert.NotSame(source.Graph.Nodes[i], copy.Graph.Nodes[i]);
      Assert.NotSame(source.Graph.Nodes[i].Pois, copy.Graph.Nodes[i].Pois);
    }
    Assert.NotSame(source.Graph.Edges[0], copy.Graph.Edges[0]);
    Assert.NotSame(source.Graph.Edges[0].Conditions, copy.Graph.Edges[0].Conditions);
  }

  [Fact]
  public void Copy_MutatingCopy_LeavesSourceUntouched()
  {
    // Arrange: A world with data on every copy-relevant surface.
    var source = PopulatedWorld();

    // Act: Mutate every mutable surface of the copy.
    var copy = LilWorldCopy.Copy(source);
    Assert.NotNull(copy);
    
    foreach (var node in copy.Graph.Nodes)
      node.Pois.Clear();
    copy.Graph.Edges[0].Conditions.Clear();
    copy.Graph.Nodes.Clear();
    copy.Graph.Edges.Clear();
    copy.Graph = new();
    copy.Stats.Steps = -1;
    copy.Stats = new();
    copy.Phase = EnPhase.Prepass;

    // Assert: The source still carries all of its original data.
    Assert.Equal(EnPhase.Map, source.Phase);
    Assert.Equal(42L, source.Stats.Steps);
    Assert.Equal(3, source.Graph.Nodes.Count);
    Assert.All(source.Graph.Nodes, node => Assert.Single(node.Pois));
    var sourceEdge = Assert.Single(source.Graph.Edges);
    Assert.Single(sourceEdge.Conditions);
  }
}
