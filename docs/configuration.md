# Configuration

[Analyzer catalog](../README.md#analyzer-catalog) | [Getting started](getting-started.md) | [Suppressions](suppressions.md)

Every Wrangler rule supports Roslyn's standard `dotnet_diagnostic.<ID>.severity` settings. Disabling one rule does not disable the others.

## Default policy

The following `.editorconfig` section makes the default policy explicit. You do not need to add it just to use the defaults:

```ini
[*.cs]
dotnet_diagnostic.WRANGLER0001.severity = warning
dotnet_diagnostic.WRANGLER0002.severity = warning
dotnet_diagnostic.WRANGLER0003.severity = warning
dotnet_diagnostic.WRANGLER0004.severity = none
dotnet_diagnostic.WRANGLER0005.severity = none
dotnet_diagnostic.WRANGLER0006.severity = none
```

Add settings to your existing `.editorconfig`, or create one at the appropriate directory level.

Severity | Effect
---------|-------
`none` | Disable the rule.
`silent` | Keep the diagnostic hidden from normal warning output.
`suggestion` | Show an editor suggestion.
`warning` | Report a warning.
`error` | Report an error.

Build settings such as treating warnings as errors can further affect the build result.

## Enable the optional pragma policies

Add these settings to the applicable section to enable both policies:

```ini
[*.cs]
dotnet_diagnostic.WRANGLER0004.severity = warning
dotnet_diagnostic.WRANGLER0005.severity = warning
```

These policies are independent:

- [WRANGLER0004](rules/WRANGLER0004.md) requires explicit suppression IDs and rule titles.
- [WRANGLER0005](rules/WRANGLER0005.md) requires an explicit, nonempty `Justification:` field.

Either can be enabled without the other. See [suppressions](suppressions.md) for an example that satisfies both.

## Enable the optional SaveChanges interceptor policy

```ini
[*.cs]
dotnet_diagnostic.WRANGLER0006.severity = warning
```

[WRANGLER0006](rules/WRANGLER0006.md) reports a custom EF Core SaveChanges interceptor callback when its sync/async counterpart is missing. It checks implementation presence, not equivalent behavior or which save paths an application uses. Intentional one-sided implementations can use a scoped suppression.

## Scope a setting

Ordinary `.editorconfig` sections let you choose a different severity for a subset of files. For example, after a broader `[*.cs]` section:

```ini
[tests/**/*.cs]
dotnet_diagnostic.WRANGLER0001.severity = error
```

Paths are relative to the `.editorconfig` file. For individual intentional exceptions, prefer a [scoped pragma](suppressions.md).

## External pragma titles

WRANGLER0004 verifies Wrangler titles automatically. It can also validate compiler and third-party rule titles using an optional `Wrangler.PragmaTitles.txt` additional file.

The `wrangler_pragma_title_validation` option controls whether unmapped external IDs need only a title (`documented`, the default) or also need a catalog mapping (`strict`). It does not enable the rule.

See [WRANGLER0004: external title catalogs](rules/WRANGLER0004.md#external-title-catalogs) for the file format, project setup, and validation rules.
