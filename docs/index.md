---
title: Practical guardrails for .NET
description: Roslyn analyzers for explicit null contracts, meaningful test failures, and reviewable warning suppressions.
_layout: landing
---

<div class="wrangler-hero">
<div class="hero-copy">

<p class="eyebrow">C# / ROSLYN ANALYZERS</p>

# Make intent unmistakable.

<p class="hero-description">Explicit null contracts. Meaningful test failures. Warning suppressions that explain themselves.</p>

Wrangler puts practical guardrails where you write code, without hiding missing data behind fabricated defaults.

<div class="hero-actions">

[Get started](getting-started.md) [Explore the rules](../README.md#analyzer-catalog)

</div>

<p class="hero-note">Individually configurable. Intentional exceptions welcome.</p>

</div>
<figure class="contract-preview">
<div class="preview-header"><span class="preview-file">Person.cs</span><span class="preview-language">C#</span></div>
<div class="preview-body">
<p class="preview-label">Instead of hiding missing data</p>

```csharp
public string Name { get; init; } = null!;
```

<p class="preview-diagnostic"><strong>WRANGLER0003</strong> Null-forgiving operator fabricates a non-null value</p>
<p class="preview-label">Express the contract</p>

```csharp
public required string Name { get; init; }
```

</div>
<figcaption>Require meaningful initialization, or make missing data explicit.</figcaption>
</figure>
</div>

<section class="docs-paths" aria-labelledby="find-your-starting-point">

## Find your starting point

<p class="section-description">A focused rule set, with the reasoning and the escape hatches documented.</p>

<div class="path-grid">
<div class="doc-path">

### Adopt at your pace

Choose a severity for each rule. Enable the optional pragma policies only when they fit your team.

[Configure Wrangler](configuration.md)

</div>
<div class="doc-path">

### Understand the diagnostic

See what triggers a rule, compare preferred code, and check framework-specific advice.

[Browse the analyzer catalog](../README.md#analyzer-catalog)

</div>
<div class="doc-path">

### Keep exceptions explicit

Valid uses can still trigger a diagnostic. Document the reason with a narrow suppression, not a silent workaround.

[Document an exception](suppressions.md)

</div>
</div>
</section>
