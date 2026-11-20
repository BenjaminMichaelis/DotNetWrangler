### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
WR0001 | Usage | Warning | Variables that aren't modified should be made constants.
WR0002 | Reliability | Warning | Null-forgiving operator hides a possible null value.
WR0003 | Reliability | Warning | `null!` or `default!` fabricates a non-null value.
WR0004 | Reliability | Warning | Use a not-null assertion instead of the null-forgiving operator in tests.
