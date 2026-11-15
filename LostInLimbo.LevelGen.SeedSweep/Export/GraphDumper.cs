using System.Globalization;
using System.Text;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.SeedSweep.Data;

namespace LostInLimbo.LevelGen.SeedSweep.Export;

/// <summary>
/// Markdown dump of one run: heading, config summary, then (in a code block), nodes with POIs, passages with conditions.
/// </summary>
/// <remarks>
/// Nodes are named <c>N0..</c> and solutions <c>k0..</c> in graph order. Passages print as
/// <c>N0 -- N1</c> (bidirectional) or <c>N0 -&gt; N1</c> (one-way), followed by markers.
/// </remarks>
internal static class GraphDumper
{
  /// <summary>
  /// Dumps a run.
  /// </summary>
  /// <param name="spec">Run props.</param>
  /// <param name="config">Resolved config.</param>
  /// <param name="graph">Generated graph; null when generation failed.</param>
  /// <param name="record">Analyzed record; its errors are listed.</param>
  /// <returns>Dump text.</returns>
  public static string Dump(RunSpec spec, CnfLevelGeneration config, LilGraph? graph, RunRecord record)
  {
    if (spec is null) throw new ArgumentNullException(nameof(spec));
    if (config is null) throw new ArgumentNullException(nameof(config));
    if (record is null) throw new ArgumentNullException(nameof(record));

    var text = new StringBuilder();
    AppendConfig(text, spec, config);
    text.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Generation time: {0:F2} ms (wall {1:F1} ms)", record.GenerationMs, record.WallMs));
    text.AppendLine();
    if (graph is null)
    {
      text.AppendLine($"No graph: {record.Status}.");
    }
    else
    {
      text.AppendLine("```text");
      AppendGraph(text, graph);
      text.AppendLine("```");
    }

    text.AppendLine();
    if (record.Issues.Count == 0)
    {
      text.AppendLine("Errors: none.");
    }
    else
    {
      text.AppendLine("Errors:");
      foreach (var issue in record.Issues) text.AppendLine($"- {issue}");
    }

    text.AppendLine();
    return text.ToString();
  }

  /// <summary>Heading, props, resolved sizes and builder settings.</summary>
  private static void AppendConfig(StringBuilder text, RunSpec spec, CnfLevelGeneration config)
  {
    var graph = config.Graph;
    var objectives = graph.ProcOpts.NodeObjectives;
    var index = spec.Index >= 0 ? $"#{spec.Index} " : "";
    text.AppendLine($"## {index}d{spec.Difficulty}, seed `{spec.TextSeed}`, many themes {(spec.ManyThemes ? "on" : "off")}").AppendLine();
    text.AppendLine(
      $"- Config: Size {graph.Size}, Connectivity {graph.Connectivity}, Complexity {graph.Complexity}, builder {graph.GraphBuilder.Builder}, " +
      $"secrets {graph.ProcOpts.RelSecrets.Amount}, goals {objectives.GoalAmount}, optional goals {objectives.OptGoalAmount}");

    if (graph.GraphBuilder.CnfSimple is { } simple)
      text.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Simple: maxExtra {0}, star {1}, choke {2:F2}",
        simple.MaxExtraDegree, simple.MaxExtraDegreeStar, simple.ChokepointChance));
    if (graph.GraphBuilder.CnfLinear is { } linear)
      text.AppendLine($"- Linear: maxEdges {linear.MaxEdges}, maxExtra {linear.MaxExtraDegree}, condPerRel {linear.MaxConditionPerRel}, " +
        $"condDist {linear.MaxConditionDistance}, skip {linear.MaxSkipDistance}");
  }

  /// <summary>Nodes and passages.</summary>
  private static void AppendGraph(StringBuilder text, LilGraph graph)
  {
    var nodeNames = new Dictionary<Guid, string>();
    for (var i = 0; i < graph.Nodes.Count; i++) nodeNames[graph.Nodes[i].Id] = $"N{i}";

    var solutionNames = new Dictionary<Guid, string>();
    foreach (var poi in graph.Nodes.SelectMany(n => n.Pois).Where(p => p.Type == EnPoiType.Solution))
      solutionNames[poi.Id] = $"k{solutionNames.Count}";

    foreach (var node in graph.Nodes)
    {
      var pois = string.Join(",", node.Pois.Select(p => solutionNames.TryGetValue(p.Id, out var key) ? key : p.Type.ToString()));
      var degree = graph.Edges.Count(e => e.From == node.Id || e.To == node.Id);
      text.AppendLine($"{nodeNames[node.Id]}{(node.Secret ? " (secret)" : "")} deg {degree}/{node.MaxEdges} [{pois}]");
    }

    foreach (var edge in graph.Edges)
    {
      var conditions = string.Join("+", edge.Conditions.Select(c => solutionNames.TryGetValue(c.RequiresPoi, out var key) ? key : "?"));
      var arrow = edge.Direction == EnPassageDirection.OneWay ? "->" : "--";
      text.AppendLine(
        $"{Name(nodeNames, edge.From)} {arrow} {Name(nodeNames, edge.To)}{(edge.Secret ? " secret" : "")}" +
        $"{(edge.Chokepoint ? " choke" : "")}{(conditions.Length > 0 ? " needs " + conditions : "")}");
    }
  }

  /// <summary>Short node name, or "?" for a missing node.</summary>
  private static string Name(Dictionary<Guid, string> names, Guid id) => names.TryGetValue(id, out var name) ? name : "?";
}
