# WRANGLER0001: Null-forgiving operator hides a possible null value

[Analyzer catalog](../../README.md#analyzer-catalog) | [Configuration](../configuration.md) | [Suppressions](../suppressions.md)

Default severity: **Warning**. Code fix: **None**.

## Why this rule exists

The null-forgiving operator (`!`) overrides the compiler's nullable analysis without changing the runtime value. It is not a null check.

Only use `!` when you can prove the value is non-null and the compiler cannot, or when deliberately violating a contract such as a null-input test. These legitimate uses still receive diagnostics; document them with a [scoped suppression and a reason](../suppressions.md).

## Reported code

```csharp
static int GetNameLength(string? name) => name!.Length;
```

## Preferred code

If a missing name violates the method's contract, fail at the boundary:

```csharp
static int GetNameLength(string? name)
{
    if (name is null)
    {
        throw new ArgumentNullException(nameof(name));
    }

    return name.Length;
}
```

If null is a meaningful result instead, preserve that meaning with a nullable return type and `?.`. When a helper guarantees a value is non-null, express the guarantee through its nullable contract or attributes such as `[NotNullWhen]` and `[MemberNotNull]`.

There is no automatic fix: a null check, conditional access, a throw, and a changed nullable contract have different behavior. Do not mechanically replace `!` with `?.` or an arbitrary default.

## Which null-forgiving rule applies

The analyzer chooses one diagnostic for each null-forgiving expression:

Expression or context | Diagnostic
----------------------|-----------
`null!`, or a default known to be a reference type, including a reference-constrained generic | [WRANGLER0002](WRANGLER0002.md), including in tests.
Other defaults, such as `default(int)!`, nullable value-type defaults, and unconstrained/`notnull` generic defaults | WRANGLER0001, including in tests.
Other `!` expressions in recognized test classes, where framework-specific advice is applicable | [WRANGLER0003](WRANGLER0003.md).
Other `!` expressions | WRANGLER0001.

Referencing a test framework or naming a class `*Tests` is not enough to qualify for WRANGLER0003. Its [framework and async requirements](WRANGLER0003.md#framework-compatibility) determine whether the advice applies.

Prefix logical negation (`!condition`) and null-conditional access (`value?.Member`) are not null-forgiving operators.
