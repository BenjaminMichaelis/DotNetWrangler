# WRANGLER0005: Document suppressed diagnostic IDs and titles

[Analyzer catalog](../../README.md#analyzer-catalog) | [Configuration](../configuration.md) | [Suppressions](../suppressions.md)

Default severity: **Disabled (opt-in warning)**. Code fix: **Document suppressed rule titles**, when titles are available.

## Why this rule exists

A warning ID alone does not explain which rule is being suppressed. Require explicit IDs and human-readable rule titles so readers can understand the suppression without looking them up.

This rule is independent of [WRANGLER0006](WRANGLER0006.md), which checks the reason for the exception.

## Enable the rule

```ini
[*.cs]
dotnet_diagnostic.WRANGLER0005.severity = warning
```

## Reported code

```csharp
#pragma warning disable WRANGLER0003
```

Blanket `#pragma warning disable` directives without IDs also receive a diagnostic.

## Preferred code

Every active `#pragma warning disable` must list explicit diagnostic IDs and name each rule in a same-line `//` comment. Use the diagnostic title, not the analyzer class name.

With one ID, repeating the ID in the comment is unnecessary; this is the format Visual Studio generates:

```csharp
#pragma warning disable WRANGLER0003 // Null-forgiving operator fabricates a non-null value
```

A labeled `WRANGLER0003: Title` is also accepted. Multiple distinct IDs require one labeled title per ID, in any order:

```csharp
#pragma warning disable WRANGLER0002, WRANGLER0003 // WRANGLER0002: Null-forgiving operator hides a possible null value; WRANGLER0003: Null-forgiving operator fabricates a non-null value
```

These examples show the disable-line syntax. Keep each suppression narrowly scoped with a matching restore, as in the [complete suppression example](../suppressions.md#example-testing-a-null-argument-contract).

## Title matching

Wrangler titles are checked against the descriptors' invariant-culture titles. Matching is case-sensitive and ignores surrounding whitespace, not punctuation. Numeric compiler warnings such as `0168` are matched as `CS0168`; repeated equivalent IDs count only once.

Semicolons are ordinary title text except before a `Justification:` field or, for multiple IDs, a declared `ID:` label. Those are reserved metadata boundaries. Catalog titles cannot contain the reserved justification field; use separate directives if a title contains another suppressed ID's label.

## External title catalogs

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

Entries use `ID = Title`, splitting at the first equals sign and trimming surrounding whitespace. Titles preserve `C#`, semicolons and further equals signs; there is no quoting or inline-comment syntax. Blank lines and lines beginning with `#` are ignored.

Use named IDs, including normalized `CS0168`, not bare numbers. Entries for `WRANGLER####` are reserved for Wrangler.

Files with this name are merged; duplicate IDs, malformed entries and unreadable files reject the catalog rather than silently accepting a partial result. External entries define project-approved wording; they are not automatically synchronized with package updates or localized IDE titles.

### Validation modes

```ini
[*.cs]
dotnet_diagnostic.WRANGLER0005.severity = warning
wrangler_pragma_title_validation = documented
```

Mode | Behavior
-----|---------
`documented` (default) | Verify Wrangler and supplied catalog titles; require nonempty titles for all other IDs.
`strict` | Also require a catalog entry for every compiler or third-party ID.

The mode can be scoped with ordinary `.editorconfig` sections. Invalid modes or catalogs produce WRANGLER0005 at active disable directives, so the file's normal severity and suppression settings apply.

No pragmas means no configuration diagnostic. These configuration errors do not prevent WRANGLER0006 from checking reasons.

## Code-fix limits

The code fix inserts available titles and corrects explicitly labeled titles, preserving justification text, line endings and suppression scope.

For external analyzers it can obtain titles from the project's Roslyn analyzer references; build-time verification still needs a catalog. Strict mode does not offer a title-only fix for a missing catalog entry.

Ambiguous free-text comments, unavailable/conflicting titles and blanket disables have no automatic fix. The fix never invents suppression IDs or a justification.

## Policy boundaries

See [suppression policy boundaries](../suppressions.md#policy-boundaries) for generated/inactive code, restore directives, and suppression of the policy itself.
