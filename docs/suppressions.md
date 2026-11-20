# Suppressing a diagnostic

[Analyzer catalog](../README.md#analyzer-catalog) | [Configuration](configuration.md)

A diagnostic is a guardrail, not proof that the code is wrong. Some valid uses of `!` still receive a diagnostic: deliberately passing null to test argument validation is one example.

For an intentional exception, suppress only the reported ID around the smallest relevant region and explain why it is needed. Restore the warning immediately afterward.

## Example: testing a null-argument contract

This xUnit test deliberately violates a non-null constructor contract:

```csharp
[Fact]
public void ConstructorRejectsNullName()
{
    // Justification: Null input intentionally exercises argument validation.
#pragma warning disable WRANGLER0003 // Null-forgiving operator fabricates a non-null value
    Assert.Throws<ArgumentNullException>(() => new Person(null!));
#pragma warning restore WRANGLER0003
}
```

`null!` receives WRANGLER0003 even in a test. Other intentional uses may receive WRANGLER0002 or WRANGLER0004; suppress the rule that was actually reported rather than all three.

This format also satisfies both optional pragma policies:

Policy | What it checks
-------|---------------
[WRANGLER0005](rules/WRANGLER0005.md) | Explicit IDs and each rule's title in a same-line comment.
[WRANGLER0006](rules/WRANGLER0006.md) | A nonempty `Justification:` field on the pragma line or the immediately preceding standalone `//` line.

A title identifies the rule; a justification explains this exception. One is not a substitute for the other. These policies check documentation, not the honesty or quality of the reason.

## Policy boundaries

The pragma policies are disabled by default and can be [enabled independently](configuration.md#enable-the-optional-pragma-policies). They skip inactive/generated code and do not inspect restore comments, enforce suppression pairing, or inspect `SuppressMessage` attributes.

Normal severity settings and scoped pragma suppression remain supported for WRANGLER0005 and WRANGLER0006 themselves. A violation on a disable directive is reported at its `disable` keyword: that directive does not hide its own violation unless an earlier suppression already disabled the policy. This is a configurable guardrail, not a tamper-proof boundary.
