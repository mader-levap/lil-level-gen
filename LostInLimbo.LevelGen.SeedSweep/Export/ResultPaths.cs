using System.Globalization;
using System.Reflection;

namespace LostInLimbo.LevelGen.SeedSweep.Export;

/// <summary>Locations of sweep output.</summary>
internal static class ResultPaths
{
  /// <summary>Name of the output root folder inside the project folder (gitignored).</summary>
  public const string ResultsFolderName = "TestResults";
  /// <summary>Folder name format of one invocation, local time.</summary>
  public const string FolderTimestampFormat = "yyyyMMdd-HHmmss";

  /// <summary>
  /// Default output root: <c>&lt;project folder&gt;/TestResults</c>. The project folder is baked
  /// into the assembly at build time, so the result does not depend on the working directory.
  /// Falls back to <c>TestResults</c> under the working directory if the metadata is missing.
  /// </summary>
  public static string DefaultRoot
  {
    get
    {
      var projectDirectory = typeof(ResultPaths).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "ProjectDirectory")?.Value;
      return Path.Combine(string.IsNullOrEmpty(projectDirectory) ? Directory.GetCurrentDirectory() : projectDirectory, ResultsFolderName);
    }
  }

  /// <summary>
  /// Creates a new timestamped folder under <paramref name="root"/>. Adds <c>-2</c>, <c>-3</c>…
  /// when a folder with the same timestamp exists.
  /// </summary>
  /// <param name="root">Output root; created when missing.</param>
  /// <param name="now">Timestamp for the folder name.</param>
  /// <returns>Full path of the created folder.</returns>
  public static string CreateRunFolder(string root, DateTime now)
  {
    if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Output root must not be empty.", nameof(root));

    var name = now.ToString(FolderTimestampFormat, CultureInfo.InvariantCulture);
    var path = Path.GetFullPath(Path.Combine(root, name));
    for (var suffix = 2; Directory.Exists(path); suffix++)
      path = Path.GetFullPath(Path.Combine(root, $"{name}-{suffix}"));

    Directory.CreateDirectory(path);
    return path;
  }
}
