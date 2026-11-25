# WRANGLER0007: Reuse singleton interceptor instances

[Analyzer catalog](../../README.md#analyzer-catalog) | [Configuration](../configuration.md) | [Suppressions](../suppressions.md)

Default severity: **Warning**. Code fix: **None**.

## Why this rule exists

EF Core interceptors implementing `ISingletonInterceptor` become part of EF's internal service configuration. Distinct instances can cause new internal service providers to be created. Microsoft warns that allocating a new singleton interceptor each time a context is configured eventually triggers `ManyServiceProvidersCreatedWarning` and degrades performance.

Reuse an appropriately thread-safe instance instead of creating one in per-context configuration. This is not a ban on allocating every interceptor: ordinary SaveChanges interceptors do not inherently implement the singleton marker and can legitimately hold per-context state.

## Reported code

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

public sealed class AppContext : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(new AppMaterializationInterceptor());
    }
}

public sealed class AppMaterializationInterceptor : IMaterializationInterceptor
{
}
```

`IMaterializationInterceptor` inherits `ISingletonInterceptor`, so the direct allocation creates a distinct singleton interceptor for each configuration execution.

The same problem occurs in an inline, scoped `AddDbContext` options callback:

```csharp
services.AddDbContext<AppContext>(options =>
    options.AddInterceptors(new AppMaterializationInterceptor()));
```

## Preferred code

For a stateless interceptor, reuse a shared instance:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

public sealed class AppContext : DbContext
{
    private static readonly AppMaterializationInterceptor Interceptor = new();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(Interceptor);
    }
}

public sealed class AppMaterializationInterceptor : IMaterializationInterceptor
{
}
```

Alternatively, resolve a singleton interceptor registered in dependency injection inside the service-provider-aware `AddDbContext` callback. Do not resolve a transient service and assume that DI makes it singleton.

Shared interceptors must be thread-safe. Do not mechanically move an interceptor with context-specific dependencies or mutable state into a static field; choose its design and lifetime deliberately.

## Detection boundary

The rule requires all of the following:

1. A direct object creation whose semantic type implements the referenced EF Core `ISingletonInterceptor` interface, including through inheritance.
2. Registration through the canonical `DbContextOptionsBuilder.AddInterceptors` API.
3. The receiver is the options builder being configured, rather than an unrelated builder created inside the same method.
4. The registration occurs directly in a canonical `DbContext.OnConfiguring` override or an inline options callback passed to canonical `AddDbContext`.

For `AddDbContext`, only statically known scoped or transient effective options lifetimes are checked. Singleton options, singleton contexts that force singleton options, and unknown lifetimes are skipped. Service-provider-aware callbacks are supported too.

Direct argument arrays and collection expressions without spreads are supported, as are canonical EF fluent chains on the configuration parameter. The parameter must remain unchanged; reassignment, writable ref aliases, and `ref`/`out` arguments cause the rule to skip that configuration function. User-defined conversions and indirect builder aliases are outside the detection boundary.

## Not reported

- Cached instances, fields, properties, local variables, and arbitrary factory results. These are not proof of correct reuse; their allocation flow is outside this rule's scope.
- Non-singleton interceptors, including ordinary SaveChanges interceptors without `ISingletonInterceptor`.
- Standalone, one-time options construction or registration on an unrelated options builder.
- Pooling and factory registration APIs, callbacks supplied through method groups or stored delegates, and allocations in nested functions.
- Source-defined framework lookalikes, generated code, and incomplete constructs lacking the required semantic information.

The analyzer recognizes a risky registration pattern; it does not prove how many contexts an application will create, inspect constructor behavior, or reconstruct arbitrary factory and caching logic. Skipped forms are not necessarily safe.

## Configuration and intentional exceptions

The rule is enabled by default and supports normal per-rule severity settings:

```ini
[*.cs]
dotnet_diagnostic.WRANGLER0007.severity = warning
```

For an intentional exception, suppress the allocation narrowly and document the reason:

```csharp
// Justification: This isolated test intentionally exercises EF's distinct-instance warning.
#pragma warning disable WRANGLER0007 // Reuse singleton interceptor instances
optionsBuilder.AddInterceptors(new AppMaterializationInterceptor());
#pragma warning restore WRANGLER0007
```

There is no automatic code fix: selecting a safe shared instance and its dependencies requires application knowledge.

## Microsoft references

- [Singleton interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors#singleton-interceptors): reuse requirements and thread safety.
- [Registering interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors#registering-interceptors): internal service provider and repeated-instance warning.
- [ISingletonInterceptor API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.diagnostics.isingletoninterceptor): the marker contract.
- [AddDbContext API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.entityframeworkservicecollectionextensions.adddbcontext): configuration callbacks and lifetime parameters.
