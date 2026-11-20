# Getting started

[Analyzer catalog](../README.md#analyzer-catalog) | [Configuration](configuration.md) | [Suppressions](suppressions.md)

## Install the package

Wrangler is distributed as the [`DotNetWrangler.Analyzers`](https://www.nuget.org/packages/DotNetWrangler.Analyzers) NuGet package, which includes both the diagnostic analyzers and the IDE code fixes:

```shell
dotnet add package DotNetWrangler.Analyzers
```

For beta builds, add `--prerelease` or specify an exact prerelease version with `--version`.

The package is a development dependency, so it does not flow to projects that reference yours. Build with a .NET SDK whose compiler supports the analyzer's Roslyn dependency; the current version is recorded in [Wrangler.Analyzers.csproj](https://github.com/BenjaminMichaelis/DotNetWrangler/blob/main/src/Wrangler.Analyzers/Wrangler.Analyzers.csproj). A `netstandard2.0` target alone does not guarantee compatibility with an older compiler host.

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

## Try a diagnostic

In the consuming project, add a method with an unmodified constant-valued local:

```csharp
static int GetRetryLimit()
{
    int retryLimit = 3;
    return retryLimit;
}
```

Build the project. [WRANGLER0001](rules/WRANGLER0001.md) reports that `retryLimit` can be made constant:

```csharp
static int GetRetryLimit()
{
    const int retryLimit = 3;
    return retryLimit;
}
```

WRANGLER0001 through WRANGLER0004 are warnings by default. The two pragma policies are disabled until you [enable them explicitly](configuration.md#enable-the-optional-pragma-policies).

## Choose your policy

Use [configuration](configuration.md) to set each rule's severity independently. For a deliberate exception, use a [scoped suppression with a reason](suppressions.md) rather than disabling the rule everywhere.

Null-forgiving diagnostics intentionally include some valid uses of `!`, including null-input argument-validation tests. The [rule selection table](rules/WRANGLER0002.md#which-null-forgiving-rule-applies) explains which diagnostic to expect.

For repository development, see [projects and build instructions](../README.md#projects).
