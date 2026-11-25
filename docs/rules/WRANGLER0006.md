# WRANGLER0006: Implement both synchronous and asynchronous SaveChanges interceptor callbacks

[Analyzer catalog](../../README.md#analyzer-catalog) | [Configuration](../configuration.md) | [Suppressions](../suppressions.md)

Default severity: **Disabled (opt-in warning)**. Code fix: **None**.

## Why this rule exists

An interceptor that customizes only one save path can leave the other path with different interception behavior. This opt-in policy calls out a custom callback whose synchronous or asynchronous counterpart is missing.

Microsoft recommends implementing both when an application may call either `SaveChanges` or `SaveChangesAsync`, but also explicitly permits one-sided implementations when only one path is used. This diagnostic enforces a team's paired-callback policy; it is not proof of an EF Core bug.

## Enable the rule

```ini
[*.cs]
dotnet_diagnostic.WRANGLER0006.severity = warning
```

## Reported code

```csharp
using Microsoft.EntityFrameworkCore.Diagnostics;

public sealed class SaveObserver : SaveChangesInterceptor
{
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Observe(result);
        return result;
    }

    private static void Observe(int result) => System.Console.WriteLine(result);
}
```

`SavedChanges` has a custom implementation, but `SavedChangesAsync` retains EF Core's default implementation.

## Preferred code

When both paths should be supported, implement both callbacks:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

public sealed class SaveObserver : SaveChangesInterceptor
{
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Observe(result);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Observe(result);
        return new ValueTask<int>(result);
    }

    private static void Observe(int result) => System.Console.WriteLine(result);
}
```

Sharing synchronous observation is appropriate here because it does not perform asynchronous work. Other interceptors may require different implementations. Do not block asynchronous work with `.Result` or `.Wait()` merely to satisfy the rule.

## Checked pairs

Synchronous callback | Asynchronous counterpart
---------------------|--------------------------
`SavingChanges` | `SavingChangesAsync`
`SavedChanges` | `SavedChangesAsync`
`SaveChangesFailed` | `SaveChangesFailedAsync`
`SaveChangesCanceled` | `SaveChangesCanceledAsync`
`ThrowingConcurrencyException` | `ThrowingConcurrencyExceptionAsync`

Only pairs available in the referenced EF Core API are checked. Implementing either side without the other reports; implementing neither is allowed. These are sync/async counterparts, not before/after lifecycle pairs: implementing `SavingChanges` does not require `SavedChanges`.

The rule recognizes canonical `ISaveChangesInterceptor` implementations, including explicit interface implementations and inherited custom implementations. EF Core's default `SaveChangesInterceptor` methods do not count as custom implementations. Partial declarations are considered together. Unrelated overloads and similarly named APIs do not satisfy the policy.

## Intentional exceptions

An async-only application can intentionally omit synchronous callbacks. A synchronous guard that throws to prohibit `SaveChanges` is another valid policy. Neither intent is inferred from method bodies.

Document the exception with a narrow suppression around the reported declaration:

```csharp
// Justification: This application uses only synchronous saves.
#pragma warning disable WRANGLER0006 // Implement both synchronous and asynchronous SaveChanges interceptor callbacks
public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
{
    Observe(result);
    return result;
}
#pragma warning restore WRANGLER0006
```

The comment also satisfies the optional [title](WRANGLER0004.md) and [justification](WRANGLER0005.md) policies.

## Limits

- This checks implementation presence, not equivalent behavior, application call sites, registration, or runtime usage.
- An explicit implementation counts even if it only delegates to a base method, returns a default, or throws.
- Command, connection, transaction, and other interceptor families are outside this rule's scope.
- Generated code and source-defined framework lookalikes are not analyzed as canonical EF Core callbacks.
- No automatic fix is supplied: choosing correct synchronous and asynchronous behavior requires application knowledge.

## Microsoft references

- [SaveChanges interception](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors#savechanges-interception): examples, sync/async guidance, and intentional async-only guards.
- [ISaveChangesInterceptor API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.diagnostics.isavechangesinterceptor): available callback contracts.
