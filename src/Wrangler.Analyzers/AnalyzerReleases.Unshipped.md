### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
WRANGLER0001 | Usage | Warning | Variables that aren't modified should be made constants.
WRANGLER0002 | Reliability | Warning | Null-forgiving operator hides a possible null value.
WRANGLER0003 | Reliability | Warning | `null!` or `default!` fabricates a non-null value.
WRANGLER0004 | Reliability | Warning | Use a not-null assertion instead of the null-forgiving operator in tests.
WRANGLER0005 | Maintainability | Disabled | Document suppressed diagnostic IDs and titles.
WRANGLER0006 | Maintainability | Disabled | Justify warning suppressions.
