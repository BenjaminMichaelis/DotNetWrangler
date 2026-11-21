---
name: add-analyzer
description: Step-by-step workflow for adding or changing a Wrangler Roslyn analyzer rule (WRANGLER####), including research, ID allocation, implementation, code fixes, tests, configuration, and documentation. Use when asked to create a new analyzer, add a diagnostic rule, or substantially change an existing rule's behavior. Do not use for documentation-only edits or general test runs.
---

# Add a Wrangler analyzer rule

Follow this workflow when adding a rule or materially changing one. [`AGENTS.md`](../../../AGENTS.md) holds the standing invariants (Roslyn practices, null-forgiving decisions, test expectations); this skill is the ordered checklist. If they disagree, `AGENTS.md` wins. Update this skill when the workflow changes.

## 1. Define the guardrail

- State the problem in one sentence and what the developer should do instead. Wrangler prefers explicit contracts and meaningful failures over hiding missing data or silencing the compiler.
- List positive cases (must report), negative cases (must not report), and legitimate uses that still report and should be suppressed with a scoped pragma and reason. Decide these before writing code; they become the tests.
- Ask the user about ambiguous product decisions (severity, category, exemptions, whether a code fix is safe) instead of guessing.

## 2. Research overlap

- Check the README [related tools](../../../README.md#related-tools), the .NET SDK `CA`/`IDE` rules, and popular third-party analyzers (for example Meziantou.Analyzer, Roslynator, Nullable.Extended.Analyzer, StyleCop, test-framework analyzers).
- If a rule already exists, explain the intended behavior difference or recommend configuring the existing rule instead. Record meaningful differences in the README.
- For framework-specific advice, use official framework documentation and canonical APIs. Note which framework versions the advice is documented for rather than branching on package versions.

## 3. Allocate the ID and descriptor

- Read both `src\Wrangler.Analyzers\AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md`; take the next unused `WRANGLER####`. Never reuse or renumber an ID.
- Create the descriptor with a stable ID, a category matching existing rules (`Usage`, `Reliability`, ...), a default severity, `isEnabledByDefault`, and `helpLinkUri: HelpLinks.ForRule(id)`.
- Message format arguments should name the specific symbol or expression so the diagnostic is actionable.
- Do not add `WellKnownDiagnosticTags.NotConfigurable` or a custom on/off switch; standard `dotnet_diagnostic.<ID>.severity` and pragmas must work.
- Add the rule to `AnalyzerReleases.Unshipped.md` with matching category, severity, and notes.
- When adding an analyzer class, include its descriptors in `PragmaTitleCatalog` so WRANGLER0004 verifies its titles. The descriptor-discovery regression must cover every analyzer in the assembly.

## 4. Implement the analyzer

Put it in `src\Wrangler.Analyzers` (`netstandard2.0`, no Workspaces or test-framework dependencies).

- `context.EnableConcurrentExecution()` and `context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)`.
- Choose the narrowest registration: `RegisterOperationAction` when an `IOperation` represents the construct; `RegisterSyntaxNodeAction` for C#-specific syntax or syntax that operations erase (for example `!`).
- Resolve framework/library types once in `RegisterCompilationStartAction` with `GetTypeByMetadataName`; bail out cleanly when a type is missing or ambiguous.
- Compare symbols with `SymbolEqualityComparer`; never match by display string, short name, or host reflection. Use `Type` vs `ConvertedType` and annotation vs flow state deliberately.
- Keep shared per-compilation state thread-safe; never cache compilation symbols statically. Pass cancellation tokens to semantic-model calls.
- Tolerate incomplete code (null symbols, error types) without broad `try/catch`.
- Report on the smallest meaningful span.

Keep an eye out for:

- Constructs reachable through multiple syntax forms (parentheses, casts, conversions, `default` vs `default(T)`, target-typed `new`, patterns).
- Generics and constraints (`class`, `struct`, `notnull`, unconstrained, base-class constraints) and nullable value types.
- Function boundaries: local functions, lambdas, constructors, initializers, accessors, and `async`/`unsafe`/`lock`/`catch` contexts when advice depends on them.
- Inheritance: base classes, derived attributes, overrides, and partial types.
- Lookalike types defined in source versus genuinely referenced framework types.

## 5. Add a code fix only when it is safe

Put it in `src\Wrangler.CodeFixes`. A fix must preserve evaluation order, side effects, async legality, and domain meaning. If no fix is universally correct, ship the diagnostic without one. Provide an equivalence key and `WellKnownFixAllProviders.BatchFixer` when batch fixing is valid.

## 6. Write requirement-focused tests

Add a `tests\Wrangler.Tests\<Rule>Tests.cs` file using the existing harness patterns (MSTest, `DataRow`, Roslyn compilations).

- Cover every positive, negative, and legitimate-but-reported case from step 1, plus the "keep an eye out" edge cases that apply.
- Assert diagnostic IDs, counts, message arguments, and spans, not merely that something was reported.
- Fail on unintended compiler errors in the input source. Use explicit or separately compiled stub references for frameworks so the test host's assemblies cannot determine detection.
- Include a positive control next to each negative case so a broken fixture cannot pass silently.
- Code-fix tests verify the fixed result compiles, is semantically correct, leaves unrelated code unchanged, and removes the diagnostic. Avoid substring checks.
- Add `none`/`warning`/`error` rows for the new ID in `AnalyzerConfigurationTests.cs`, with peer rules still active, and extend its shared source to produce the new diagnostic.
- Add a scoped `#pragma warning disable/restore` regression showing suppression and restoration.
- Sanity-check important tests by temporarily breaking the analyzer and confirming they fail; revert the mutation.

## 7. Document

- README: add a catalog row and a `### WRANGLER####` section (the existing help-link convention) linking to the canonical rule page.
- Add `docs\rules\WRANGLER####.md` with the diagnostic title, default severity, code-fix availability, examples of flagged and preferred code, legitimate-use guidance, and relevant configuration. Keep detailed content in one place; link to shared guides under `docs\` rather than duplicating them.
- Add the rule to the `docs\toc.yml` sidebar and validate the site with `dotnet docfx build docfx.json --warningsAsErrors`. See [maintaining the documentation](../../../docs/publishing.md) for setup.
- Update the README's related-tool differences when needed.
- Update `AGENTS.md` when the rule introduces lasting invariants or intentional product decisions future contributors must preserve.
- Keep the descriptor, release entry, README catalog/anchor, canonical rule page, and help link consistent.

## 8. Verify and finish

```powershell
dotnet test tests\Wrangler.Tests\Wrangler.Tests.csproj --filter "FullyQualifiedName~<Rule>Tests|FullyQualifiedName~AnalyzerConfigurationTests"
dotnet test DotNetWrangler.slnx
```

- Builds must have no new warnings, including Roslyn extended analyzer rules (`RS####`).
- For non-trivial rules, ask an independent reviewer to critique the implementation and tests for false positives, false negatives, and tests that pin incidental behavior.
- Commit only when asked, using Conventional Commits (`feat:` for a new rule) and no Copilot co-author.

## References

- [Writing a Roslyn analyzer](https://www.meziantou.net/writing-a-roslyn-analyzer.htm)
- [Language-agnostic analyzers with IOperation](https://www.meziantou.net/writing-a-language-agnostic-roslyn-analyzer-using-ioperation.htm)
- [Working with types in a Roslyn analyzer](https://www.meziantou.net/working-with-types-in-a-roslyn-analyzer.htm)
- [How to test a Roslyn analyzer](https://www.meziantou.net/how-to-test-a-roslyn-analyzer.htm)
- [Microsoft: Write your first analyzer and code fix](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix?WT.mc_id=8B97120A00B57354)
- [Roslyn: Analyzer actions semantics](https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Actions%20Semantics.md)
- [Roslyn analyzer release tracking](https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md)
- [Microsoft: Configure code analysis rules](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/configuration-options?WT.mc_id=8B97120A00B57354)
- [Roslyn Quoter](https://roslynquoter.azurewebsites.net/) and [SharpLab](https://sharplab.io/) for inspecting syntax trees and operations
