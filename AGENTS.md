# Lost in Limbo level generation

## Project overview

`LostInLimbo.LevelGen` is an engine-neutral C# class library for procedural generation of levels and chunks for the game *Lost in Limbo*.

Its primary consumers are:

- A standalone C# level editor (uses Stride engine).
- The future Godot or Unity game.
- Potentially other engines through a native or data-export bridge.

The library generates **data only**. It does not create game-engine objects, meshes, scenes, or UI.

## Non-negotiable architecture rules

- Keep this project **engine-neutral**.
- Do **not** add references to Godot, Unity, Stride, WPF, WinForms, Avalonia, or any game-engine SDK.
- Do not use `Godot.*`, `UnityEngine.*`, `Stride.*`, `System.Windows.*`, or similar engine/UI namespaces.
- Do not create `GameObject`, `Node3D`, `Mesh`, `Transform`, `Entity`, or other engine-specific objects.
- Do not perform file I/O, console output, logging, HTTP requests, or UI work inside generation algorithms.
- Generation methods must be **deterministic** for the same inputs and random seed.
- Accept randomness through an injectable seed or random-number-generator abstraction; do not use unseeded `Random` inside generation logic.
- Return **plain C# data types** such as records, classes, enums, arrays, and collections.
- Keep public APIs small, explicit, and stable.
- Prefer composition and pure functions over global state and hidden side effects.
- Do not introduce NuGet dependencies unless they are essential, portable, and compatible with `netstandard2.1`. Even then, ask for user permission first.

## Target framework

This library targets `netstandard2.1` so it can be consumed by modern .NET applications and Unity.

Do not change the target framework without explicit user approval.

```xml
<TargetFramework>netstandard2.1</TargetFramework>
```

You may use modern C# language syntax where the compiler supports it, but do not use APIs unavailable in .NET Standard 2.1.

## Repository layout

```text
lil-level-gen/
 ├─ LostInLimbo.slnx              - Solution for level generator and its tests.
 ├─ AGENTS.md                     - This file.
 ├─ README.md                     - Project notes.
 ├─ TODO.md                       - Current tasks.
 ├─ GENERATOR.md                  - Description of level generator.
 ├─ LostInLimbo.LevelGen/
 │   ├─ Api/                      - Contains public API.
 │   ├─ Data/                     - Contains public data contracts.
 │   └─ Internal/                 - Contains internal code.
 └─ LostInLimbo.LevelGen.Tests/
```

- `LostInLimbo.LevelGen` contains all generator code.
- `LostInLimbo.LevelGen.Tests` contains unit and integration tests.
- Editor, Godot, Unity, and other engine-specific code must not be added to this library project.

## Basic commands

Build and test entire solution:

```bash
dotnet build LostInLimbo.slnx
dotnet test LostInLimbo.slnx
```

Build only the library:

```bash
dotnet build LostInLimbo.LevelGen/LostInLimbo.LevelGen.csproj
```

Run only tests:

```bash
dotnet test LostInLimbo.LevelGen.Tests/LostInLimbo.LevelGen.Tests.csproj
```

Pack library (only when explicitly asked):

```bash
dotnet pack LostInLimbo.LevelGen/LostInLimbo.LevelGen.csproj -c Release -o artifacts/packages
```

Before considering work complete:

- Run `dotnet build LostInLimbo.slnx`.
- Run `dotnet test LostInLimbo.slnx`.
- Fix all warnings introduced by the change when practical.
- Add or update tests for new behavior, bug fixes, and edge cases.

## Code style

### General coding rules

- Use C# file-scoped namespaces.
- Write null-aware code and do not silence nullable warnings with `null!` without a documented reason.
- Use `sealed` for classes that are not intended for inheritance.
- Prefer `record` for immutable data-transfer types.
- Prefer `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, or arrays in public APIs when callers should not mutate the result.
- Avoid public mutable collections where practical.
- Use explicit, descriptive names: `GenerateChunk`, `ChunkData`, `RoomTemplate`, `CorridorSegment`, `SpawnPoint`.
- Avoid abbreviations except widely understood ones such as `Id`, `X`, `Y`, and `Z`.
- Keep methods small and focused; extract helper methods when a generation pass becomes complex.
- Avoid magic numbers; use named constants, options objects, or configuration records.
- Use `CancellationToken` for potentially expensive or cancellable operations.
- Do not use `async void`.
- Do not use `DateTime.Now`, environment-specific paths, or machine-specific behavior in generation logic.

### Testing requirements

Use xUnit.

For every meaningful generator feature, add tests that verify:

- Deterministic output for the same seed and inputs.
- Correct area dimensions and cell bounds.
- Required areas are connected or reachable, where applicable.
- No overlapping or invalid geometry.
- Spawn points and important objects are placed within valid cells.
- Boundary areas and edge cases do not throw or produce invalid data.
- Cancellation is respected for expensive operations.

Prefer focused tests. Example names:

- `GenerateChunk_WithSameSeed_ProducesIdenticalData`
- `GenerateChunk_WithInvalidDimensions_ThrowsArgumentException`
- `GenerateChunk_WhenCancelled_ThrowsOperationCanceledException`

Other rules:
- If a bug is known, create a test first, run it to make sure the test catches the bug, then fix the bug.
- Comment tests. Use the Arrange/Act/Assert convention, for example `// Assert: Value is correct.`

### Error handling

- Validate public inputs and throw specific exceptions such as `ArgumentException`, `ArgumentOutOfRangeException`, or `InvalidOperationException`.
- Include useful exception messages and parameter names.
- Do not swallow exceptions.
- Do not return `null` collections; return empty collections where appropriate.
- Do not use exceptions for normal control flow during generation.

### Documentation

- Code should be well-commented, but comments cannot be too long either.
- Add XML documentation comments to all types and members, except for overridden methods.
- Explain non-obvious generation rules, constraints, and coordinate-system assumptions.
- Document units, coordinate origins, and whether coordinates are inclusive or exclusive.

## Other

- **Never commit or push.** The user reviews and commits manually.
- Do not pack NuGet package unless explicitly asked.

## Generation design

Right now do not worry about backward compatibility.

### General generation rules
The generator should be structured as separate, testable passes, for example:

1. Initialize chunk context and random generator.
2. Generate base layout: rooms, corridors, open areas, or maze cells.
3. Validate connectivity and constraints.
4. Place major features: doors, portals, exits, objectives.
5. Place props, decorations, hazards, and spawn points.
6. Return the completed `ChunkData`.

Keep each pass independently testable where possible.

A chunk generator should look conceptually like this:

```csharp
public ChunkData GenerateChunk(
    ChunkKey key,
    LevelGenerationContext context,
    CancellationToken cancellationToken = default)
```

It must not depend on player position, camera, engine frame time, or scene state.

### World

- All dimensions (X, Y and Z) are used.
- Important: **Z is up/down** (Blender/UE convention). Consumers that use Y as up/down need to take it in account.
- Use `Point3` for grid/cell coordinates. Use `System.Numerics.Vector3` for world-space positons or dimensions in meters.

### Randomness and determinism

- Derive per-chunk random generators deterministically from the world seed and chunk coordinates.
- Never rely on iteration order of `Dictionary<TKey, TValue>` or `HashSet<T>` when it can affect generated output.
- Sort collections explicitly before using their order in generation.
- Document whether a method is deterministic.
- Tests must be able to reproduce a generated level from a known seed.

## When uncertain

If a requested change would require:

- An engine dependency,
- A new NuGet package,
- A target-framework change,
- A breaking public-API change,
- File/system access inside the generator,
- Or nondeterministic behavior,

stop and ask before implementing it.