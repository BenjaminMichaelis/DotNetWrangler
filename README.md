# MakeConst analyzer template

A Roslyn analyzer and code-fix solution based on the [Microsoft tutorial](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix).
The analyzer reports local variable declarations that can be declared `const`;
the code fix inserts `const` and replaces `var` with the inferred type when needed.

## Projects

- `src/MakeConst.Analyzers` contains the diagnostic analyzer and targets .NET Standard 2.0.
- `src/MakeConst.CodeFixes` contains the code fix provider and targets .NET Standard 2.0.
- `tests/MakeConst.Tests` contains analyzer and code-fix tests.

## Build and test

```shell
dotnet test DotnetBotWranglers.sln
```