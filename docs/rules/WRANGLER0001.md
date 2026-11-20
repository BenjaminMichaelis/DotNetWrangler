# WRANGLER0001: Variable can be made constant

[Analyzer catalog](../../README.md#analyzer-catalog) | [Configuration](../configuration.md) | [Suppressions](../suppressions.md)

Default severity: **Warning**. Code fix: **Make constant**.

## Why this rule exists

A constant-valued local that is never modified should express that intent with `const`. This makes the contract visible to readers and lets the compiler prevent later assignment.

## Reported code

```csharp
static int GetRetryLimit()
{
    int retryLimit = 3;
    return retryLimit;
}
```

## Preferred code

```csharp
static int GetRetryLimit()
{
    const int retryLimit = 3;
    return retryLimit;
}
```

The code fix adds `const` and replaces `var` with an explicit type when needed.

## Scope and exceptions

The rule looks for local declarations with constant-valued initializers and no later writes. An unmodified local initialized by a runtime method call is not a constant just because it is never reassigned.

If retaining a non-constant local is intentional, use a [scoped suppression with a reason](../suppressions.md).
