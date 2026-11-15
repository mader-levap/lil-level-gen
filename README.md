# Lost in Limbo (LiL) solution

This is the solution for all C# projects related to the level generator library. It will be used by the level editor and **Lost in Limbo**, the future game I want to develop later.

Read the entire `README.md`. There is some configuration that needs to be done before everything works.

## Important

- All repositories should live in the same parent directory, for example:
```
[ROOT_DIR]/
 ├─ lil-level-editor/
 └─ lil-level-gen/
```

## Level generation

### Usage

#### Generate world

Use `LevelGenSession` instance.

Very simple example using default props:
```csharp
private readonly LevelGenSession _levelGenSession = new();
PropMain propMain = new();
CnfLevelGeneration config = LevelGenConfig.Execute(propMain);
_levelGenSession.Start(EnRunKind.All, config);
```

#### Save/load of world

Save and load a generated world (binary; the library only works on streams, files are up to you):
```csharp
LilWorld? world = _levelGenSession.World();
using (var file = File.Create("level.lilw")) LevelGenWorldSerializer.Write(world!, file);
using (var file = File.OpenRead("level.lilw")) world = LevelGenWorldSerializer.Read(file);
```

## Releases

### One-time setup on the LevelGen side
Setup is per machine. Register the feed:

```bash
dotnet nuget add source "[ABSOLUTE_PATH_TO_ROOT_DIR]\lil-level-gen\artifacts\packages" --name lil-local
```

Verify it:
```bash
dotnet nuget list source
```

Should show `lil-local` among other sources.

### One-time setup on the consumer's side

In the consuming project (for example `LevelEditor`) add the reference:

```bash
dotnet add LostInLimbo.LevelEditor.Game\LostInLimbo.LevelEditor.Game.csproj package LostInLimbo.LevelGen --version 0.0.1
```

Adjust the project name and version if they differ at the moment.

### Generating local NuGet package

You run this every time you want a new, updated version of the library. You must bump the version first.
Pack the library into the local feed folder (gitignored):

```bash
dotnet pack LostInLimbo.LevelGen/LostInLimbo.LevelGen.csproj -c Release -o artifacts/packages
```

The consuming project will also need its version bumped (e.g. `<PackageReference Include="LostInLimbo.LevelGen" Version="0.0.2" />` in `.csproj`), unless you set `Version="*"` or the more restrictive `Version="0.0.*"`.

### Other

Notes:

- NuGet caches packages by version. After changing the library, bump `<Version>` in `LostInLimbo.LevelGen.csproj` before packing again, otherwise consumers keep resolving the cached copy.
- You can clear the cache with `dotnet nuget locals global-packages --clear`.

## Testing

### Standard

Run in terminal:
```bash
dotnet test LostInLimbo.slnx
```

### With coverage

#### In Visual Studio Code
You need C# Dev Kit extension from Microsoft.

- Open Test Explorer (flask icon in the Activity Bar) — your xUnit tests appear grouped by class.
- Click Run All Tests with Coverage (the coverage play-button next to the normal Run button), or right-click a subset.
- Results: per-file coverage percentages in the Explorer view, inline green/red coverage overlay in the editor, and a Test Coverage toolbar to jump between uncovered blocks. Toggle the overlay via Test: Show Inline Coverage (Ctrl+; Ctrl+Shift+I).

#### In CLI

Run in terminal:

```bash
dotnet test LostInLimbo.slnx --collect:"XPlat Code Coverage" --settings coverlet.runsettings --nologo
```

It will generate report `coverage.cobertura.xml` in directory `LostInLimbo.LevelGen.Tests\TestResults\<guid>\`.

### Seed sweep (mass generation)

`LostInLimbo.LevelGen.SeedSweep` is a console harness, not part of `dotnet test`. It generates many levels up to the graph phase through the public API and checks each graph against its props and config. It reports hard errors, such as broken structure, impossible POIs, unreachable nodes, validator failures and non-deterministic reruns. It also reports suspicious results, such as obstacles that only lock secret passages, keys lying next to their door in the start node, and connectivity shortfalls.

Run in terminal (Release is much faster):

```bash
dotnet run --project LostInLimbo.LevelGen.SeedSweep -c Release
dotnet run --project LostInLimbo.LevelGen.SeedSweep -c Release -- --runs 2000 --difficulty 5-10
dotnet run --project LostInLimbo.LevelGen.SeedSweep -c Release -- dump 924 967
dotnet run --project LostInLimbo.LevelGen.SeedSweep -c Release -- dump --text-seed 148 --difficulty 5 --many-themes on
```

| Option | Default | Meaning |
|---|---|---|
| `--runs N` | 10000 | Number of runs. |
| `--difficulty A[-B]` | 0-20 | Difficulty or range; runs cycle through it. |
| `--many-themes on\|off\|both` | both | `both` alternates per difficulty cycle. |
| `--seed-prefix P` | `sweep` | Text seed of run `i` is `P-i`. |
| `--start-index N` | 0 | Index of the first run. |
| `--determinism-every N` | 25 | Generate every Nth run twice and compare; 0 disables. |
| `--parallel N` | CPU count | Degree of parallelism. |
| `--timeout-seconds N` | 20 | Per-run timeout. |
| `--warmup N` | 42 | Untimed warm-up runs before measuring; 0 disables. |
| `--out DIR` | `LostInLimbo.LevelGen.SeedSweep/TestResults` | Output root. |
| `--help` | | Usage. |

Run indices are reproducible: `dump <index>` with the same `--difficulty`, `--many-themes` and `--seed-prefix` options regenerates the exact run from a sweep report.

Each invocation writes a new folder `LostInLimbo.LevelGen.SeedSweep/TestResults/<yyyyMMdd-HHmmss>/` (gitignored):
- A sweep writes `report.md` (same Markdown as the console), `runs.csv` (one row per run with all metrics) and `options.md`.
- A dump writes `dump-*.md` (and `options.md`).

Exit codes: 0 = no errors, 1 = at least one run with errors, 2 = invalid arguments.

Timing in the report is generation time measured by the library (`GenerationStatus.Elapsed`): exact, without polling delay. The CSV also has `WallMs`, the harness' wall time including status polling (rounded up to the ~15 ms OS timer tick on Windows). Before measuring, the harness generates `--warmup` untimed levels (own `warmup-i` seeds, both builders, all difficulties) and runs with tiered JIT disabled, so measured runs see fully compiled code. Parallel runs still share the CPU; use `--parallel 1` for the cleanest numbers.
