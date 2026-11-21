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

### Compiler compatibility

Wrangler runs in your SDK's or IDE's Roslyn compiler. If you see `CS9057`, the compiler is too old for the installed package. Select a newer [.NET SDK](https://dotnet.microsoft.com/download) for builds or update your IDE for live analysis. You don't need to change your project's target framework.

## Try a diagnostic

Add this method inside a class in the project where you installed Wrangler:

```csharp
static int GetRetryLimit()
{
    int retryLimit = 3;
    return retryLimit;
}
```

Run `dotnet build`. [WRANGLER0001](rules/WRANGLER0001.md) reports that `retryLimit` can be made constant:

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
