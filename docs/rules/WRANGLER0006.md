# WRANGLER0006: Withdrawn proposal

[Analyzer catalog](../../README.md#analyzer-catalog)

The proposed SaveChanges sync/async callback-pair policy was withdrawn before release. WRANGLER0006 is not emitted, and its ID remains reserved.

Microsoft permits intentional synchronous-only and asynchronous-only interceptors, so callback pairing is not a universal correctness requirement. See [SaveChanges interception](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors#savechanges-interception).

The replacement, [WRANGLER0007: Reuse singleton interceptor instances](WRANGLER0007.md), addresses direct allocations of EF Core singleton interceptors during recognized per-context configuration.
