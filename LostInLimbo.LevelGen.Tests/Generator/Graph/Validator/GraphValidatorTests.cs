using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Validator;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.GraphTestCreate;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Validator;

/// <summary>
/// Tests <see cref="GraphValidator"/>: structural prechecks, condition-aware flood-fill
/// connectivity and the final beatability outcome with its failure reasons.
/// </summary>
/// <remarks>
/// Grouped into suites: structure (references, POIs), the MaxEdges hard cap, connectivity
/// (flood-fill reachability) and conditions (key/door solvability). Test graphs are
/// hand-built so each failure shape can be isolated; positive cases are additionally
/// cross-checked against real builder output in <c>LevelGraphValidationTests</c>.
/// </remarks>
public sealed class GraphValidatorTests
{
  /// <summary>Tests structural validation: graph presence, required POIs and reference integrity.</summary>
  public sealed class StructureTests
  {
    [Fact]
    public void Validate_NullGraph_Fails()
    {
      // Arrange: No graph at all.

      // Act: Validate the missing graph.
      var result = Validate(null);

      // Assert: Failure with the dedicated reason.
      Assert.False(result.Success);
      Assert.Equal("Graph is null.", result.FailureReason);
    }

    [Fact]
    public void Validate_GraphWithoutNodes_Fails()
    {
      // Arrange: Graph with no nodes and no edges.
      var graph = new LilGraph();

      // Act: Validate the empty graph.
      var result = Validate(graph);

      // Assert: Failure with the dedicated reason.
      Assert.False(result.Success);
      Assert.Equal("Graph has no nodes.", result.FailureReason);
    }

    [Fact]
    public void Validate_GraphWithoutStartPoi_Fails()
    {
      // Arrange: Single node containing only an exit.
      var graph = new LilGraph
      {
        Nodes = [Node(1, Poi(10, EnPoiType.Exit))],
      };

      // Act: Validate the start-less graph.
      var result = Validate(graph);

      // Assert: Failure with the dedicated reason.
      Assert.False(result.Success);
      Assert.Equal("Graph has no Start PoI.", result.FailureReason);
    }

    [Fact]
    public void Validate_GraphWithoutExitPoi_Fails()
    {
      // Arrange: Single node containing only a start.
      var graph = new LilGraph
      {
        Nodes = [Node(1, Poi(10, EnPoiType.Start))],
      };

      // Act: Validate the exit-less graph.
      var result = Validate(graph);

      // Assert: Failure with the dedicated reason.
      Assert.False(result.Success);
      Assert.Equal("Graph has no Exit PoI.", result.FailureReason);
    }

    [Fact]
    public void Validate_SingleNodeWithStartAndExit_Succeeds()
    {
      // Arrange: One node holding both start and exit; no edges needed.
      var graph = new LilGraph
      {
        Nodes = [Node(1, Poi(10, EnPoiType.Start), Poi(11, EnPoiType.Exit))],
      };

      // Act: Validate the minimal beatable graph.
      var result = Validate(graph);

      // Assert: Success without a failure reason.
      Assert.True(result.Success);
      Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Validate_DuplicateNodeId_Fails()
    {
      // Arrange: Two distinct nodes sharing one identifier.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start), Poi(11, EnPoiType.Exit)),
          Node(1),
        ],
      };

      // Act: Validate the graph with ambiguous node identity.
      var result = Validate(graph);

      // Assert: Failure naming the duplicated identifier.
      Assert.False(result.Success);
      Assert.Equal($"Duplicate node id {Id(1)}.", result.FailureReason);
    }

