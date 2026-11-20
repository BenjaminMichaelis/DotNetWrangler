# Wrangler analyzers

Roslyn analyzers that put guardrails on .NET code.

## Rules

Rule ID | Category | Description
--------|----------|------------
WR0001 | Usage | Local variables that are never modified should be `const` (code fix available).
WR0002 | Reliability | The null-forgiving operator (`!`) overrides the compiler's nullable analysis. Handle null explicitly, use nullable attributes, or assert in tests; suppress with a justification only when you can prove the value is non-null.
WR0003 | Reliability | `null!` / `default!` fabricate a non-null value. Make the member nullable, `required`, or initialize it with a real value instead of an arbitrary default.

## Projects

- `src/Wrangler.Analyzers` contains the diagnostic analyzers and targets .NET Standard 2.0.
- `src/Wrangler.CodeFixes` contains the code fix providers and targets .NET Standard 2.0.
- `tests/Wrangler.Tests` contains analyzer and code-fix tests.

## Build and test

```shell
dotnet test DotnetBotWranglers.sln
```
