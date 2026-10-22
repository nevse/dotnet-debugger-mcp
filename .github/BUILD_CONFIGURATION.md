# Build Configuration Reference

This document describes the build configuration for the DotnetDebugger.Mcp repository.

## Centralized Package Management

This repository uses [Central Package Management (CPM)](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management) to manage NuGet package versions centrally.

### How It Works

- **Directory.Packages.props** - Defines all package versions in one place
- **Project files** - Reference packages without specifying versions

### Directory.Packages.props

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <!-- All package versions defined here -->
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.1" />
    <PackageVersion Include="ModelContextProtocol" Version="0.5.0-preview.1" />
    <PackageVersion Include="Microsoft.VisualStudio.Shared.VSCodeDebugProtocol" Version="18.0.10427.1" />
    <PackageVersion Include="Markdig" Version="0.44.0" />
    <PackageVersion Include="MSTest" Version="4.0.2" />
  </ItemGroup>
</Project>
```

The debugger itself is not among them. It is [clrdbg](https://github.com/JaneySprings/clrdbg), which
publishes no package: the build takes the debug adapter from one of its GitHub releases, as described
under [The Debug Adapter](#the-debug-adapter). The
`Microsoft.VisualStudio.Shared.VSCodeDebugProtocol` package provides the Debug Adapter Protocol types
the server uses to talk to it.

### Project File Pattern

In project files (.csproj), packages are referenced WITHOUT version attributes:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Hosting" />
  <PackageReference Include="ModelContextProtocol" />
</ItemGroup>
```

The version is automatically resolved from Directory.Packages.props.

### Adding New Packages

1. Add version to `Directory.Packages.props`:
   ```xml
   <PackageVersion Include="NewPackage" Version="1.0.0" />
   ```

2. Reference in project file without version:
   ```xml
   <PackageReference Include="NewPackage" />
   ```

### Updating Package Versions

Update the version in **one place** - Directory.Packages.props:

```xml
<PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.2" />
```

All projects will use the updated version after restore.

## Common Build Properties

**Directory.Build.props** defines properties shared across all projects:

### Compilation Settings
- **TargetFramework**: net10.0
- **LangVersion**: latest
- **ImplicitUsings**: enabled
- **Nullable**: enabled

### Code Quality
- **EnforceCodeStyleInBuild**: true
- Follows .editorconfig rules

### Metadata
- **Authors**: .NET Debugger MCP Contributors
- **License**: MIT
- **Copyright**: Copyright © 2026

### Per-Project Properties

Projects can override or add properties as needed:

**DotnetDebugger.Mcp.csproj** (Main project):
```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
</PropertyGroup>
```

**DotnetDebugger.Mcp.Tests.csproj** (Test project):
```xml
<PropertyGroup>
  <IsPackable>false</IsPackable>
  <IsTestProject>true</IsTestProject>
</PropertyGroup>
```

## The Debug Adapter

