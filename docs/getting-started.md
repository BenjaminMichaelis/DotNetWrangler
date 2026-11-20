# Getting started

[Analyzer catalog](../README.md#analyzer-catalog) | [Configuration](configuration.md) | [Suppressions](suppressions.md)

## Current distribution

Wrangler currently lives in this source repository. The analyzer and code-fix projects both have `IsPackable` set to `false`; this repository does not yet configure a distributable NuGet package or an IDE extension. The instructions below use a source project reference, not a package installation.

## Use the analyzers from source

Clone this repository alongside the project you want to analyze. In the consuming `.csproj`, add a reference to the analyzer project:

```xml
<ItemGroup>
  <ProjectReference Include="..\DotnetBotWranglers\src\Wrangler.Analyzers\Wrangler.Analyzers.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

Adjust `Include` relative to the consuming `.csproj` and your local checkout's folder name. Build that project with a .NET SDK whose compiler supports the analyzer's Roslyn dependency; the current version is recorded in [Wrangler.Analyzers.csproj](https://github.com/BenjaminMichaelis/DotNetWrangler/blob/main/src/Wrangler.Analyzers/Wrangler.Analyzers.csproj). A `netstandard2.0` target alone does not guarantee compatibility with an older compiler host.

This reference loads the diagnostic analyzers, not the IDE code-fix providers. Code fixes are implemented in `Wrangler.CodeFixes`, but this reference does not install them into your editor.

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
