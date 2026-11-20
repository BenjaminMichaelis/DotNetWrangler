# Wrangler analyzers

Roslyn analyzers that put guardrails on .NET code.

## Analyzer catalog

ID | Description | Default severity
---|-------------|-----------------
[WR0001](#wr0001) | Make unmodified constant-valued locals `const`. | Warning
[WR0002](#wr0002) | Handle null explicitly instead of overriding nullable analysis with `!`. | Warning
[WR0003](#wr0003) | Avoid fabricating non-null values with `null!` or reference-type `default!`. | Warning
[WR0004](#wr0004) | Prefer a framework-specific not-null assertion in tests. | Warning
[WR0005](#wr0005) | Document explicit pragma suppression IDs and rule titles. | Disabled (opt-in warning)
[WR0006](#wr0006) | Require an explicit justification for pragma suppressions. | Disabled (opt-in warning)

## Configuration

Every rule can be enabled, disabled, or assigned a different severity independently using `.editorconfig`:

```ini
root = true

[*.cs]
dotnet_diagnostic.WR0001.severity = warning
dotnet_diagnostic.WR0002.severity = warning
dotnet_diagnostic.WR0003.severity = warning
dotnet_diagnostic.WR0004.severity = warning
dotnet_diagnostic.WR0005.severity = warning
dotnet_diagnostic.WR0006.severity = warning
```

Set any rule's severity to `none` to turn it off, or `warning` / `error` to enable it. Disabling one rule does not disable the others. WR0005 and WR0006 are opt-in: the example enables both, but either can be enabled alone. Use a scoped `#pragma warning disable` / `restore` pair for individual intentional exceptions rather than disabling a rule for the entire project.

## Rule details

### WR0001

Local variables that are never modified should be `const`. A code fix is available.

### WR0002

The null-forgiving operator (`!`) overrides the compiler's nullable analysis. Normally it should only be used when you can prove the value is non-null and the compiler cannot. Prefer a null check, `?.`, a throw for contract violations, or nullable attributes such as `[NotNullWhen]` and `[MemberNotNull]`. Intentional null-input tests are another legitimate exception. When `!` is genuinely required, use a scoped pragma suppression with a reason, as shown under [WR0003](#wr0003).

### WR0003

`null!` and reference-type defaults such as `default(string)!` fabricate a non-null value and move the `NullReferenceException` somewhere harder to diagnose. Make the member nullable, `required`, or initialize it with a real value. Don't replace it with an arbitrary default (what is the default first name?). Defaults of value types (including nullable value types) and generics not known to be reference types use the general null-forgiving diagnostic instead of WR0003.

Deliberately passing null to verify argument validation is a valid use of `!`: the test is intentionally violating the non-null contract. The analyzer does not silently exempt these tests. Document the exception with a narrowly scoped pragma for the reported rule:

```csharp
[Fact]
public void ConstructorRejectsNullName()
{
    // Justification: Null input intentionally exercises argument validation.
#pragma warning disable WR0003 // Null-forgiving operator fabricates a non-null value
    Assert.Throws<ArgumentNullException>(() => new Person(null!));
#pragma warning restore WR0003
}
```

Other justified uses of `!` follow the same approach, suppressing WR0002 or WR0004 when that is the diagnostic reported. A justification makes the exception reviewable; the analyzer does not attempt to validate the wording of that reason.

### WR0004

Inside test classes, use the framework's not-null assertion instead of `!` so an unexpected null produces an assertion failure rather than a later `NullReferenceException`. A class is recognized by test methods declared on it or a base class, including derived test attributes. `!` elsewhere in a test project is reported as WR0002. `null!` and reference-type defaults still receive WR0003; other defaults receive WR0002.

Framework | Recognized test attributes | Recommended assertion
----------|----------------------------|----------------------
MSTest | `[TestMethod]`, including `[DataTestMethod]` | `Assert.IsNotNull(value)`
xUnit | `[Fact]`, including `[Theory]` | `Assert.NotNull(value)`
NUnit 3.x, 4.x, 5.x | `[Test]`, `[TestCase]`, `[TestCaseSource]`, `[Theory]` | `Assert.That(value, Is.Not.Null)`
TUnit 1.x | `[Test]` from `TUnit.Core` | `await Assert.That(value).IsNotNull()`

Detection uses referenced framework type symbols rather than package-version strings. NUnit's [constraint syntax](https://docs.nunit.org/articles/nunit/writing-tests/constraints/NullConstraint.html) works across all three supported major versions, avoiding the classic-assertion changes in NUnit 4.0 and 4.5 (the latter also requires C# 14 for the restored classic syntax). TUnit's [assertions must be awaited](https://tunit.dev/docs/assertions/awaiting/); tests and helpers using them must support asynchronous execution. TUnit also requires `TUnit.Assertions` and its extension namespace. NUnit 2.x and pre-1.0 TUnit are outside the documented compatibility targets; no per-minor or per-patch version branches are needed.

TUnit assertion advice is limited to asynchronous methods, local functions, and lambdas where awaiting is possible. Constructors, initializers, getters, synchronous helpers, and restricted contexts such as `lock` bodies receive WR0002 instead of an unusable awaited-assertion recommendation.

Nullable-warning behavior is framework-dependent, not a guarantee of WR0004. MSTest/xUnit assertions can expose nullable annotations. For NUnit constraint assertions, [NUnit.Analyzers' NUnit3001 suppressor](https://docs.nunit.org/articles/nunit-analyzers/NUnit3001.html) handles subsequent nullable warnings, except inside multiple-assert blocks where execution can continue after a failed assertion. TUnit provides an assertion suppressor and also supports [capturing the validated return value](https://tunit.dev/docs/assertions/awaiting/#using-return-values-from-awaited-assertions); its suppressor does not change the compiler's null-state. Don't assume every assertion syntax or older framework version eliminates nullable warnings by itself.

### WR0005

Opt-in. Every active `#pragma warning disable` must list explicit diagnostic IDs and name each rule in a same-line `//` comment. Use the human-readable diagnostic title, not the analyzer class name. With one ID, repeating the ID in the comment is unnecessary; this is the format Visual Studio generates:

```csharp
#pragma warning disable WR0003 // Null-forgiving operator fabricates a non-null value
```

A labeled `WR0003: Title` is also accepted. Multiple distinct IDs require one labeled title per ID, in any order:

```csharp
#pragma warning disable WR0002, WR0003 // WR0002: Null-forgiving operator hides a possible null value; WR0003: Null-forgiving operator fabricates a non-null value
```

Wrangler titles are checked against the descriptors' invariant-culture titles. Matching is case-sensitive and ignores surrounding whitespace, not punctuation. Numeric compiler warnings such as `0168` are matched as `CS0168`; repeated equivalent IDs count only once.

By default, compiler and third-party IDs require a nonempty title, without claiming its wording has been verified. To verify external titles too, supply an optional `Wrangler.PragmaTitles.txt` additional file:

```xml
<ItemGroup>
  <AdditionalFiles Include="Wrangler.PragmaTitles.txt" />
</ItemGroup>
```

```text
# Project-approved external titles
CONTOSO001 = Prefer C# interfaces; avoid synchronous calls
```

Entries use `ID = Title`, splitting at the first equals sign and trimming surrounding whitespace. Titles preserve `C#`, semicolons and further equals signs; there is no quoting or inline-comment syntax. Blank lines and lines beginning with `#` are ignored. Use named IDs, including normalized `CS0168`, not bare numbers. Entries for `WR####` are reserved for Wrangler. Files with this name are merged; duplicate IDs, malformed entries and unreadable files reject the catalog rather than silently accepting a partial result. External entries define project-approved wording; they are not automatically synchronized with package updates or localized IDE titles.

```ini
[*.cs]
dotnet_diagnostic.WR0005.severity = warning
wrangler_pragma_title_validation = documented
```

Mode | Behavior
-----|---------
`documented` (default) | Verify Wrangler and supplied catalog titles; require nonempty titles for all other IDs.
`strict` | Also require a catalog entry for every compiler or third-party ID.

The mode can be scoped with ordinary `.editorconfig` sections. Invalid modes or catalogs produce WR0005 at active disable directives, so the file's normal severity and suppression settings apply. No pragmas means no configuration diagnostic. These configuration errors do not prevent WR0006 from checking reasons.

Semicolons are ordinary title text except before a `Justification:` field or, for multiple IDs, a declared `ID:` label. Those are reserved metadata boundaries. Catalog titles cannot contain the reserved justification field; use separate directives if a title contains another suppressed ID's label.

A code fix inserts available titles and corrects explicitly labeled titles, preserving justification text, line endings and suppression scope. For external analyzers it can obtain titles from the project's Roslyn analyzer references; build-time verification still needs a catalog. Strict mode does not offer a title-only fix for a missing catalog entry. Ambiguous free-text comments, unavailable/conflicting titles and blanket disables have no automatic fix. The fix never invents suppression IDs or a justification.

### WR0006

Opt-in, independent of WR0005. Require a nonempty `Justification:` field either in the pragma's same-line comment or in the immediately preceding standalone `//` comment:

```csharp
// Justification: Intentionally tests the constructor's null-argument contract.
#pragma warning disable WR0003 // Null-forgiving operator fabricates a non-null value
    Assert.Throws<ArgumentNullException>(() => new Person(null!));
#pragma warning restore WR0003
```

```csharp
#pragma warning disable WR0003 // Null-forgiving operator fabricates a non-null value; Justification: Tests null rejection.
```

The field name is case-insensitive, permits whitespace before the colon, and needs non-whitespace reason text. A preceding reason must be on the immediately adjacent line: blank lines, block/doc comments, comments trailing code, and earlier comments do not qualify. It applies only to the next directive. One reason can cover all IDs in a directive; use separate directives when their reasons differ.

After an inline `Justification:` starts, the rest of the comment is reason text, including semicolons and diagnostic labels. A title alone is not a reason, and a reason alone is not a title. This checks presence, not honesty or quality; placeholders such as `TODO` are not specially rejected. There is deliberately no automatic justification fix.

Both pragma policies skip inactive/generated code and leave restore comments and suppression-pairing enforcement out of scope. They do not inspect `SuppressMessage` attributes. Normal severity settings and scoped pragma suppression remain supported, including for WR0005/WR0006 themselves. A violation on a disable directive remains visible at its `disable` keyword unless an earlier suppression already disabled the policy; this is a configurable guardrail, not a tamper-proof boundary.

## Related tools

- [Nullable.Extended.Analyzer](https://github.com/tom-englert/Nullable.Extended) reports `!` without a `// !` justification comment.
- [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer) has an opt-in rule for `null!` / `default!`.
- The .NET SDK's IDE analyzers report `!` that the compiler doesn't need and offer to remove it.
- StyleCop [SA1404](https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/documentation/SA1404.md) requires justifications on `SuppressMessage` attributes, not pragma title/reason comments. The related pragma proposal [DotNetAnalyzers/StyleCopAnalyzers#3403](https://github.com/DotNetAnalyzers/StyleCopAnalyzers/issues/3403) motivated similar discussion.
- The SDK's [IDE0079](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/ide0079) finds unnecessary suppressions; WR0005/WR0006 document the suppressions that remain.

## Projects

- `src/Wrangler.Analyzers` contains the diagnostic analyzers and targets .NET Standard 2.0.
- `src/Wrangler.CodeFixes` contains the code fix providers and targets .NET Standard 2.0.
- `tests/Wrangler.Tests` contains analyzer and code-fix tests.

## Build and test

```shell
dotnet test DotnetBotWranglers.sln
```
