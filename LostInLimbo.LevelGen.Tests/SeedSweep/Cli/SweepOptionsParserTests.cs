using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.SeedSweep.Cli;
using LostInLimbo.LevelGen.SeedSweep.Data;

namespace LostInLimbo.LevelGen.Tests.SeedSweep.Cli;

/// <summary>Tests <see cref="SweepOptionsParser"/>.</summary>
public sealed class SweepOptionsParserTests
{
  #region Valid input

  public sealed class ValidTests
  {
    [Fact]
    public void Parse_NoArguments_ReturnsDefaults()
    {
      // Act: Parse nothing.
      var result = SweepOptionsParser.Parse([]);

      // Assert: Default sweep.
      Assert.True(result.Success);
      Assert.Equal(EnSweepCommand.Sweep, result.Options.Command);
      Assert.Equal(SweepOptions.DefaultRuns, result.Options.Runs);
      Assert.Equal(0, result.Options.DifficultyMin);
      Assert.Equal(PropMain.MaxDifficulty, result.Options.DifficultyMax);
    }

    [Fact]
    public void Parse_AllOptions_AppliesEveryValue()
    {
      // Arrange: Every sweep option.
      string[] args =
      [
        "--runs", "50", "--difficulty", "3-7", "--many-themes", "on", "--seed-prefix", "x",
        "--start-index", "100", "--determinism-every", "0", "--parallel", "2", "--timeout-seconds", "5", "--out", "dir",
      ];

      // Act: Parse.
      var result = SweepOptionsParser.Parse(args);

      // Assert: All values applied.
      Assert.True(result.Success, string.Join("; ", result.Errors));
      var options = result.Options;
      Assert.Equal(50, options.Runs);
      Assert.Equal(3, options.DifficultyMin);
      Assert.Equal(7, options.DifficultyMax);
      Assert.Equal(EnManyThemesMode.On, options.ManyThemes);
      Assert.Equal("x", options.SeedPrefix);
      Assert.Equal(100, options.StartIndex);
      Assert.Equal(0, options.DeterminismEvery);
      Assert.Equal(2, options.Parallelism);
      Assert.Equal(5, options.TimeoutSeconds);
      Assert.Equal("dir", options.OutputRoot);
    }

    [Fact]
    public void Parse_SingleDifficulty_SetsBothEnds()
    {
      // Act: Parse one difficulty.
      var result = SweepOptionsParser.Parse(["--difficulty", "5"]);

      // Assert: Range of one.
      Assert.True(result.Success);
      Assert.Equal(5, result.Options.DifficultyMin);
      Assert.Equal(5, result.Options.DifficultyMax);
    }

    [Fact]
    public void Parse_DumpIndices_ReturnsDumpCommand()
    {
      // Act: Parse a dump of two runs.
      var result = SweepOptionsParser.Parse(["dump", "924", "967"]);

      // Assert: Dump with both indices.
      Assert.True(result.Success);
      Assert.Equal(EnSweepCommand.Dump, result.Options.Command);
      Assert.Equal([924, 967], result.Options.DumpIndices);
    }

    [Fact]
    public void Parse_DumpTextSeed_ReturnsDumpCommand()
    {
      // Act: Parse a seed dump.
      var result = SweepOptionsParser.Parse(["dump", "--text-seed", "148", "--difficulty", "5", "--many-themes", "on"]);

      // Assert: Seed dump.
      Assert.True(result.Success, string.Join("; ", result.Errors));
      Assert.Equal("148", result.Options.DumpTextSeed);
      Assert.Empty(result.Options.DumpIndices);
    }

    [Fact]
    public void Parse_Help_ReturnsHelpCommand()
    {
      // Act: Parse help after other options.
      var result = SweepOptionsParser.Parse(["--runs", "5", "--help"]);

      // Assert: Help wins.
      Assert.True(result.Success);
      Assert.Equal(EnSweepCommand.Help, result.Options.Command);
    }
  }

  #endregion

  #region ToArguments

  public sealed class ToArgumentsTests
  {
    [Fact]
    public void ToArguments_DefaultSweep_ListsEveryOptionExplicitly()
    {
      // Act: Write out default options.
      var args = SweepOptionsParser.ToArguments(new SweepOptions());

      // Assert: Defaults spelled out, no output root when none is set.
      Assert.Equal(
        "--runs 10000 --difficulty 0-20 --many-themes both --seed-prefix sweep --start-index 0 --determinism-every 25 --parallel 0 --timeout-seconds 20",
        SweepOptionsParser.JoinArguments(args));
    }

