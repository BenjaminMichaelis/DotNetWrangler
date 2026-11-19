# Analyzer with code fix template

A Roslyn analyzer starter solution based on the Visual Studio **Analyzer with Code Fix** template.
The sample analyzer reports type names containing lowercase letters, and its code fix
renames the type and its references to uppercase.

## Projects

- `src/MakeConst.Analyzers` contains the diagnostic analyzer and targets .NET Standard 2.0.
- `src/MakeConst.CodeFixes` contains the code fix provider and targets .NET Standard 2.0.
- `tests/MakeConst.Tests` contains analyzer and code-fix tests.

## Build and test

```shell
dotnet test DotnetBotWranglers.sln
```

The [Microsoft tutorial](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix)
uses this template to build a MakeConst analyzer.