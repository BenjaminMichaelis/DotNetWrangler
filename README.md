# Wrangler analyzers

Roslyn analyzers that put guardrails on .NET code.

## Analyzer catalog

ID | Description | Default severity
---|-------------|-----------------
[WR0001](#wr0001) | Make unmodified constant-valued locals `const`. | Warning
[WR0002](#wr0002) | Handle null explicitly instead of overriding nullable analysis with `!`. | Warning
[WR0003](#wr0003) | Avoid fabricating non-null values with `null!` or reference-type `default!`. | Warning
[WR0004](#wr0004) | Prefer a framework-specific not-null assertion in tests. | Warning

## Configuration

Every rule can be enabled, disabled, or assigned a different severity independently using `.editorconfig`:

```ini
root = true

[*.cs]
dotnet_diagnostic.WR0001.severity = warning
dotnet_diagnostic.WR0002.severity = warning
dotnet_diagnostic.WR0003.severity = warning
dotnet_diagnostic.WR0004.severity = warning
```

Set any rule's severity to `none` to turn it off, or `warning` / `error` to enable it. Disabling WR0002 does not disable WR0003 or WR0004. Use a scoped `#pragma warning disable` / `restore` pair for individual intentional exceptions rather than disabling a rule for the entire project.

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
#pragma warning disable WR0003 // Null input intentionally exercises argument validation.
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

## Related tools

- [Nullable.Extended.Analyzer](https://github.com/tom-englert/Nullable.Extended) reports `!` without a `// !` justification comment.
- [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer) has an opt-in rule for `null!` / `default!`.
- The .NET SDK's IDE analyzers report `!` that the compiler doesn't need and offer to remove it.

## Projects

- `src/Wrangler.Analyzers` contains the diagnostic analyzers and targets .NET Standard 2.0.
- `src/Wrangler.CodeFixes` contains the code fix providers and targets .NET Standard 2.0.
- `tests/Wrangler.Tests` contains analyzer and code-fix tests.

## Build and test

```shell
dotnet test DotnetBotWranglers.sln
```
