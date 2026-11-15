namespace LostInLimbo.LevelGen.SeedSweep.Data;

/// <summary>
/// Everything measured for one run: props, resolved config values, graph metrics and errors.
/// </summary>
/// <remarks>
/// Mutable so the runner and analyzer can fill it step by step. Every public property except
/// <see cref="Issues"/> becomes one CSV column, in declaration order.
/// </remarks>
internal sealed class RunRecord
{
  #region Props and config

  /// <summary>Absolute run index; -1 for an explicit text seed.</summary>
  public int Index { get; set; }
  /// <summary>Difficulty prop.</summary>
  public int Difficulty { get; set; }
  /// <summary>Many-themes prop.</summary>
  public bool ManyThemes { get; set; }
  /// <summary>Text seed prop.</summary>
  public string Seed { get; set; } = "";
  /// <summary>Resolved number of ordinary nodes.</summary>
  public int Size { get; set; }
  /// <summary>Resolved number of extra (non-tree) passages.</summary>
  public int Connectivity { get; set; }
  /// <summary>Resolved number of obstacles.</summary>
  public int Complexity { get; set; }
  /// <summary>Graph builder name.</summary>
  public string Builder { get; set; } = "";
  /// <summary>Requested secret node amount.</summary>
  public int SecretAmount { get; set; }
  /// <summary>Requested mandatory goal amount.</summary>
  public int GoalAmount { get; set; }
  /// <summary>Requested optional goal amount.</summary>
  public int OptGoalAmount { get; set; }
  /// <summary>Requested style group count.</summary>
  public int GroupCount { get; set; }

  #endregion

  #region Run

  /// <summary>Final generation status.</summary>
  public string Status { get; set; } = "";
  /// <summary>
  /// Generation time reported by the library (<c>GenerationStatus.Elapsed</c>), in milliseconds.
  /// Exact, without polling delay; the time of the first generation only (not the determinism rerun).
  /// </summary>
  public double GenerationMs { get; set; }
  /// <summary>
  /// Wall time of the harness session loop including status polling, in milliseconds. Coarse
  /// (polling sleeps round up to the OS timer tick); kept for comparison.
  /// </summary>
  public double WallMs { get; set; }
  /// <summary>
  /// Position of this run among all runs (warm-up included) executed on its harness thread in
  /// this process; 1 = the thread's first run.
  /// </summary>
  public int ThreadRunOrdinal { get; set; }
  /// <summary>
  /// Methods the JIT compiled while this run was generated. Process-wide: in parallel sweeps it
  /// may include compilation triggered by concurrent runs.
  /// </summary>
  public long JitMethods { get; set; }
  /// <summary>
  /// Highest garbage-collection generation collected while this run was generated; -1 when none.
  /// Process-wide, like <see cref="JitMethods"/>.
  /// </summary>
  public int GcGeneration { get; set; } = -1;

  #endregion

  #region Graph metrics

  /// <summary>All nodes, including secret ones.</summary>
  public int Nodes { get; set; }
  /// <summary>Secret nodes.</summary>
  public int SecretNodes { get; set; }
  /// <summary>All passages.</summary>
  public int Edges { get; set; }
  /// <summary>Secret passages.</summary>
  public int SecretEdges { get; set; }
  /// <summary>One-way passages.</summary>
  public int OneWayEdges { get; set; }
  /// <summary>Ordinary passages beyond a spanning tree: <c>(Edges - SecretEdges) - (Size - 1)</c>.</summary>
  public int ExtraEdges { get; set; }
  /// <summary>Solution POIs (one per obstacle).</summary>
  public int Solutions { get; set; }
  /// <summary>Mandatory goal POIs.</summary>
  public int Goals { get; set; }
  /// <summary>Optional goal POIs.</summary>
  public int OptGoals { get; set; }
  /// <summary>Ordinary nodes without any goal POI, after placement.</summary>
  public int FreeNodes { get; set; }
  /// <summary>Mandatory goals on the start or exit node.</summary>
  public int GoalsOnStartExit { get; set; }
  /// <summary>Optional goals on the start or exit node.</summary>
  public int OptGoalsOnStartExit { get; set; }
  /// <summary>Solutions in the start node.</summary>
  public int SolutionsInStart { get; set; }
  /// <summary>Mandatory goals whose node also holds a solution.</summary>
  public int GoalsWithSolution { get; set; }
  /// <summary>Start and exit are in the same node.</summary>
  public bool SharedStartExit { get; set; }
  /// <summary>Exit is reachable holding no solution (secret passages count).</summary>
  public bool ExitOpen { get; set; }
  /// <summary>Exit is reachable holding no solution over ordinary passages only.</summary>
  public bool ExitOpenNoSecret { get; set; }
  /// <summary>Mandatory goals reachable holding no solution.</summary>
  public int GoalsOpen { get; set; }
  /// <summary>Share of nodes reachable holding no solution.</summary>
  public double OpenReachShare { get; set; }
  /// <summary>Conditioned one-way passages that are not secret.</summary>
  public int ConditionedOneWayNonSecret { get; set; }
  /// <summary>Solutions whose node is an endpoint of a passage they unlock.</summary>
  public int KeyNextToDoor { get; set; }
  /// <summary>Like <see cref="KeyNextToDoor"/>, with the solution in the start node.</summary>
  public int KeyAtStartDoor { get; set; }
  /// <summary>Passages with at least one condition.</summary>
  public int ConditionedEdges { get; set; }
  /// <summary>Highest condition count on one passage.</summary>
  public int MaxConditionsPerEdge { get; set; }
  /// <summary>Passages with more than one condition.</summary>
  public int StackedEdges { get; set; }
  /// <summary>Conditioned one-way passages.</summary>
  public int ConditionedOneWay { get; set; }
  /// <summary>Conditioned secret passages.</summary>
  public int ConditionedSecret { get; set; }
  /// <summary>Solutions no passage requires.</summary>
  public int OrphanSolutions { get; set; }
  /// <summary>Solutions whose absence (holding all others) changes no reach: bypassed locks.</summary>
  public int UselessObstacles { get; set; }
  /// <summary>Distinct style groups, a missing group counted as one.</summary>
  public int DistinctGroups { get; set; }
  /// <summary>Nodes without a style group.</summary>
  public int NullGroupNodes { get; set; }
  /// <summary>Smallest node RelSize.</summary>
  public int RelSizeMin { get; set; }
  /// <summary>Largest node RelSize.</summary>
  public int RelSizeMax { get; set; }

  #endregion

  /// <summary>Errors found in this run; empty for a clean run.</summary>
  public List<string> Issues { get; } = [];

  /// <summary>True when the run produced a graph that was analyzed.</summary>
  public bool Generated => Status == "Paused";

  /// <summary>Records an error.</summary>
  /// <param name="message">Error description.</param>
  public void Error(string message) => Issues.Add(message);

  /// <summary>Short identification for report examples, e.g. <c>#12 d5 Simple S7</c>.</summary>
  public string Label => $"#{Index} d{Difficulty} {Builder} S{Size}";
}