    [Fact]
    public void Validate_DuplicatePoiId_Fails()
    {
      // Arrange: Two nodes each holding a POI with the same identifier.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start), Poi(11, EnPoiType.Exit)),
          Node(2, Poi(10, EnPoiType.Solution)),
        ],
      };

      // Act: Validate the graph with ambiguous POI identity.
      var result = Validate(graph);

      // Assert: Failure naming the duplicated identifier.
      Assert.False(result.Success);
      Assert.Equal($"Duplicate POI id {Id(10)}.", result.FailureReason);
    }

    [Fact]
    public void Validate_EdgeReferencingMissingNode_Fails()
    {
      // Arrange: Edge whose destination node does not exist.
      var graph = new LilGraph
      {
        Nodes = [Node(1, Poi(10, EnPoiType.Start), Poi(11, EnPoiType.Exit))],
        Edges = [Edge(100, from: 1, to: 999)],
      };

      // Act: Validate the graph with a dangling endpoint.
      var result = Validate(graph);

      // Assert: Failure naming the edge and the missing node.
      Assert.False(result.Success);
      Assert.Equal($"Edge {Id(100)} references missing node {Id(999)}.", result.FailureReason);
    }

    [Fact]
    public void Validate_EdgeConnectingNodeToItself_Fails()
    {
      // Arrange: Self-loop edge on an otherwise valid node.
      var graph = new LilGraph
      {
        Nodes = [Node(1, Poi(10, EnPoiType.Start), Poi(11, EnPoiType.Exit))],
        Edges = [Edge(100, from: 1, to: 1)],
      };

      // Act: Validate the graph with a degenerate passage.
      var result = Validate(graph);

      // Assert: Failure naming the self-looping edge.
      Assert.False(result.Success);
      Assert.Equal($"Edge {Id(100)} connects node to itself.", result.FailureReason);
    }

    [Fact]
    public void Validate_ConditionReferencingMissingPoi_Fails()
    {
      // Arrange: Condition pointing at a POI identifier that no node holds.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(11, EnPoiType.Exit)),
        ],
        Edges = [Edge(100, from: 1, to: 2, conditions: [Condition(999)])],
      };

      // Act: Validate the graph with a dangling requirement.
      var result = Validate(graph);

      // Assert: Failure naming the edge and the missing POI.
      Assert.False(result.Success);
      Assert.Equal(
        $"Condition on edge {Id(100)} references missing POI {Id(999)}.",
        result.FailureReason);
    }
  }

  /// <summary>Tests flood-fill reachability: open passages, one-way passages and leftovers.</summary>
  public sealed class ConnectivityTests
  {
    [Fact]
    public void Validate_OpenChain_Succeeds()
    {
      // Arrange: Start - middle - exit, all passages open and bidirectional.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2),
          Node(3, Poi(11, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 2, to: 3),
        ],
      };

      // Act: Validate the connected graph.
      var result = Validate(graph);

      // Assert: Success without a failure reason.
      Assert.True(result.Success);
      Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Validate_ExitOnUnreachableNode_Fails()
    {
      // Arrange: Exit sits on an isolated node; start only reaches a plain middle node.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2),
          Node(3, Poi(11, EnPoiType.Exit)),
        ],
        Edges = [Edge(100, from: 1, to: 2)],
      };

      // Act: Validate the graph with an unreachable exit.
      var result = Validate(graph);

      // Assert: Failure reports the unreachable exit. Note this error takes precedence over orphan node.
      Assert.False(result.Success);
      Assert.Equal("Exit PoI is not reachable from Start.", result.FailureReason);
    }

    [Fact]
    public void Validate_LeftoverUnreachableNode_Fails()
    {
      // Arrange: Start and exit are fine, but a third node floats disconnected.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start), Poi(11, EnPoiType.Exit)),
          Node(2),
          Node(3),
        ],
        Edges = [Edge(100, from: 1, to: 2)],
      };

      // Act: Validate the graph with one orphan node.
      var result = Validate(graph);

      // Assert: Failure reports how many nodes stayed unreachable.
      Assert.False(result.Success);
      Assert.Equal("1 node(s) unreachable from Start.", result.FailureReason);
    }

    [Fact]
    public void Validate_OneWayChainFromStart_Succeeds()
    {
      // Arrange: Start - middle - exit linked only in the forward direction.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2),
          Node(3, Poi(11, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2, EnPassageDirection.OneWay),
          Edge(101, from: 2, to: 3, EnPassageDirection.OneWay),
        ],
      };

      // Act: Validate the forward-only graph.
      var result = Validate(graph);

      // Assert: Success; walking against the one-way sense is not required here.
      Assert.True(result.Success);
      Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Validate_OnlyOneWayEdgesIntoStart_Fails()
    {
      // Arrange: Every passage points towards the start, so the player can never leave.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start), Poi(11, EnPoiType.Exit)),
          Node(2),
          Node(3),
        ],
        Edges =
        [
          Edge(100, from: 2, to: 1, EnPassageDirection.OneWay),
          Edge(101, from: 3, to: 2, EnPassageDirection.OneWay),
        ],
      };

      // Act: Validate the graph trapping the player at the start.
      var result = Validate(graph);

      // Assert: Exit itself is reachable, so the leftover count is reported.
      Assert.False(result.Success);
      Assert.Equal("2 node(s) unreachable from Start.", result.FailureReason);
    }

    [Fact]
    public void Validate_BidirectionalTraversalAgainstOneWayEdge_DoesNotFlood()
    {
      // Arrange: Only one-way edge 1 -> 2 exists; start is on node 2, exit on node 1.
      // Node 1 is reachable backwards only, which one-way passages must forbid.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(11, EnPoiType.Exit)),
          Node(2, Poi(10, EnPoiType.Start)),
        ],
        Edges = [Edge(100, from: 1, to: 2, EnPassageDirection.OneWay)],
      };

      // Act: Validate the graph where the start sits at the one-way destination.
      var result = Validate(graph);

      // Assert: The player cannot walk 2 -> 1, so the exit is unreachable.
      Assert.False(result.Success);
      Assert.Equal("Exit PoI is not reachable from Start.", result.FailureReason);
    }
  }

  /// <summary>Tests condition-aware traversal: solvable key/door setups and softlocks.</summary>
  public sealed class ConditionTests
  {
    [Fact]
    public void Validate_SolutionBeforeDoor_Succeeds()
    {
      // Arrange: Key lives with the start; the only passage to the exit requires it.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start), Poi(12, EnPoiType.Solution)),
          Node(2, Poi(11, EnPoiType.Exit)),
        ],
        Edges = [Edge(100, from: 1, to: 2, conditions: [Condition(12)])],
      };

      // Act: Validate the trivially solvable graph.
      var result = Validate(graph);

      // Assert: The key is collected at the start, so the door opens.
      Assert.True(result.Success);
      Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Validate_SolutionAcquiredAlongTheWay_Succeeds()
    {
      // Arrange: Start -> key room -> door requiring the key -> exit, in a straight line.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(12, EnPoiType.Solution)),
          Node(3, Poi(11, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 2, to: 3, conditions: [Condition(12)]),
        ],
      };

      // Act: Validate the classic key-before-door graph.
      var result = Validate(graph);

      // Assert: Flooding the key room unlocks the door in the next iteration.
      Assert.True(result.Success);
      Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Validate_SolutionBehindItsOwnDoor_Fails()
    {
      // Arrange: The key sits behind the very door that requires it; exit is fine otherwise.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start), Poi(11, EnPoiType.Exit)),
          Node(2),
          Node(3, Poi(12, EnPoiType.Solution)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 2, to: 3, conditions: [Condition(12)]),
        ],
      };

      // Act: Validate the softlocked graph.
      var result = Validate(graph);

      // Assert: Root cause is reported as the never-satisfied conditioned edge.
      Assert.False(result.Success);
      Assert.Equal($"Conditioned edge {Id(101)} can never be satisfied.", result.FailureReason);
    }

    [Fact]
    public void Validate_TwoSolutionsBothReachable_Succeeds()
    {
      // Arrange: Door requires both keys; both keys hang off branches open from the start.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(12, EnPoiType.Solution)),
          Node(3, Poi(13, EnPoiType.Solution)),
          Node(4, Poi(11, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 1, to: 3),
          Edge(102, from: 2, to: 4, conditions: [Condition(12), Condition(13)]),
        ],
      };

      // Act: Validate the double-lock graph with both keys reachable.
      var result = Validate(graph);

      // Assert: All conditions resolve after both branches flood.
      Assert.True(result.Success);
      Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Validate_TwoSolutionsOneBehindDoor_Fails()
    {
      // Arrange: Door requires both keys; the blue key is behind that same door.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start), Poi(12, EnPoiType.Solution)),
          Node(2),
          Node(3, Poi(11, EnPoiType.Exit), Poi(13, EnPoiType.Solution)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 2, to: 3, conditions: [Condition(12), Condition(13)]),
        ],
      };

      // Act: Validate the graph where one required key is out of reach.
      var result = Validate(graph);

      // Assert: The conditioned edge can never be satisfied; that is the root cause.
      Assert.False(result.Success);
      Assert.Equal($"Conditioned edge {Id(101)} can never be satisfied.", result.FailureReason);
    }

    [Fact]
    public void Validate_ConditionedShortcutWithReachableSolution_Succeeds()
    {
      // Arrange: Exit is reachable through an open detour, but a conditioned shortcut also
      // exists; both must be able to open for the graph to be fully verified.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(12, EnPoiType.Solution)),
          Node(3, Poi(11, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 2, to: 3),
          Edge(102, from: 1, to: 3, conditions: [Condition(12)]),
        ],
      };

      // Act: Validate the graph with an open path plus a key-gated shortcut.
      var result = Validate(graph);

      // Assert: Both endpoints flood and the key is collected, so the shortcut passes too.
      Assert.True(result.Success);
      Assert.Null(result.FailureReason);
    }
  }

  /// <summary>Tests the <see cref="LilNode.MaxEdges"/> hard cap: over-cap failures and unlimited negative caps.</summary>
  public sealed class MaxEdgesTests
  {
    [Fact]
    public void Validate_NodeOverMaxEdgesCap_Fails()
    {
      // Arrange: Start node capped at one relationship, yet two edges touch it. The cap
      // violation must be reported before any traversal outcome would complain.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, maxEdges: 1, Poi(10, EnPoiType.Start), Poi(11, EnPoiType.Exit)),
          Node(2),
          Node(3),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 1, to: 3),
        ],
      };

      // Act: Validate the graph with an over-saturated node.
      var result = Validate(graph);

      // Assert: Failure naming the node, its degree and the cap.
      Assert.False(result.Success);
      Assert.Equal(
        $"Node {Id(1)} has 2 relationships, over its MaxEdges cap of 1.",
        result.FailureReason);
    }

    [Fact]
    public void Validate_NodeExactlyAtMaxEdgesCap_Succeeds()
    {
      // Arrange: Every node sits exactly at its cap of one relationship.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, maxEdges: 1, Poi(10, EnPoiType.Start)),
          Node(2, maxEdges: 1, Poi(11, EnPoiType.Exit)),
        ],
        Edges = [Edge(100, from: 1, to: 2)],
      };

      // Act: Validate the saturated but legal graph.
      var result = Validate(graph);

      // Assert: The cap is inclusive, so the graph stays beatable.
      Assert.True(result.Success);
      Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Validate_NegativeMaxEdges_NeverFails()
    {
      // Arrange: A hub holding many relationships, but its cap is negative - no limit.
      var nodes = new List<LilNode> { Node(1, maxEdges: -1, Poi(10, EnPoiType.Start), Poi(11, EnPoiType.Exit)) };
      var edges = new List<LilEdge>();
      for (var i = 2; i <= 8; i++)
      {
        nodes.Add(Node(i));
        edges.Add(Edge(100 + i, from: 1, to: i));
      }
      var graph = new LilGraph { Nodes = nodes, Edges = edges };

      // Act: Validate the high-degree hub without a limit.
      var result = Validate(graph);

      // Assert: The negative cap disables the check entirely.
      Assert.True(result.Success);
      Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Validate_ZeroMaxEdgesWithoutEdges_Succeeds()
    {
      // Arrange: A single node with a zero cap and no edges to count.
      var graph = new LilGraph
      {
        Nodes = [Node(1, maxEdges: 0, Poi(10, EnPoiType.Start), Poi(11, EnPoiType.Exit))],
      };

      // Act: Validate the edge-less graph.
      var result = Validate(graph);

      // Assert: Zero relationships fit a zero cap.
      Assert.True(result.Success);
      Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Validate_ParallelPassagesBothCountTowardCap()
    {
      // Arrange: Two passages between the same pair; the cap of one cannot hold both.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, maxEdges: 1, Poi(10, EnPoiType.Start)),
          Node(2, maxEdges: 2, Poi(11, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 1, to: 2),
        ],
      };

      // Act: Validate the graph with a parallel passage over the cap.
      var result = Validate(graph);

      // Assert: Both passages count, so the first node reports two relationships.
      Assert.False(result.Success);
      Assert.Equal(
        $"Node {Id(1)} has 2 relationships, over its MaxEdges cap of 1.",
        result.FailureReason);
    }

    [Fact]
    public void Validate_MaxEdgesCapViolation_ReportsBeforeUnreachableExit()
    {
      // Arrange: The exit is unreachable AND a node exceeds its cap; the structural
      // cap violation must win because it runs before the traversal outcome.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, maxEdges: 1, Poi(10, EnPoiType.Start)),
          Node(2, maxEdges: 1, Poi(11, EnPoiType.Exit)),
          Node(3),
          Node(4),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 3),
          Edge(101, from: 1, to: 4),
        ],
      };

      // Act: Validate the doubly broken graph.
      var result = Validate(graph);

      // Assert: The most structural reason is reported first.
      Assert.False(result.Success);
      Assert.Equal(
        $"Node {Id(1)} has 2 relationships, over its MaxEdges cap of 1.",
        result.FailureReason);
    }
  }

  /// <summary>
  /// Runs one validation on a fresh validator instance. A <see cref="GraphValidator"/> holds
  /// per-call state, so instances must not be reused or shared across threads.
  /// </summary>
  private static GraphValidationResult Validate(LilGraph? graph) => new GraphValidator(graph).Validate();
}
