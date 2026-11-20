# Wrangler agent guidance

## Purpose and project structure

Wrangler provides Roslyn analyzers that put practical guardrails on .NET code. Prefer explicit contracts and meaningful failures over hiding missing data or silencing the compiler. The [README](README.md) is the user-facing source of truth for the analyzer catalog, configuration, framework compatibility, and rule examples.

Path | Responsibility
-----|---------------
`src\Wrangler.Analyzers` | Diagnostic analyzers; targets .NET Standard 2.0.
`src\Wrangler.CodeFixes` | Code fix providers and Workspace-dependent code; targets .NET Standard 2.0.
`tests\Wrangler.Tests` | MSTest tests, using VSTest and Roslyn compilations/workspaces; targets .NET 8.

Add rules to these existing projects, not a separate project per rule. Keep compiler-only analyzer code independent of Workspaces and test-framework runtime dependencies. Read the project files and `Directory.Build.props` for current dependency versions and language settings.

## Adding or changing a rule

- Research existing SDK and third-party analyzers first; start with the README's [related tools](README.md#related-tools). Explain the intended behavior difference where functionality overlaps.
- Keep diagnostic IDs stable and use the `WR####` scheme. Check both `AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md` before allocating an ID; record new rules in the unshipped file.
- Keep the descriptor, release entry, README catalog, rule documentation, and `HelpLinks.ForRule` destination consistent. README rule anchors are diagnostic help-link targets.
- Every rule must remain individually configurable through Roslyn's standard `dotnet_diagnostic.<ID>.severity` settings, including `none`, and support scoped pragma suppression. Do not mark diagnostics non-configurable or add a custom enable/disable mechanism unnecessarily.
- Follow existing formatting and naming. Comments should explain only essential constraints or rationale, not narrate code or add routine Arrange/Act/Assert headings.
- When committing or creating a PR, use Conventional Commits for commit messages and PR titles. Do not add a GitHub Copilot co-author.

## Roslyn implementation practices

- Use the narrowest applicable action registration. Prefer `IOperation` for semantic analysis when it represents the construct; use syntax actions for language-specific constructs or syntax that operations erase.
- The null-forgiving operator has no distinct operation node. Its analyzer intentionally registers `SyntaxKind.SuppressNullableWarningExpression` and consults the semantic model; do not replace this with an operation-only scan that misses `!`.
- Enable concurrent execution and skip generated code with `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)`, following the existing analyzers.
- Resolve framework types once per compilation with `RegisterCompilationStartAction` and `GetTypeByMetadataName`. Handle absent or ambiguous types without guessing. Never retain compilation-specific symbols, semantic models, or syntax trees in static caches.
- Compare symbols with `SymbolEqualityComparer.Default`, or `IncludeNullability` when the distinction matters. Do not identify APIs by display strings, short names, or reflection over the analyzer host's loaded assemblies.
- Use semantic types and generic constraints to establish type facts. Distinguish an expression's `Type` from its `ConvertedType`, and nullable annotations from flow state.
- Keep per-compilation shared caches thread-safe. Avoid repeated whole-compilation/tree scans for each matching node, and honor cancellation tokens in semantic queries and long-running work.
- Expect incomplete or invalid source while a user types: guard unavailable semantic information rather than throwing. Do not hide analyzer defects behind broad exception handlers. Keep extended analyzer rules enabled.
- Report focused, actionable diagnostics. A code fix must preserve evaluation order, side effects, async legality, and domain meaning; do not mechanically replace `!` with `?.`, assertions, or fabricated defaults.

## Null-forgiving rule invariants

These are intentional product decisions, not false positives to silently exempt:

