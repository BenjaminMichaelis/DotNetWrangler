# WRANGLER0006: Justify warning suppressions

[Analyzer catalog](../../README.md#analyzer-catalog) | [Configuration](../configuration.md) | [Suppressions](../suppressions.md)

Default severity: **Disabled (opt-in warning)**. Code fix: **None**.

## Why this rule exists

A rule title says what is being suppressed, not why this instance is intentional. Require an explicit, nonempty `Justification:` field so the exception is reviewable.

This rule is independent of [WRANGLER0005](WRANGLER0005.md), which checks IDs and titles. A title alone is not a reason, and a reason alone is not a title.

## Enable the rule

```ini
[*.cs]
dotnet_diagnostic.WRANGLER0006.severity = warning
```

## Reported code

The title alone does not explain the exception:

```csharp
#pragma warning disable WRANGLER0003 // Null-forgiving operator fabricates a non-null value
```

## Preferred code

Put the reason in the immediately preceding standalone `//` comment:

```csharp
// Justification: Intentionally tests the constructor's null-argument contract.
#pragma warning disable WRANGLER0003 // Null-forgiving operator fabricates a non-null value
    Assert.Throws<ArgumentNullException>(() => new Person(null!));
#pragma warning restore WRANGLER0003
```

Alternatively, use an inline field:

```csharp
#pragma warning disable WRANGLER0003 // Null-forgiving operator fabricates a non-null value; Justification: Tests null rejection.
```

The inline example shows the disable-line syntax; the same narrow suppression and restore pattern still applies.

## Accepted justification syntax

The field name is case-insensitive, permits whitespace before the colon, and needs non-whitespace reason text.

A preceding reason must be on the immediately adjacent line. Blank lines, block/doc comments, comments trailing code, and earlier comments do not qualify. It applies only to the next directive.

One reason can cover all IDs in a directive; use separate directives when their reasons differ.

After an inline `Justification:` starts, the rest of the comment is reason text, including semicolons and diagnostic labels. Put rule titles before the inline justification.

## Limits

This checks presence, not honesty or quality. Placeholders such as `TODO` are not specially rejected.

There is deliberately no automatic justification fix: a tool cannot supply the actual reason for a project's exception.

See [suppression policy boundaries](../suppressions.md#policy-boundaries) for generated/inactive code, restore directives, and suppression of the policy itself.
