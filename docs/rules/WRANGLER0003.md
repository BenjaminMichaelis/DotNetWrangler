# WRANGLER0003: Use a not-null assertion instead of the null-forgiving operator in tests

[Analyzer catalog](../../README.md#analyzer-catalog) | [Configuration](../configuration.md) | [Suppressions](../suppressions.md)

Default severity: **Warning**. Code fix: **None**.

## Why this rule exists

Inside test classes, use the framework's not-null assertion instead of `!`. An unexpected null should fail with a clear assertion message, not a later `NullReferenceException`.

## Reported code

In an MSTest test method, given a nullable `actual` value:

```csharp
Assert.AreEqual(4, actual!.Length);
```

## Preferred code

Assert the expectation before using the value:

```csharp
Assert.IsNotNull(actual);
Assert.AreEqual(4, actual.Length);
```

Choose the assertion for your framework below. Nullable-warning handling depends on the assertion API and framework tooling; not every assertion updates compiler null-state.

## Framework compatibility

Framework | Recognized test attributes | Recommended assertion
----------|----------------------------|----------------------
MSTest | `[TestMethod]`, including `[DataTestMethod]` | `Assert.IsNotNull(value)`
xUnit | `[Fact]`, including `[Theory]` | `Assert.NotNull(value)`
NUnit 3.x, 4.x, 5.x | `[Test]`, `[TestCase]`, `[TestCaseSource]`, `[Theory]` | `Assert.That(value, Is.Not.Null)`
TUnit 1.x | `[Test]` from `TUnit.Core` | `await Assert.That(value).IsNotNull()`

A class is recognized by test methods declared on it or a base class, including derived test attributes. Detection uses referenced framework type symbols rather than package-version strings. Merely referencing a test framework, naming a class `*Tests`, or defining framework lookalikes in analyzed source is not sufficient.

NUnit's [constraint syntax](https://docs.nunit.org/articles/nunit/writing-tests/constraints/NullConstraint.html) works across all three supported major versions, avoiding the classic-assertion changes in NUnit 4.0 and 4.5 (the latter also requires C# 14 for the restored classic syntax). NUnit 2.x and pre-1.0 TUnit are outside the documented compatibility targets; no per-minor or per-patch version branches are needed.

### TUnit and async contexts

TUnit's [assertions must be awaited](https://tunit.dev/docs/assertions/awaiting/). TUnit also requires `TUnit.Assertions` and its extension namespace.

Advice is limited to asynchronous methods, local functions, and lambdas where awaiting is possible. The nearest function boundary matters: an async outer method does not make a synchronous nested helper awaitable.

Constructors, initializers, getters, synchronous helpers, and await-forbidden contexts such as `lock` bodies receive WRANGLER0001 instead. Enclosing unsafe contexts are also considered so the analyzer does not recommend an illegal `await`.

### Nullable-warning behavior

MSTest/xUnit assertions can expose nullable annotations. For NUnit constraint assertions, [NUnit.Analyzers' NUnit3001 suppressor](https://docs.nunit.org/articles/nunit-analyzers/NUnit3001.html) handles subsequent nullable warnings, except inside multiple-assert blocks where execution can continue after a failed assertion.

TUnit provides an assertion suppressor and supports [capturing the validated return value](https://tunit.dev/docs/assertions/awaiting/#using-return-values-from-awaited-assertions). Its suppressor does not change the compiler's null-state.

These are different mechanisms. Do not assume every assertion syntax or older framework version eliminates nullable warnings by itself.

## Fallbacks and exceptions

`!` elsewhere in a test project receives WRANGLER0001. `null!` and reference-type defaults still receive WRANGLER0002; other defaults receive WRANGLER0001. See the [rule selection table](WRANGLER0001.md#which-null-forgiving-rule-applies).

Intentional exceptions, including null-input tests, still receive diagnostics. Use a [scoped suppression with a reason](../suppressions.md).
