# WRANGLER0002: Null-forgiving operator fabricates a non-null value

[Analyzer catalog](../../README.md#analyzer-catalog) | [Configuration](../configuration.md) | [Suppressions](../suppressions.md)

Default severity: **Warning**. Code fix: **None**.

## Why this rule exists

`null!` and reference-type defaults such as `default(string)!` tell the compiler a null value is non-null. They move a possible `NullReferenceException` to a later, harder-to-diagnose location.

## Reported code

```csharp
public sealed class Person
{
    public string Name { get; init; } = null!;
}
```

## Preferred code

If callers must provide a name, make that requirement explicit:

```csharp
public sealed class Person
{
    public required string Name { get; init; }
}
```

Alternatively, initialize the member with a real value in the constructor, validating it when needed. If missing data is valid, declare the member nullable.

`required` requires callers to initialize a member; it does not perform runtime null validation. Choose the contract that matches the domain.

Do not replace missing data with an arbitrary default just to silence the warning. An empty first name is not a valid universal default.

## Defaults and generic constraints

This rule takes precedence for `null!` and defaults known to be reference types, including reference-constrained generics.

Defaults of value types, including nullable value types, and generics not known to be reference types receive [WRANGLER0001](WRANGLER0001.md) instead. For example, `default(int)!` does not fabricate a null reference, and a `notnull` constraint alone does not establish that a generic type is a reference type.

## Intentional null-input tests

Deliberately passing null to verify argument validation is valid. These tests still receive WRANGLER0002; the analyzer does not automatically exempt exception assertions or negative tests.

Use a [scoped suppression with a reason](../suppressions.md#example-testing-a-null-argument-contract) to document the intentional contract violation.