- `!` changes static analysis, not the runtime value. Prefer null checks, nullable contracts/attributes, meaningful initialization, or not-null assertions when appropriate. An empty first name is not a valid universal default.
- Legitimate uses exist, including deliberately passing null to test argument validation or relying on a guarantee the compiler cannot infer. They still receive diagnostics. Document exceptions with a narrow pragma and a reason; do not automatically exempt exception assertions or negative tests. The analyzer does not enforce the wording of that reason.
- WR0003 takes precedence for `null!` and defaults known to be reference types, including reference-constrained generics. Other defaults receive WR0002, even in test classes: `default(int)!`, nullable value-type defaults, and unconstrained/`notnull` generics must not be described as definitely fabricating a null reference.
- Other suppressions receive WR0004 in recognized test classes when the framework-specific advice is applicable, or WR0002 otherwise. Prefix logical negation and null-conditional access are not null-forgiving operators.
- Identify test frameworks through referenced type symbols and test-method attributes, including inherited attributes and methods on base classes. Merely referencing a test framework, naming a class `*Tests`, or defining framework lookalikes in analyzed source is not sufficient.
- Use canonical framework APIs rather than hard-coded package-version branches when possible. Consult the README and official framework documentation before changing assertion recommendations.
- Only recommend TUnit's awaited assertions in a suitable async method, local function, or lambda. Respect the nearest function boundary and await-forbidden contexts. Check enclosing unsafe types, members, and blocks too; stopping at an async method can miss inherited unsafe context. Fall back to WR0002 rather than suggesting an illegal `await`.
- Do not claim all assertions update compiler null-state. Nullable annotations, framework diagnostic suppressors, and capturing a validated return value are different mechanisms; multiple-assert blocks can also continue after a failed assertion.

## Tests and verification

Use the existing test harness and add focused regression cases for changed behavior. Test framework support in analyzed snippets does not require migrating this repository's own MSTest suite.

```powershell
# Focused null-forgiving and configuration tests
dotnet test tests\Wrangler.Tests\Wrangler.Tests.csproj --filter "FullyQualifiedName~NullForgivingOperatorTests|FullyQualifiedName~AnalyzerConfigurationTests"

# Entire solution
dotnet test DotnetBotWranglers.sln
```

- Check IDs, counts, relevant message arguments, and diagnostic spans as appropriate, not just whether any diagnostic exists.
- Assert observable requirements rather than incidental analyzer behavior or code-fix formatting. Validate code fixes with compilation diagnostics and semantic symbols, including preserved types, values, and uses.
- Ensure input compilations are free of unintended compiler errors. Use explicit references or separately compiled framework stubs so the test host's assemblies cannot accidentally determine framework detection.
- For null-forgiving changes, cover relevant reference/value-type and generic-constraint cases, framework detection/fallbacks, and async/unsafe boundaries. Compile recommendations or replacements when their syntax or legality is in question.
- Preserve the scoped pragma regression, including warning restoration. Use `AnalyzerConfigurationTests.cs` to verify actual `.editorconfig` behavior with peer rules enabled and `none`, `warning`, and `error` severities, rather than only checking descriptor constants.
- Keep code-fix tests alongside analyzer tests and verify resulting code. Run the smallest relevant test set, expanding when shared behavior changes or failures require it. Documentation-only changes do not require a build or test run.

## Reference material

Use these for concepts and API behavior, not as instructions to recreate projects or copy old package versions, VSIX setup, or package feeds into this solution.

Reference | Use
----------|----
[Writing a Roslyn analyzer](https://www.meziantou.net/writing-a-roslyn-analyzer.htm) | Analyzer descriptors, syntax/semantic analysis, and code fix fundamentals.
[Writing language-agnostic Roslyn analyzers using IOperation](https://www.meziantou.net/writing-a-language-agnostic-roslyn-analyzer-using-ioperation.htm) | Operation-based analysis and deciding when syntax-specific analysis is necessary.
[Working with types in a Roslyn analyzer](https://www.meziantou.net/working-with-types-in-a-roslyn-analyzer.htm) | Metadata names, type information, symbol equality, generics, and inheritance.
[How to test a Roslyn analyzer](https://www.meziantou.net/how-to-test-a-roslyn-analyzer.htm) | Diagnostic expectations, reference assemblies, and code-fix testing approaches.
[Microsoft: Write your first analyzer and code fix](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix) | Official end-to-end tutorial; the origin of this repository's MakeConst rule.
[Roslyn: Analyzer actions semantics](https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Actions%20Semantics.md) | Action lifetimes, ordering, concurrency, and compilation-scoped state.
[Microsoft: Configure code analysis rules](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-options) | Standard per-rule severity configuration and generated-code settings.
[Microsoft: Null-forgiving operator](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/null-forgiving) | Runtime semantics and legitimate uses, including argument-validation tests.
