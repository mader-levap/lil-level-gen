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
