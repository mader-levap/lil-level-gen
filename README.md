# Lost in Limbo (LiL) solution

This is solution for all C# projects related to level generator library. It will be used for level editor and **Lost in Limbo** - codename for future game I want to develop later.

Read entire `README.md`. There is some configuration needed to do before everything works.

## Important

- All solutions should live in same common directory, for example:
```
[ROOT_DIR]/
 ├─ lil-level-editor/
 └─ lil-level-gen/
```

## Releases

### One-time setup on LevelGen side
Setup per machine. Register the feed:
```bash
dotnet nuget add source "[ABSOLUTE_PATH_TO_ROOT_DIR]\lil-level-gen\artifacts\packages" --name lil-local
```

Verify it:
```bash
dotnet nuget list source
```

Should show `lil-local` among other packages.

### One-time setup on consumer's side

In consuming project (for example `LevelEditor`) add the reference:

```bash
dotnet add LostInLimbo.LevelEditor.Game\LostInLimbo.LevelEditor.Game.csproj package LostInLimbo.LevelGen --version 0.0.1
```

Adjust project name and version if at this moment it is different.

### Generating local NuGet package

You run this every time you want new, updated version of library. You must bump version first.
Pack the library into the local feed folder (gitignored):

```bash
dotnet pack LostInLimbo.LevelGen/LostInLimbo.LevelGen.csproj -c Release -o artifacts/packages
```

Consuming project will also need to have version bumped (e.g. `<PackageReference Include="LostInLimbo.LevelGen" Version="0.0.2" />` in `.csproj`), unless you set `Version="*"` or more restrictive `Version="0.0.*"`.

### Other

Notes:

- NuGet caches packages by version. After changing the library, bump `<Version>` in `LostInLimbo.LevelGen.csproj` before packing again, otherwise consumers keep resolving the cached copy.
- You can clear the cache with `dotnet nuget locals global-packages --clear`.
