using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop;

namespace LostInLimbo.LevelGen.Tests.Prop;

/// <summary>
/// Sweeps <see cref="ConfigGenerator"/> across every allowed difficulty (0 through
/// <see cref="PropMain.MaxDifficulty"/>) with many seeded generations each. Asserts that
/// every generated configuration validates and keeps to hard caps and relational invariants.
/// </summary>
/// <remarks>
/// Gameplay focuses on low difficulties, but the whole allowed range must still produce valid
/// data. All runs are deterministic: seed = <c>difficulty * SeedsPerDifficulty + i</c>, so any
/// reported (difficulty, seed) pair reproduces the exact configuration. Violations are
/// collected per difficulty and reported together, so one failure does not hide others.
/// </remarks>
[Trait("Category", "Stress")]
public sealed class ConfigGeneratorHighDifficultyTests
{
  /// <summary>Number of seeded generations per difficulty.</summary>
  private const int SeedsPerDifficulty = 256;

  /// <summary>Fixed text seed so only the supporting draw stream varies across the sweep.</summary>
  private const string TextSeed = "mordor";

  /// <summary>Maximum number of violations listed in a failure message.</summary>
  private const int MaxReportedViolations = 5;

  /// <summary>Every allowed difficulty, 0 through <see cref="PropMain.MaxDifficulty"/> inclusive.</summary>
  public static TheoryData<int> AllDifficulties()
  {
    var data = new TheoryData<int>();
    for (int difficulty = 0; difficulty <= PropMain.MaxDifficulty; difficulty++)
      data.Add(difficulty);
    return data;
  }

  [Theory]
  [MemberData(nameof(AllDifficulties))]
  public void Execute_WithSeedSweepAtDifficulty_ProducesOnlyValidConfigurations(int difficulty)
  {
    // Arrange: Violation buffer for this difficulty.
    var violations = new List<string>();

    for (int i = 0; i < SeedsPerDifficulty; i++)
    {
      // Arrange: Unique, reproducible supporting seed per (difficulty, iteration).
      long seed = (long)difficulty * SeedsPerDifficulty + i;
      var propMain = new PropMain { TextSeed = TextSeed, Difficulty = difficulty, ManyThemes = true };

      // Act: Generate the configuration.
      var config = new ConfigGenerator(propMain, seed).Execute();

      // Assert: Collect every violated rule for this seed.
      foreach (var violation in CheckConfiguration(config, difficulty))
        violations.Add($"seed {seed}: {violation}");
    }

    // Assert: No seed produced an invalid configuration.
    if (violations.Count > 0)
    {
      var sample = string.Join(Environment.NewLine, violations.Take(MaxReportedViolations));
      Assert.Fail(
        $"Difficulty {difficulty}: {violations.Count} violation(s) across {SeedsPerDifficulty} seeds. " +
        $"First {Math.Min(MaxReportedViolations, violations.Count)}:{Environment.NewLine}{sample}");
    }
  }

  /// <summary>
  /// Checks one generated configuration against validation, hard caps and relational invariants.
  /// </summary>
  /// <param name="config">Generated configuration.</param>
  /// <param name="difficulty">Difficulty the configuration was generated for.</param>
  /// <returns>Descriptions of every violated rule; empty when the configuration is valid.</returns>
  private static List<string> CheckConfiguration(CnfLevelGeneration config, int difficulty)
  {
    var violations = new List<string>();
    var graph = config.Graph;
    var objectives = graph.ProcOpts.NodeObjectives;

    // Full validation; record the message instead of throwing so the sweep continues.
    try
    {
      config.Validate();
    }
    catch (ArgumentException exception)
    {
      violations.Add($"Validate threw {exception.GetType().Name}: {exception.Message}");
    }

    // Hard caps, checked separately so the report names the offending value directly.
    if (graph.Size > CnfGraph.MaxSize)
      violations.Add($"Size {graph.Size} > CnfGraph.MaxSize {CnfGraph.MaxSize}");
    if (graph.ProcOpts.RelSecrets.Amount > CnfProcRelSecretsOpt.MaxAmount)
      violations.Add($"RelSecrets.Amount {graph.ProcOpts.RelSecrets.Amount} > MaxAmount {CnfProcRelSecretsOpt.MaxAmount}");

    // Relational invariants of the resolvers, valid across the whole difficulty range.
    int sizeMax = Math.Min(difficulty + 2, CnfGraph.MaxSize);
    if (graph.Size < 2 || graph.Size > sizeMax)
      violations.Add($"Size {graph.Size} outside [2, {sizeMax}]");
    if (graph.Connectivity < 0 || graph.Connectivity > difficulty + graph.Size - 1)
      violations.Add($"Connectivity {graph.Connectivity} outside [0, {difficulty + graph.Size - 1}]");
    if (graph.Complexity < 0 || graph.Complexity >= graph.Size)
      violations.Add($"Complexity {graph.Complexity} outside [0, Size {graph.Size})");
    int goalMax = Math.Max(1, difficulty / 2);
    if (objectives.GoalAmount < 1 || objectives.GoalAmount > goalMax)
      violations.Add($"GoalAmount {objectives.GoalAmount} outside [1, {goalMax}]");
    if (objectives.OptGoalAmount < 0 || objectives.OptGoalAmount >= graph.Size)
      violations.Add($"OptGoalAmount {objectives.OptGoalAmount} outside [0, Size {graph.Size})");

    // General section.
    float degeneration = config.General.Degeneration;
    if (float.IsNaN(degeneration) || float.IsInfinity(degeneration) || degeneration < 0f)
      violations.Add($"Degeneration {degeneration} is not finite and >= 0");

    // OneNode stays excluded from random builder picking.
    if (graph.GraphBuilder.Builder == EnGraphBuilder.OneNode)
      violations.Add("Builder is OneNode");

    return violations;
  }
}
