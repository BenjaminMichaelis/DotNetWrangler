### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
WRANGLER0001 | Reliability | Warning | Null-forgiving operator hides a possible null value.
WRANGLER0002 | Reliability | Warning | `null!` or `default!` fabricates a non-null value.
WRANGLER0003 | Reliability | Warning | Use a not-null assertion instead of the null-forgiving operator in tests.
WRANGLER0004 | Maintainability | Disabled | Document suppressed diagnostic IDs and titles.
WRANGLER0005 | Maintainability | Disabled | Justify warning suppressions.
WRANGLER0007 | Performance | Warning | Reuse singleton interceptor instances during per-context EF Core options configuration.