The debugger is [clrdbg](https://github.com/JaneySprings/clrdbg). It publishes no package, but each
release carries `clrdbg_universal.zip`, the debug adapter built framework-dependent, and the build takes
it from there.

The server starts the adapter as a child process, `dotnet <output directory>/clrdbg/clrdbg.dll`, and
speaks the Debug Adapter Protocol over its standard input and output, so the adapter is a binary the
build has to put beside it rather than a reference it can link. Running the debugger in a process of
its own means a native crash inside `libmscordbi` takes down the adapter rather than the server, and on
macOS the `dotnet` muxer carries the entitlement a debugger needs.

### ClrdbgVersion

**Directory.Build.props** pins the release with `ClrdbgVersion`, and each of the two zips the build
takes from it - the adapter and the [remote CoreCLR libraries](#the-remote-coreclr-libraries) - by a
URL and a SHA-256: `ClrdbgAdapterUrl` and `ClrdbgAdapterSha256`, `RemoteCoreclrLibrariesUrl` and
`RemoteCoreclrLibrariesSha256`. Both come from the same version, since the remote libraries speak a
protocol the adapter has to match. Update the version and both hashes together.

clrdbg's release workflow attests the build provenance of every zip. CI checks both against it with
`scripts/verify-clrdbg-release.sh`, which runs `gh attestation verify` for the release workflow and
the `ClrdbgVersion` tag, on top of the hash: in the Code Quality job, so a pull request that moves the
version finds out, and in the Package job before it packs. Run the script locally after a bump.

### FetchClrdbgRelease

**Directory.Build.targets** defines the `FetchClrdbgRelease` target. It downloads each zip a project
needs into `obj/clrdbg/<version>/` at the repository root, checks its hash and unpacks it beside the
zip. It does that once per version and hash, so a pin moved to a recreated release fetches again. The
zips are kept, and the provenance check reads them there. The first build therefore needs network
access to github.com.

`CopyClrdbgAdapter` copies the adapter to `$(OutDir)clrdbg/` before `Build` in every project that sets
`NeedsClrdbgAdapter`, which is the server and the test project. Being framework-dependent is what lets
one adapter serve every platform: its `runtimes/` folder carries the native DbgShim assets for each
supported runtime identifier.

### ClrdbgSourcePath

To work on the debugger itself, such as a fork carrying a fix, set `ClrdbgSourcePath` to a clrdbg
checkout. The `BuildClrdbgAdapter` target then builds the adapter from it, framework-dependent, in place
of the release's. The remote CoreCLR libraries still come from `ClrdbgVersion`, so the checkout has to
speak their protocol. An environment variable of the same name applies to every command in the shell:

```bash
export ClrdbgSourcePath=/path/to/clrdbg
dotnet build
```

It can also be passed to a single command:

```bash
dotnet build -p:ClrdbgSourcePath=/path/to/clrdbg
```

Only released clrdbg reaches the package CI builds: a fix on clrdbg's `main` waits for a release, or is
built locally this way.

### PackClrdbgAdapter

**DotnetDebugger.Mcp.csproj** defines the `PackClrdbgAdapter` target, which puts the adapter in the package
under `tools/net10.0/any/clrdbg/`, beside the server that starts it. The native DbgShim assets under
`runtimes/` cover every supported platform, so a single package works everywhere.

## The Remote CoreCLR Libraries

Debugging a MAUI app takes two native libraries besides the adapter: a target library built into the
app, and a host library the adapter loads to talk to it. They are part of clrdbg too, and come from the
`RemoteCoreClrLibraries.zip` asset of the same release as the adapter, pinned as described under
[ClrdbgVersion](#clrdbgversion). Unlike the adapter they could not be built here even from a checkout:
the host half is NativeAOT for six runtime identifiers and the target half needs Xcode and the Android
NDK.

clrdbg can also drive Visual Studio's remote debugger, and switches to it when either directory holds
a file with `vsdbg` in its name (`MobileDebugAgent` in clrdbg). This server refuses such a directory
before the launch reaches clrdbg (`RemoteCoreclrLibraries.Resolve`), so that path stays unreachable.
The refusal copies clrdbg's rule, so **when `ClrdbgVersion` moves, compare the two** and follow any
change to how clrdbg recognises Microsoft's libraries.

`FetchClrdbgRelease` fetches this zip too, and `CopyRemoteCoreclrLibraries` copies it to
`$(OutDir)remote-coreclr/` before `Build` in every project that sets `NeedsRemoteCoreclrLibraries` -
the server and the test project. **DotnetDebugger.Mcp.csproj** defines `PackRemoteCoreclrLibraries`,
which puts them in the package under `tools/net10.0/any/remote-coreclr/`.

## Build Commands

```bash
# Restore packages (reads Directory.Packages.props)
dotnet restore

# Build all projects
dotnet build

# Run tests
dotnet test

# Build specific project
dotnet build src/DotnetDebugger.Mcp/DotnetDebugger.Mcp.csproj

# Using helper scripts
./scripts/build-and-test.sh
```

## Solution Structure

```
dotnet-debugger-mcp/
├── Directory.Build.props         # Common build properties
├── Directory.Packages.props       # Centralized package versions
├── Directory.Build.targets        # Fetches the debug adapter from a clrdbg release
├── DotnetDebugger.Mcp.slnx       # Solution file (XML format)
├── .editorconfig                  # Code style rules
├── .gitignore                     # Git ignore patterns
├── src/
│   └── DotnetDebugger.Mcp/       # Main project
└── tests/
    └── DotnetDebugger.Mcp.Tests/ # Test project
```

## Benefits of This Approach

1. **Single Source of Truth** - All versions in one file
2. **Consistency** - All projects use same package versions
3. **Easy Updates** - Update one place, affects all projects
4. **Reduced Duplication** - Common properties defined once
5. **Better IDE Support** - Modern tooling understands CPM

## References

- [Central Package Management Documentation](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management)
- [Customize Your Build (Directory.Build.props)](https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-your-build)
- [MSBuild Properties Reference](https://learn.microsoft.com/en-us/visualstudio/msbuild/common-msbuild-project-properties)
