# .NET Wrangler

Roslyn analyzers that put guardrails on .NET code.

Wrangler encourages explicit null contracts, meaningful test failures, and reviewable warning suppressions. It also offers a code fix for constant-valued locals.

Read the [documentation site](https://benjaminmichaelis.github.io/DotNetWrangler/) or browse the guides below.

## Start here

- [Getting started](docs/getting-started.md): install the `DotNetWrangler.Analyzers` NuGet package or use the analyzers from source.
- [Configuration](docs/configuration.md): choose severities and enable the optional pragma policies.
- [Suppressing a diagnostic](docs/suppressions.md): document intentional exceptions without disabling a rule project-wide.
- [Analyzer catalog](#analyzer-catalog): find a rule's explanation, examples, and code-fix availability.

## Analyzer catalog

ID | Description | Default severity | Code fix
---|-------------|------------------|---------
[WRANGLER0001](docs/rules/WRANGLER0001.md) | Make unmodified constant-valued locals `const`. | Warning | Yes
[WRANGLER0002](docs/rules/WRANGLER0002.md) | Handle null explicitly instead of overriding nullable analysis with `!`. | Warning | No
[WRANGLER0003](docs/rules/WRANGLER0003.md) | Avoid fabricating non-null values with `null!` or reference-type `default!`. | Warning | No
[WRANGLER0004](docs/rules/WRANGLER0004.md) | Prefer a framework-specific not-null assertion in tests. | Warning | No
[WRANGLER0005](docs/rules/WRANGLER0005.md) | Document explicit pragma suppression IDs and rule titles. | Disabled (opt-in warning) | When titles are available
[WRANGLER0006](docs/rules/WRANGLER0006.md) | Require an explicit justification for pragma suppressions. | Disabled (opt-in warning) | No

The three null-forgiving rules are alternatives, not three diagnostics on every `!`. [WRANGLER0002](docs/rules/WRANGLER0002.md#which-null-forgiving-rule-applies) explains which rule takes precedence. Valid uses can still receive a diagnostic; use a [scoped suppression with a reason](docs/suppressions.md) for intentional exceptions.

## Rule details

### WRANGLER0001

[Variable can be made constant](docs/rules/WRANGLER0001.md): explanation, before/after example, and code fix.

### WRANGLER0002

[Null-forgiving operator hides a possible null value](docs/rules/WRANGLER0002.md): explicit null handling and precedence among the null-forgiving rules.

### WRANGLER0003

[Null-forgiving operator fabricates a non-null value](docs/rules/WRANGLER0003.md): meaningful initialization, nullable contracts, and intentional null-input tests.

### WRANGLER0004

[Use a not-null assertion instead of the null-forgiving operator in tests](docs/rules/WRANGLER0004.md): supported frameworks, assertion recommendations, and async/nullable limitations.

### WRANGLER0005

[Document suppressed diagnostic IDs and titles](docs/rules/WRANGLER0005.md): single/multiple-ID formats, external title catalogs, validation modes, and code-fix limits.

### WRANGLER0006

[Justify warning suppressions](docs/rules/WRANGLER0006.md): accepted justification locations, field syntax, and policy scope.

## Configuration

Every rule supports standard `.editorconfig` severity settings, including `none`, and scoped pragma suppression. WRANGLER0005 and WRANGLER0006 are independent opt-in policies.

See [configuration examples](docs/configuration.md) and [suppression guidance](docs/suppressions.md).

## Related tools

- [Nullable.Extended.Analyzer](https://github.com/tom-englert/Nullable.Extended) reports `!` without a `// !` justification comment.
- [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer) has an opt-in rule for `null!` / `default!`.
- The .NET SDK's IDE analyzers report `!` that the compiler doesn't need and offer to remove it.
- StyleCop [SA1404](https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/documentation/SA1404.md) requires justifications on `SuppressMessage` attributes, not pragma title/reason comments. The related pragma proposal [DotNetAnalyzers/StyleCopAnalyzers#3403](https://github.com/DotNetAnalyzers/StyleCopAnalyzers/issues/3403) motivated similar discussion.
- The SDK's [IDE0079](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/ide0079) finds unnecessary suppressions; WRANGLER0005/WRANGLER0006 document the suppressions that remain.

## Projects

- `src/Wrangler.Analyzers` contains the diagnostic analyzers and targets .NET Standard 2.0.
- `src/Wrangler.CodeFixes` contains the code fix providers and targets .NET Standard 2.0.
- `src/Wrangler.Package` packs both assemblies into the `DotNetWrangler.Analyzers` NuGet package.
- `tests/Wrangler.Tests` contains analyzer and code-fix tests.

## Build and test

```shell
dotnet test DotNetWrangler.slnx
```

## Release

The [publish workflow](https://github.com/BenjaminMichaelis/DotNetWrangler/blob/main/.github/workflows/publish.yml) tests the solution, packs it, and publishes to nuget.org through [trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).

- Pushing a `vMAJOR.MINOR.PATCH[-prerelease]` tag publishes exactly that version. Only tags without a suffix publish stable versions.
- The nightly run at 10:05 UTC publishes from `main` only if that commit has not already published. It takes the highest tagged version, increments its patch if it is stable, and appends `-beta.<run number>`. After the first tagged beta, use a different suffix (for example, `-rc.1`) for manual prereleases to avoid colliding with nightly versions.

Verify a package locally before releasing:

```shell
dotnet pack src/Wrangler.Package/Wrangler.Package.csproj -c Release -o artifacts -p:Version=0.0.0-local
```

See [maintaining the documentation](docs/publishing.md) to build, preview, customize, and publish the DocFX site.

## License

.NET Wrangler is licensed under the [Apache-2.0 license](https://github.com/BenjaminMichaelis/DotNetWrangler/blob/main/LICENSE).
