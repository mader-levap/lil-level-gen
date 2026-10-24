using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Config.Graph.Validator;
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
/// (flood-fill reachability), conditions (key/door solvability) and goal beatability
/// (one-way passages vs. mandatory objectives). Test graphs are
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
      Assert.Equal("Graph has no Start POI.", result.FailureReason);
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
      Assert.Equal("Graph has no Exit POI.", result.FailureReason);
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

    [Fact]
    public void Validate_EmptyNodeId_Fails()
    {
      // Arrange: Single node whose identifier is Guid.Empty (Id(0) maps to the empty Guid).
      var graph = new LilGraph
      {
        Nodes = [Node(0, Poi(10, EnPoiType.Start), Poi(11, EnPoiType.Exit))],
      };

      // Act: Validate the graph with an unassigned node id.
      var result = Validate(graph);

      // Assert: Failure naming the empty node identifier.
      Assert.False(result.Success);
      Assert.Equal($"Node {Id(0)} has an empty id.", result.FailureReason);
    }

    [Fact]
    public void Validate_EmptyPoiId_Fails()
    {
      // Arrange: Node holds a POI whose identifier is Guid.Empty.
      var graph = new LilGraph
      {
        Nodes = [Node(1, Poi(0, EnPoiType.Start), Poi(11, EnPoiType.Exit))],
      };

      // Act: Validate the graph with an unassigned POI id.
      var result = Validate(graph);

      // Assert: Failure naming the empty POI identifier, not a duplicate-id complaint.
      Assert.False(result.Success);
      Assert.Equal($"POI {Id(0)} has an empty id.", result.FailureReason);
    }

    [Fact]
    public void Validate_EmptyEdgeId_Fails()
    {
      // Arrange: Edge whose identifier is Guid.Empty.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(11, EnPoiType.Exit)),
        ],
        Edges = [Edge(0, from: 1, to: 2)],
      };

      // Act: Validate the graph with an unassigned edge id.
      var result = Validate(graph);

      // Assert: Failure naming the empty edge identifier.
      Assert.False(result.Success);
      Assert.Equal($"Edge {Id(0)} has an empty id.", result.FailureReason);
    }

    [Fact]
    public void Validate_DuplicateEdgeId_Fails()
    {
      // Arrange: Two distinct parallel passages sharing one identifier.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(11, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(100, from: 1, to: 2),
        ],
      };

      // Act: Validate the graph with ambiguous edge identity.
      var result = Validate(graph);

      // Assert: Failure naming the duplicated identifier.
      Assert.False(result.Success);
      Assert.Equal($"Duplicate edge id {Id(100)}.", result.FailureReason);
    }

    [Fact]
    public void Validate_ConditionWithEmptyRequiresPoi_Fails()
    {
      // Arrange: Condition whose RequiresPoi is Guid.Empty instead of a real POI reference.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(11, EnPoiType.Exit)),
        ],
        Edges = [Edge(100, from: 1, to: 2, conditions: [Condition(0)])],
      };

      // Act: Validate the graph with the empty condition requirement.
      var result = Validate(graph);

      // Assert: Failure reports the empty id directly instead of calling it missing.
      Assert.False(result.Success);
      Assert.Equal($"Condition on edge {Id(100)} has an empty RequiresPoi.", result.FailureReason);
    }

    [Fact]
    public void Validate_ConditionOnNonSolutionPoi_Fails()
    {
      // Arrange: A collectible Goal POI satisfies the passage in traversal terms, but a
      // RequiresSolution condition may only reference a Solution POI.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start), Poi(12, EnPoiType.Goal)),
          Node(2, Poi(11, EnPoiType.Exit)),
        ],
        Edges = [Edge(100, from: 1, to: 2, conditions: [Condition(12)])],
      };

      // Act: Validate the graph whose condition targets a Goal instead of a Solution.
      var result = Validate(graph);

      // Assert: Failure naming the edge, the POI and its offending type.
      Assert.False(result.Success);
      Assert.Equal(
        $"Condition on edge {Id(100)} references non-Solution POI {Id(12)} (type Goal).",
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

      // Assert: Failure reports the unreachable exit. Note this error takes precedence over the orphan node.
      Assert.False(result.Success);
      Assert.Equal("Exit POI is not reachable from Start.", result.FailureReason);
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
      Assert.Equal("Exit POI is not reachable from Start.", result.FailureReason);
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

    [Fact]
    public void Validate_ConditionedEdgeInsideDisconnectedBranch_ReportsUnreachableNodes()
    {
      // Arrange: Start and exit share node 1; a dead branch (nodes 2 and 3) carries a gated
      // edge whose solution lives inside that same branch, so nothing in it is ever reached.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start), Poi(11, EnPoiType.Exit)),
          Node(2),
          Node(3, Poi(12, EnPoiType.Solution)),
        ],
        Edges = [Edge(100, from: 2, to: 3, conditions: [Condition(12)])],
      };

      // Act: Validate the graph with a conditioned edge inside the unreachable branch.
      var result = Validate(graph);

      // Assert: The disconnected branch is the root cause; the gate is not a softlock
      // because the player can never stand at it.
      Assert.False(result.Success);
      Assert.Equal("2 node(s) unreachable from Start.", result.FailureReason);
    }

    [Fact]
    public void Validate_ConditionedEdgeBesideUnreachableExit_ReportsUnreachableExit()
    {
      // Arrange: The exit sits in a dead branch whose internal edge is conditioned on a
      // solution from that same branch; the player reaches neither side of the gate.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(11, EnPoiType.Exit)),
          Node(3, Poi(12, EnPoiType.Solution)),
        ],
        Edges = [Edge(100, from: 2, to: 3, conditions: [Condition(12)])],
      };

      // Act: Validate the graph whose unreachable region contains a gated edge.
      var result = Validate(graph);

      // Assert: The unreachable exit wins over the gate the player never encounters.
      Assert.False(result.Success);
      Assert.Equal("Exit POI is not reachable from Start.", result.FailureReason);
    }

    [Fact]
    public void Validate_FrontierGateWithKeyInOrphanComponent_ReportsSoftlock()
    {
      // Arrange: Node 2 is reachable from the start, but its gate needs a key parked in the
      // disconnected node 4 - the player stands at the gate and can never open it.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start), Poi(11, EnPoiType.Exit)),
          Node(2),
          Node(3),
          Node(4, Poi(12, EnPoiType.Solution)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 2, to: 3, conditions: [Condition(12)]),
        ],
      };

      // Act: Validate the graph with a reachable but unsatisfiable gate.
      var result = Validate(graph);

      // Assert: A gate on the reachable frontier keeps the softlock diagnosis.
      Assert.False(result.Success);
      Assert.Equal($"Conditioned edge {Id(101)} can never be satisfied.", result.FailureReason);
    }

    [Fact]
    public void Validate_OneWayGateWithUnreachableEntrance_ReportsUnreachableNodes()
    {
      // Arrange: A one-way passage leaves the orphan node 2 towards the start, yet its
      // solution sits in node 2 itself - the player can never approach the entrance.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start), Poi(11, EnPoiType.Exit)),
          Node(2, Poi(12, EnPoiType.Solution)),
        ],
        Edges = [Edge(100, from: 2, to: 1, EnPassageDirection.OneWay, conditions: [Condition(12)])],
      };

      // Act: Validate the graph with a one-way gated edge behind the player.
      var result = Validate(graph);

      // Assert: One-way direction matters: only the unreachable node is reported.
      Assert.False(result.Success);
      Assert.Equal("1 node(s) unreachable from Start.", result.FailureReason);
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

  /// <summary>Tests for the goal-in-secret rule and its deliberate exceptions.</summary>
  public sealed class GoalPoiTests
  {
    [Fact]
    public void Validate_GoalPoiInSecretNode_Fails()
    {
      // Arrange: A beatable graph whose secret node hosts a mandatory goal.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(11, EnPoiType.Exit)),
          Node(3, Poi(12, EnPoiType.Goal)) with { Secret = true },
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 2, to: 3, secret: true),
        ],
      };

      // Act: Validate the graph with the mandatory goal in the secret node.
      var result = Validate(graph);

      // Assert: Failure with the dedicated reason.
      Assert.False(result.Success);
      Assert.Equal($"Goal POI {Id(12)} is in secret node {Id(3)}.", result.FailureReason);
    }

    [Fact]
    public void Validate_OptionalGoalPoiInSecretNode_Succeeds()
    {
      // Arrange: The same beatable graph, but the secret node hosts an optional goal.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(11, EnPoiType.Exit)),
          Node(3, Poi(12, EnPoiType.OptionalGoal)) with { Secret = true },
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 2, to: 3, secret: true),
        ],
      };

      // Act: Validate the graph with the optional goal in the secret node.
      var result = Validate(graph);

      // Assert: An optional goal in a secret node is explicitly allowed.
      Assert.True(result.Success, result.FailureReason);
    }

    [Fact]
    public void Validate_GoalPoiInOrdinaryNode_Succeeds()
    {
      // Arrange: A beatable two-node graph whose ordinary node hosts a mandatory goal.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start), Poi(12, EnPoiType.Goal)),
          Node(2, Poi(11, EnPoiType.Exit)),
        ],
        Edges = [Edge(100, from: 1, to: 2)],
      };

      // Act: Validate the graph with the mandatory goal in an ordinary node.
      var result = Validate(graph);

      // Assert: The rule only fires for secret nodes.
      Assert.True(result.Success, result.FailureReason);
    }

    [Fact]
    public void Validate_MultipleGoalPoisInOneOrdinaryNode_Succeeds()
    {
      // Arrange: A beatable two-node graph whose ordinary node hosts a Start, a
      // mandatory goal and an optional goal at once - multiplicity is not the rule's
      // business.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start), Poi(12, EnPoiType.Goal), Poi(13, EnPoiType.OptionalGoal)),
          Node(2, Poi(11, EnPoiType.Exit)),
        ],
        Edges = [Edge(100, from: 1, to: 2)],
      };

      // Act: Validate the graph with two goals on one node.
      var result = Validate(graph);

      // Assert: Multiple goals per node are fine as far as the validator is concerned.
      Assert.True(result.Success, result.FailureReason);
    }

    [Fact]
    public void Validate_GoalPoiInSecretNode_ReportsBeforeUnreachableExit()
    {
      // Arrange: The exit is unreachable AND a secret node hosts a mandatory goal; the
      // goal-in-secret problem must win because it is structural and runs before the
      // traversal outcome.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(11, EnPoiType.Exit)),
          Node(3, Poi(12, EnPoiType.Goal)) with { Secret = true },
        ],
        Edges =
        [
          Edge(100, from: 1, to: 3, secret: true),
        ],
      };

      // Act: Validate the doubly broken graph.
      var result = Validate(graph);

      // Assert: The structural reason is reported first.
      Assert.False(result.Success);
      Assert.Equal($"Goal POI {Id(12)} is in secret node {Id(3)}.", result.FailureReason);
    }
  }

  /// <summary>
  /// Tests that a mandatory objective stays completable around one-way passages: a
  /// passage leading toward the exit must not strand a <see cref="EnPoiType.Goal"/> POI on
  /// its near side, and a passage into a region holding mandatory content must not lead
  /// past the point of no return with no exit beyond it, because the exit only works once
  /// every non-optional objective is done. Visiting a goal node does not count as
  /// completing it. Voluntary dead ends holding only optional content stay legal, and
  /// content that is unreachable with or without the crossing keeps the CheckExits
  /// reasons.
  /// </summary>
  public sealed class GoalBeatabilityTests
  {
    [Fact]
    public void Validate_GoalBehindOneWayTowardExit_Fails()
    {
      // Arrange: Start <-> goal room -> one-way -> exit. The player can walk through the
      // goal room without completing its objective, cross the point of no return and find
      // an exit that will not work - with no way back to finish the objective.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(12, EnPoiType.Goal)),
          Node(3, Poi(11, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 2, to: 3, EnPassageDirection.OneWay),
        ],
      };

      // Act: Validate the graph with the stranded mandatory goal.
      var result = Validate(graph);

      // Assert: Failure names the stranded goal and the one-way passage that locks it out.
      Assert.False(result.Success);
      Assert.Equal(
        $"Goal POI {Id(12)} is stranded behind one-way passage {Id(101)}.",
        result.FailureReason);
    }

    [Fact]
    public void Validate_GoalOnExitSideOfOneWay_Succeeds()
    {
      // Arrange: The same shape, but the mandatory goal sits with the exit beyond the
      // one-way, so crossing the point of no return strands nothing.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2),
          Node(3, Poi(11, EnPoiType.Exit), Poi(12, EnPoiType.Goal)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 2, to: 3, EnPassageDirection.OneWay),
        ],
      };

      // Act: Validate the graph whose goal lies beyond the one-way.
      var result = Validate(graph);

      // Assert: The goal remains reachable from the far side, so the graph is fine.
      Assert.True(result.Success, result.FailureReason);
    }

    [Fact]
    public void Validate_ReturnPathRescuesGoal_Succeeds()
    {
      // Arrange: Start <-> goal room, one-way to the exit room, plus a backlink from the
      // exit room to the start. Crossing the one-way does not lock the goal out because
      // the player can walk back and complete it.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(12, EnPoiType.Goal)),
          Node(3, Poi(11, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 2, to: 3, EnPassageDirection.OneWay),
          Edge(102, from: 3, to: 1),
        ],
      };

      // Act: Validate the graph with a way back after the one-way.
      var result = Validate(graph);

      // Assert: The goal is reachable from the far side via the return path.
      Assert.True(result.Success, result.FailureReason);
    }

    [Fact]
    public void Validate_OptionalGoalBehindOneWay_Succeeds()
    {
      // Arrange: The stranded shape, but the objective is optional - only non-optional
      // objectives gate the exit, so skipping it is the player's choice.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(12, EnPoiType.OptionalGoal)),
          Node(3, Poi(11, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 2, to: 3, EnPassageDirection.OneWay),
        ],
      };

      // Act: Validate the graph with only an optional goal behind the one-way.
      var result = Validate(graph);

      // Assert: An optional goal may be left behind without softlocking the level.
      Assert.True(result.Success, result.FailureReason);
    }

    [Fact]
    public void Validate_GoalOnStartNodeBehindOneWay_Fails()
    {
      // Arrange: The mandatory goal sits on the start node itself and a one-way leads to
      // the exit. There is no exception for start-side goals.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start), Poi(12, EnPoiType.Goal)),
          Node(2, Poi(11, EnPoiType.Exit)),
        ],
        Edges = [Edge(100, from: 1, to: 2, EnPassageDirection.OneWay)],
      };

      // Act: Validate the graph with the goal stranded on the start side.
      var result = Validate(graph);

      // Assert: Failure names the stranded goal and the locking one-way passage.
      Assert.False(result.Success);
      Assert.Equal(
        $"Goal POI {Id(12)} is stranded behind one-way passage {Id(100)}.",
        result.FailureReason);
    }

    [Fact]
    public void Validate_OneWayNotTowardExit_WithStrandedGoal_Succeeds()
    {
      // Arrange: A one-way from the goal room into an exit-less dead end. Crossing it
      // strands the player away from the goal, but no exit lies beyond, so the exit rule
      // does not fire - one-ways that do not lead toward the exit are allowed.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(12, EnPoiType.Goal)),
          Node(3, Poi(11, EnPoiType.Exit)),
          Node(4),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 1, to: 3),
          Edge(102, from: 2, to: 4, EnPassageDirection.OneWay),
        ],
      };

      // Act: Validate the graph with the exit-less one-way dead end.
      var result = Validate(graph);

      // Assert: Only exit-bound one-way passages are checked, so this graph is fine.
      Assert.True(result.Success, result.FailureReason);
    }

    [Fact]
    public void Validate_GoalStrandedByFirstOfTwoOneWays_Fails()
    {
      // Arrange: Two chained one-way passages lead to the exit; the goal lies behind both.
      // The first one-way in edge-list order strands it and must be the one reported.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(12, EnPoiType.Goal)),
          Node(3),
          Node(4, Poi(11, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 2, to: 3, EnPassageDirection.OneWay),
          Edge(102, from: 3, to: 4, EnPassageDirection.OneWay),
        ],
      };

      // Act: Validate the graph with the chained one-way passages.
      var result = Validate(graph);

      // Assert: The first offending one-way passage is reported, pinning list-order determinism.
      Assert.False(result.Success);
      Assert.Equal(
        $"Goal POI {Id(12)} is stranded behind one-way passage {Id(101)}.",
        result.FailureReason);
    }

    [Fact]
    public void Validate_MandatoryGoalBeyondOneWayWithoutExitPath_Fails()
    {
      // Arrange: Start <-> exit room, plus a one-way into the room holding the mandatory
      // goal. The goal forces the player across the point of no return, the one-way
      // forbids leaving it, and no exit lies beyond - a forced softlock the near-side
      // exit cannot rescue.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(12, EnPoiType.Goal)),
          Node(3, Poi(11, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2, EnPassageDirection.OneWay),
          Edge(101, from: 1, to: 3),
        ],
      };

      // Act: Validate the graph with the trapped mandatory goal.
      var result = Validate(graph);

      // Assert: Failure names the trapped goal and the locking one-way passage.
      Assert.False(result.Success);
      Assert.Equal(
        $"Goal POI {Id(12)} is trapped behind one-way passage {Id(100)} (far side cannot reach the Exit).",
        result.FailureReason);
    }

    [Fact]
    public void Validate_ReferencedSolutionBeyondOneWayWithoutExitPath_Fails()
    {
      // Arrange: The key behind a one-way is the only thing that opens the only door to
      // the exit. CheckTraversals crosses the passage, picks the key up and opens the
      // door, so the graph looks fine there - but the player can never come back to use
      // it, which is exactly the softlock this check exists for.
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
          Edge(100, from: 1, to: 2, EnPassageDirection.OneWay),
          Edge(101, from: 1, to: 3, conditions: [Condition(12)]),
        ],
      };

      // Act: Validate the graph whose exit key sits beyond the point of no return.
      var result = Validate(graph);

      // Assert: Failure names the trapped solution and the locking one-way passage.
      Assert.False(result.Success);
      Assert.Equal(
        $"Solution POI {Id(12)} is trapped behind one-way passage {Id(100)} (far side cannot reach the Exit).",
        result.FailureReason);
    }

    [Fact]
    public void Validate_SolutionBeyondOneWayGatesNearSideGoal_Fails()
    {
      // Arrange: The exit sits right next to the start, but the mandatory goal lies
      // behind a door whose key sits beyond a one-way. Getting the key is forced, using
      // it is impossible; since no goal lies beyond the passage, the trapped solution is
      // the POI to name.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(12, EnPoiType.Solution)),
          Node(3, Poi(13, EnPoiType.Goal)),
          Node(4, Poi(11, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 4),
          Edge(101, from: 1, to: 2, EnPassageDirection.OneWay),
          Edge(102, from: 1, to: 3, conditions: [Condition(12)]),
        ],
      };

      // Act: Validate the graph whose goal key sits beyond the point of no return.
      var result = Validate(graph);

      // Assert: Failure names the trapped solution and the locking one-way passage.
      Assert.False(result.Success);
      Assert.Equal(
        $"Solution POI {Id(12)} is trapped behind one-way passage {Id(101)} (far side cannot reach the Exit).",
        result.FailureReason);
    }

    [Fact]
    public void Validate_SolutionBeyondOneWayGatingOnlyRedundantPassage_Succeeds()
    {
      // Arrange: A key beyond a one-way only opens a second, redundant passage to the
      // exit - the open one already wins the level. The crossing is never necessary, so
      // even a condition-referenced solution out there must not be mistaken for
      // must-collect content.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(12, EnPoiType.Solution), Poi(13, EnPoiType.OptionalGoal)),
          Node(3, Poi(11, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2, EnPassageDirection.OneWay),
          Edge(101, from: 1, to: 3),
          Edge(102, from: 1, to: 3, conditions: [Condition(12)]),
        ],
      };

      // Act: Validate the graph with a non-essential key beyond the one-way.
      var result = Validate(graph);

      // Assert: The level is winnable without ever crossing, so the dead end stays legal.
      Assert.True(result.Success, result.FailureReason);
    }

    [Fact]
    public void Validate_OptionalGoalBeyondOneWayWithoutExitPath_Succeeds()
    {
      // Arrange: The one-way leads into an exit-less dead end holding only an optional
      // objective; the exit stays reachable without crossing, so nothing forces the
      // player into the trap.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(12, EnPoiType.OptionalGoal)),
          Node(3, Poi(11, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2, EnPassageDirection.OneWay),
          Edge(101, from: 1, to: 3),
        ],
      };

      // Act: Validate the graph with only an optional objective beyond the one-way.
      var result = Validate(graph);

      // Assert: Optional objectives never gate the exit, so the dead end stays legal.
      Assert.True(result.Success, result.FailureReason);
    }

    [Fact]
    public void Validate_GapOneWayWithDisconnectedGoal_ReportsUnreachable()
    {
      // Arrange: An innocent exit-less dead end next to a totally disconnected goal node.
      // The winnable-without-crossing flood fails because of the orphan, not the one-way,
      // so the shortfall must not be blamed on the passage.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(11, EnPoiType.Exit)),
          Node(3, Poi(12, EnPoiType.Goal)),
          Node(4),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 4, EnPassageDirection.OneWay),
          Edge(101, from: 1, to: 2),
        ],
      };

      // Act: Validate the graph where the orphan is the real root cause.
      var result = Validate(graph);

      // Assert: The unreachable region keeps the report instead of the innocent passage.
      Assert.False(result.Success);
      Assert.Equal("1 node(s) unreachable from Start.", result.FailureReason);
    }

    [Fact]
    public void Validate_GapOneWayWithUnreachableExit_ReportsUnreachableExit()
    {
      // Arrange: The trapped-goal shape, but the exit itself is a disconnected island.
      // The unreachable exit is the root cause of everything and must own the report, so
      // the one-way check bails out before it can claim a trap.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(12, EnPoiType.Goal)),
          Node(3, Poi(11, EnPoiType.Exit)),
        ],
        Edges = [Edge(100, from: 1, to: 2, EnPassageDirection.OneWay)],
      };

      // Act: Validate the graph whose exit is broken outright.
      var result = Validate(graph);

      // Assert: The guard lets CheckExits report the unreachable exit first.
      Assert.False(result.Success);
      Assert.Equal("Exit POI is not reachable from Start.", result.FailureReason);
    }

    [Fact]
    public void Validate_GoalDisconnectedBeyondExitBoundOneWay_ReportsUnreachable()
    {
      // Arrange: A one-way leads straight to the exit, but the only mandatory goal is a
      // disconnected island - unreachable with or without crossing. The stranded report
      // must only consider goals the player can actually reach today.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(11, EnPoiType.Exit)),
          Node(3, Poi(12, EnPoiType.Goal)),
        ],
        Edges = [Edge(100, from: 1, to: 2, EnPassageDirection.OneWay)],
      };

      // Act: Validate the graph with an orphaned goal behind an exit-bound one-way.
      var result = Validate(graph);

      // Assert: The orphan keeps the unreachable report instead of a false strand claim.
      Assert.False(result.Success);
      Assert.Equal("1 node(s) unreachable from Start.", result.FailureReason);
    }

    [Fact]
    public void Validate_ExitLessOneWayDeadEnd_Allowed_Succeeds()
    {
      // Arrange: Start reaches an exit directly, plus an open one-way into an empty dead end.
      // With dead ends allowed (default), the voluntary one-way is fine.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(11, EnPoiType.Exit)),
          Node(3),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 1, to: 3, EnPassageDirection.OneWay),
        ],
      };

      // Act: Validate with default options.
      var result = Validate(graph);

      // Assert: The empty dead end is allowed.
      Assert.True(result.Success, result.FailureReason);
      Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Validate_ExitLessOneWayDeadEnd_Disallowed_Fails()
    {
      // Arrange: Same empty dead end, but one-way dead ends are disallowed.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start)),
          Node(2, Poi(11, EnPoiType.Exit)),
          Node(3),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 1, to: 3, EnPassageDirection.OneWay),
        ],
      };

      // Act: Validate with dead ends disallowed.
      var result = Validate(graph, CnfValidGraphOpt.DefEnabled with { AllowOneWayDeadEnds = false });

      // Assert: The one-way is a point of no return even though the dead end is empty.
      Assert.False(result.Success);
      Assert.Equal(
        $"One-way passage {Id(101)} is a point of no return: the far side cannot reach the Exit.",
        result.FailureReason);
    }

    [Fact]
    public void Validate_SolvableGateInsideDisallowedDeadEnd_Fails()
    {
      // Arrange: Start holds the key, a one-way leads into an empty dead end containing a
      // solvable conditioned gate. Even though the gate can be opened, the dead end itself
      // has no exit, so disallowing dead ends rejects the whole one-way.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start), Poi(12, EnPoiType.Solution)),
          Node(2, Poi(11, EnPoiType.Exit)),
          Node(3),
          Node(4),
        ],
        Edges =
        [
          Edge(100, from: 1, to: 2),
          Edge(101, from: 1, to: 3, EnPassageDirection.OneWay),
          Edge(102, from: 3, to: 4, conditions: [Condition(12)]),
        ],
      };

      // Act: Validate with dead ends disallowed.
      var result = Validate(graph, CnfValidGraphOpt.DefEnabled with { AllowOneWayDeadEnds = false });

      // Assert: The far side of the one-way holds no exit, so the passage itself is rejected
      // before the conditioned gate is ever considered.
      Assert.False(result.Success);
      Assert.Equal(
        $"One-way passage {Id(101)} is a point of no return: the far side cannot reach the Exit.",
        result.FailureReason);
    }
  }

  /// <summary>
  /// Runs one validation on a fresh validator instance using default options with enabled validator.
  /// A <see cref="GraphValidator"/> holds per-call state, so instances must not be reused or shared across threads.
  /// </summary>
  /// <param name="graph">Graph to validate.</param>
  private static LilResult Validate(LilGraph? graph)
    => Validate(graph, CnfValidGraphOpt.DefEnabled);


  /// <summary>
  /// Runs one validation on a fresh validator instance using given validator options.
  /// A <see cref="GraphValidator"/> holds per-call state, so instances must not be reused or shared across threads.
  /// </summary>
  /// <param name="graph">Graph to validate.</param>
  /// <param name="options">Validator options.</param>
  private static LilResult Validate(LilGraph? graph, CnfValidGraphOpt options)
    => new GraphValidator(graph, options).Validate();
}
