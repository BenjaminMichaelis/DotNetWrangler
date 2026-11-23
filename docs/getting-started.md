# Getting started

[Analyzer catalog](../README.md#analyzer-catalog) | [Configuration](configuration.md) | [Suppressions](suppressions.md)

## Install the package

Install [`DotNetWrangler.Analyzers`](https://www.nuget.org/packages/DotNetWrangler.Analyzers) in each C# project you want to analyze. The NuGet package includes both the analyzers and IDE code fixes, so you don't need to clone the repository. Only prerelease versions are currently published.

### .NET CLI

From the directory containing your project's `.csproj`, run:

```shell
dotnet add package DotNetWrangler.Analyzers --prerelease
```

This adds a package reference and restores it. To install a specific version, use `--version` with an exact version from NuGet.org instead of `--prerelease`.

### Visual Studio

1. Select your C# project in **Solution Explorer**, then choose **Project > Manage NuGet Packages**.
2. On the **Browse** tab, select **nuget.org** as the package source, check **Include prerelease**, and search for `DotNetWrangler.Analyzers`.
3. Select the package and choose **Install**.

### Every project in a repository

To analyze all projects with one shared version, add the package in a file at the repository root instead of to each project. Use the latest version listed on [NuGet.org](https://www.nuget.org/packages/DotNetWrangler.Analyzers) in place of the version below.

If the repository uses [Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management?WT.mc_id=8B97120A00B57354), add a [global package reference](https://learn.microsoft.com/nuget/consume-packages/central-package-management?WT.mc_id=8B97120A00B57354#global-package-references) to `Directory.Packages.props`:

```xml
<ItemGroup>
  <GlobalPackageReference Include="DotNetWrangler.Analyzers" Version="0.1.0-beta.1" />
</ItemGroup>
```

Otherwise, add a package reference to `Directory.Build.props`:

```xml
<ItemGroup>
  <PackageReference Include="DotNetWrangler.Analyzers" Version="0.1.0-beta.1" PrivateAssets="all" />
</ItemGroup>
```

`PrivateAssets="all"` lets your projects use Wrangler without passing it to projects that depend on yours. If you publish your library as a NuGet package, Wrangler is not listed as a dependency, so installing your library doesn't also install Wrangler. This controls dependency propagation, not the package's visibility on NuGet.org.

`dotnet add package` adds this setting for you, but a hand-written reference needs it explicitly. A global package reference applies it automatically.

Test projects receive diagnostics too. That's intended: [WRANGLER0003](rules/WRANGLER0003.md) targets test code.

### Compiler compatibility

Wrangler's analyzers and code fixes target .NET Standard 2.0 and reference **Roslyn 5.0.0**. The supported compiler baseline is:

Host | Minimum version
-----|----------------
.NET SDK (command-line builds) | 10.0.100
Visual Studio (live analysis and code fixes) | Visual Studio 2026, version 18.0
Other editors | A Roslyn 5.0-compatible host, including Workspaces support for code fixes

See Microsoft's [Roslyn/Visual Studio version reference](https://learn.microsoft.com/visualstudio/extensibility/roslyn-version-support?view=visualstudio). CI runs analyzer and code-fix tests against Roslyn 5.0 and verifies that the packaged analyzer reports diagnostics in .NET 10 and .NET 11 compiler hosts. Other editor integrations are not tested by this repository.

These are **compiler-host requirements**, not target-framework requirements. For example, a `net8.0` project can use Wrangler when built with a supported SDK. SDKs older than .NET 10 and Visual Studio 2022 are outside the supported host baseline.

If you see `CS9057`, the compiler is too old for the installed package. Select a newer [.NET SDK](https://dotnet.microsoft.com/download) for builds or update your IDE for live analysis. You don't need to change your project's target framework. A consumer's `global.json` controls SDK selection; this repository's .NET 11 SDK pin is only for building Wrangler itself.

This baseline applies to packages built from the current source. Previously published packages may reference newer Roslyn versions; lowering the dependency here does not change those packages.

WRANGLER0001 through WRANGLER0003 are warnings by default. The two pragma policies are disabled until you [enable them explicitly](configuration.md#enable-the-optional-pragma-policies).

## Choose your policy

Use [configuration](configuration.md) to set each rule's severity independently. For a deliberate exception, use a [scoped suppression with a reason](suppressions.md) rather than disabling the rule everywhere.

Null-forgiving diagnostics intentionally include some valid uses of `!`, including null-input argument-validation tests. The [rule selection table](rules/WRANGLER0001.md#which-null-forgiving-rule-applies) explains which diagnostic to expect.

## Use the analyzers from source

To try unreleased changes, reference the source instead of the package.

Clone this repository into a folder named `DotNetWrangler` alongside the project you want to analyze. In the consuming `.csproj`, add a reference to the analyzer project:

```xml
<ItemGroup>
  <ProjectReference Include="..\DotNetWrangler\src\Wrangler.Analyzers\Wrangler.Analyzers.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

Adjust `Include` relative to the consuming `.csproj` and your local checkout's folder name. The same compiler requirement applies.

This reference loads the diagnostic analyzers, not the IDE code-fix providers. Use the package to get code fixes in your editor.

For repository development, see [projects and build instructions](../README.md#projects).