    [Fact]
    public void ToArguments_CustomSweep_ParsesBackToSameOptions()
    {
      // Arrange: Non-default sweep options, output root with a space.
      var options = new SweepOptions
      {
        Runs = 50, DifficultyMin = 3, DifficultyMax = 7, ManyThemes = EnManyThemesMode.Off, SeedPrefix = "x",
        StartIndex = 100, DeterminismEvery = 0, Parallelism = 2, TimeoutSeconds = 5, OutputRoot = "my dir",
      };

      // Act: Round trip.
      var parsed = SweepOptionsParser.Parse(SweepOptionsParser.ToArguments(options));

      // Assert: Same options.
      Assert.True(parsed.Success, string.Join("; ", parsed.Errors));
      Assert.Equal(options with { DumpIndices = parsed.Options.DumpIndices }, parsed.Options);
    }

    [Fact]
    public void ToArguments_DumpIndices_ParsesBackToSameDump()
    {
      // Arrange: Dump of two runs with a single difficulty.
      var options = new SweepOptions { Command = EnSweepCommand.Dump, DumpIndices = [924, 967], DifficultyMin = 5, DifficultyMax = 5 };

      // Act: Round trip.
      var args = SweepOptionsParser.ToArguments(options);
      var parsed = SweepOptionsParser.Parse(args);

      // Assert: Dump command first, indices and difficulty preserved.
      Assert.Equal("dump 924 967 --difficulty 5 --many-themes both --seed-prefix sweep --timeout-seconds 20", SweepOptionsParser.JoinArguments(args));
      Assert.True(parsed.Success, string.Join("; ", parsed.Errors));
      Assert.Equal([924, 967], parsed.Options.DumpIndices);
    }

    [Fact]
    public void ToArguments_DumpTextSeed_ParsesBackToSameDump()
    {
      // Arrange: Seed dump.
      var options = new SweepOptions
      {
        Command = EnSweepCommand.Dump, DumpTextSeed = "148", DifficultyMin = 5, DifficultyMax = 5, ManyThemes = EnManyThemesMode.On,
      };

      // Act: Round trip.
      var parsed = SweepOptionsParser.Parse(SweepOptionsParser.ToArguments(options));

      // Assert: Seed, difficulty and themes preserved.
      Assert.True(parsed.Success, string.Join("; ", parsed.Errors));
      Assert.Equal("148", parsed.Options.DumpTextSeed);
      Assert.Equal(5, parsed.Options.DifficultyMin);
      Assert.Equal(EnManyThemesMode.On, parsed.Options.ManyThemes);
    }

    [Fact]
    public void JoinArguments_ArgumentWithSpace_IsQuoted()
    {
      // Act: Join a path with a space.
      var line = SweepOptionsParser.JoinArguments(["--out", "C:\\my dir"]);

      // Assert: Quoted.
      Assert.Equal("--out \"C:\\my dir\"", line);
    }
  }

  #endregion

  #region Invalid input

  public sealed class InvalidTests
  {
    [Theory]
    [InlineData("--runs", "-1")]
    [InlineData("--runs", "0")]
    [InlineData("--runs", "many")]
    [InlineData("--difficulty", "21")]
    [InlineData("--difficulty", "7-3")]
    [InlineData("--difficulty", "1-2-3")]
    [InlineData("--many-themes", "maybe")]
    [InlineData("--start-index", "-5")]
    [InlineData("--determinism-every", "-1")]
    [InlineData("--parallel", "-1")]
    [InlineData("--timeout-seconds", "0")]
    [InlineData("--seed-prefix", " ")]
    [InlineData("--unknown", "1")]
    [InlineData("--text-seed", "148")]
    public void Parse_InvalidOption_ReturnsError(string name, string value)
    {
      // Act: Parse one bad option.
      var result = SweepOptionsParser.Parse([name, value]);

      // Assert: Rejected.
      Assert.False(result.Success);
    }

    [Fact]
    public void Parse_OptionWithoutValue_ReturnsError()
    {
      // Act: Option at the end without value.
      var result = SweepOptionsParser.Parse(["--runs"]);

      // Assert: Rejected with a clear message.
      Assert.Contains("Option --runs needs a value.", result.Errors);
    }

    [Fact]
    public void Parse_BareIndexWithoutDump_ReturnsError()
    {
      // Act: Index without the dump command.
      var result = SweepOptionsParser.Parse(["924"]);

      // Assert: Rejected.
      Assert.Contains("Unexpected argument '924'.", result.Errors);
    }

    [Fact]
    public void Parse_DumpWithoutTarget_ReturnsError()
    {
      // Act: Dump without index or seed.
      var result = SweepOptionsParser.Parse(["dump"]);

      // Assert: Rejected.
      Assert.False(result.Success);
    }

    [Fact]
    public void Parse_DumpTextSeedWithRange_ReturnsErrors()
    {
      // Act: Seed dump with a difficulty range and alternating themes.
      var result = SweepOptionsParser.Parse(["dump", "--text-seed", "148"]);

      // Assert: Both missing specifics reported.
      Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void Parse_SeveralProblems_ReportsAll()
    {
      // Act: Two bad values.
      var result = SweepOptionsParser.Parse(["--runs", "0", "--parallel", "-1"]);

      // Assert: Both reported.
      Assert.Equal(2, result.Errors.Count);
    }
  }

  #endregion
}
