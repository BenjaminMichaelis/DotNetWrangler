using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Wrangler.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SingletonInterceptorAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "WRANGLER0007";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Reuse singleton interceptor instances",
        "Reuse singleton interceptor '{0}' instead of allocating it during per-context options configuration",
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinks.ForRule(DiagnosticId));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            Compilation compilation = start.Compilation;
            INamedTypeSymbol? singleton = FrameworkType("Microsoft.EntityFrameworkCore.Diagnostics.ISingletonInterceptor");
            INamedTypeSymbol? interceptor = FrameworkType("Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor");
            INamedTypeSymbol? builder = FrameworkType("Microsoft.EntityFrameworkCore.DbContextOptionsBuilder");
            INamedTypeSymbol? genericBuilder = FrameworkType("Microsoft.EntityFrameworkCore.DbContextOptionsBuilder`1");
            INamedTypeSymbol? dbContext = FrameworkType("Microsoft.EntityFrameworkCore.DbContext");
            if (singleton?.TypeKind != TypeKind.Interface || interceptor?.TypeKind != TypeKind.Interface
                || builder is null || dbContext is null)
            {
                return;
            }

            INamedTypeSymbol? extensions = FrameworkType("Microsoft.Extensions.DependencyInjection.EntityFrameworkServiceCollectionExtensions");
            INamedTypeSymbol? lifetime = compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceLifetime");
            INamedTypeSymbol? services = compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceCollection");
            INamedTypeSymbol? action = compilation.GetTypeByMetadataName("System.Action`1");
            INamedTypeSymbol? providerAction = compilation.GetTypeByMetadataName("System.Action`2");
            INamedTypeSymbol? provider = compilation.GetTypeByMetadataName("System.IServiceProvider");
            INamedTypeSymbol? enumerable = compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1");
            var addInterceptors = builder.GetMembers("AddInterceptors").OfType<IMethodSymbol>()
                .Concat(genericBuilder?.GetMembers("AddInterceptors").OfType<IMethodSymbol>() ?? [])
                .Where(method => !method.IsStatic && method.Parameters.Length == 1 && IsBuilder(method.ReturnType)
                    && IsInterceptorSequence(method.Parameters[0].Type))
                .ToImmutableArray();
            var onConfiguring = dbContext.GetMembers("OnConfiguring").OfType<IMethodSymbol>()
                .Where(method => method.IsVirtual && method.Parameters.Length == 1
                    && Equal(method.Parameters[0].Type, builder) && method.ReturnsVoid).ToImmutableArray();
            var registrations = extensions?.GetMembers("AddDbContext").OfType<IMethodSymbol>()
                .Where(IsRegistration).ToImmutableArray() ?? [];
            var writtenParameters = new ConcurrentDictionary<IParameterSymbol, bool>(SymbolEqualityComparer.Default);

            start.RegisterOperationAction(operationContext =>
            {
                var invocation = (IInvocationOperation)operationContext.Operation;
                if (!addInterceptors.Any(method => Equal(method, invocation.TargetMethod.OriginalDefinition))
                    || invocation.Syntax.ContainsDiagnostics)
                {
                    return;
                }

                IParameterSymbol? parameter = ConfigurationParameter(invocation, operationContext.ContainingSymbol, operationContext.CancellationToken);
                if (parameter is null || !IsConfiguredBuilder(invocation.Instance, parameter))
                {
                    return;
                }

                foreach (IArgumentOperation argument in invocation.Arguments)
                {
                    ReportCreations(argument.Value);
                }

                void ReportCreations(IOperation value)
                {
                    operationContext.CancellationToken.ThrowIfCancellationRequested();
                    value = Unwrap(value);
                    switch (value)
                    {
                        case IObjectCreationOperation creation when creation.Type is INamedTypeSymbol type
                            && type.AllInterfaces.Any(item => Equal(item, singleton))
                            && !creation.Syntax.ContainsDiagnostics:
                            operationContext.ReportDiagnostic(Diagnostic.Create(Rule, creation.Syntax.GetLocation(), type.Name));
                            break;
                        case IArrayCreationOperation { Initializer: { } initializer }:
                            foreach (IOperation element in initializer.ElementValues)
                            {
                                ReportCreations(element);
                            }
                            break;
                        case ICollectionExpressionOperation collection:
                            foreach (IOperation element in collection.Elements)
                            {
                                if (element is not ISpreadOperation)
                                {
                                    ReportCreations(element);
                                }
                            }
                            break;
                    }
                }
            }, OperationKind.Invocation);

            INamedTypeSymbol? FrameworkType(string name)
            {
                INamedTypeSymbol? type = compilation.GetTypeByMetadataName(name);
                return type is not null && !type.Locations.Any(location => location.IsInSource)
                    && type.ContainingAssembly.Identity.Name == "Microsoft.EntityFrameworkCore" ? type : null;
            }

            bool IsBuilder(ITypeSymbol? type) => type is INamedTypeSymbol named
                && (Equal(named.OriginalDefinition, builder) || Equal(named.OriginalDefinition, genericBuilder));

            bool IsInterceptorSequence(ITypeSymbol type) => type is IArrayTypeSymbol array
                ? Equal(array.ElementType, interceptor)
                : type is INamedTypeSymbol named && Equal(named.OriginalDefinition, enumerable)
                    && named.TypeArguments.Length == 1 && Equal(named.TypeArguments[0], interceptor);

            bool IsRegistration(IMethodSymbol method)
            {
                if (lifetime is null || services is null || lifetime.Locations.Any(location => location.IsInSource)
                    || services.Locations.Any(location => location.IsInSource)
                    || lifetime.ContainingAssembly.Identity.Name != "Microsoft.Extensions.DependencyInjection.Abstractions"
                    || services.ContainingAssembly.Identity.Name != "Microsoft.Extensions.DependencyInjection.Abstractions"
                    || !method.IsExtensionMethod || method.TypeParameters.Length is < 1 or > 2
                    || method.Parameters.Length != 4 || !Equal(method.Parameters[0].Type, services)
                    || !Equal(method.ReturnType, services) || method.Parameters[1].Name != "optionsAction"
                    || method.Parameters[2].Name != "contextLifetime" || !Equal(method.Parameters[2].Type, lifetime)
                    || method.Parameters[3].Name != "optionsLifetime" || !Equal(method.Parameters[3].Type, lifetime)
                    || method.Parameters[1].Type is not INamedTypeSymbol callback)
                {
                    return false;
                }

                return (Equal(callback.OriginalDefinition, action) && callback.TypeArguments.Length == 1
                        && Equal(callback.TypeArguments[0], builder))
                    || (Equal(callback.OriginalDefinition, providerAction) && callback.TypeArguments.Length == 2
                        && Equal(callback.TypeArguments[0], provider) && Equal(callback.TypeArguments[1], builder));
            }

            IParameterSymbol? ConfigurationParameter(IOperation operation, ISymbol containingSymbol, CancellationToken cancellationToken)
            {
                IOperation root = operation;
                for (IOperation? ancestor = operation.Parent; ancestor is not null; ancestor = ancestor.Parent)
                {
                    root = ancestor;
                    if (ancestor is ILocalFunctionOperation)
                    {
                        return null;
                    }
                    if (ancestor is IAnonymousFunctionOperation lambda)
                    {
                        IOperation callback = lambda;
                        while (callback.Parent is IDelegateCreationOperation or IParenthesizedOperation or IConversionOperation { OperatorMethod: null })
                        {
                            callback = callback.Parent;
                        }
                        if (callback.Parent is not IArgumentOperation { Parameter: { Name: "optionsAction" } } argument
                            || argument.Parent is not IInvocationOperation registration
                            || !registrations.Any(method => Equal(method, (registration.TargetMethod.ReducedFrom
                                ?? registration.TargetMethod).OriginalDefinition))
                            || !RepeatedLifetime(registration, "contextLifetime")
                            || !RepeatedLifetime(registration, "optionsLifetime"))
                        {
                            return null;
                        }
                        IParameterSymbol? parameter = lambda.Symbol.Parameters.LastOrDefault(item => Equal(item.Type, builder));
                        return parameter is not null && !IsWritten(lambda, parameter, cancellationToken) ? parameter : null;
                    }
                }

                if (containingSymbol is not IMethodSymbol { Parameters.Length: 1 } enclosing)
                {
                    return null;
                }
                for (IMethodSymbol? method = enclosing.OverriddenMethod; method is not null; method = method.OverriddenMethod)
                {
                    if (onConfiguring.Any(candidate => Equal(candidate, method.OriginalDefinition)))
                    {
                        IParameterSymbol parameter = enclosing.Parameters[0];
                        return !IsWritten(root, parameter, cancellationToken) ? parameter : null;
                    }
                }
                return null;
            }

            bool IsWritten(IOperation root, IParameterSymbol parameter, CancellationToken cancellationToken) =>
                writtenParameters.GetOrAdd(parameter, _ =>
                {
                    // Writes or writable ref escapes invalidate builder identity without alias/dataflow analysis.
                    var pending = new Stack<IOperation>();
                    pending.Push(root);
                    while (pending.Count > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        IOperation current = pending.Pop();
                        if (current is IParameterReferenceOperation reference && Equal(reference.Parameter, parameter))
                        {
                            IOperation target = current;
                            while (target.Parent is ITupleOperation or IConversionOperation or IParenthesizedOperation
                                or IConditionalOperation { IsRef: true })
                            {
                                target = target.Parent;
                            }
                            if (target.Parent is IAssignmentOperation assignment && ReferenceEquals(assignment.Target, target)
                                || target.Parent is ISimpleAssignmentOperation { IsRef: true }
                                || target.Parent is IVariableInitializerOperation
                                    { Parent: IVariableDeclaratorOperation { Symbol.RefKind: RefKind.Ref } }
                                || target.Parent is IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out })
                            {
                                return true;
                            }
                        }
                        foreach (IOperation child in current.ChildOperations)
                        {
                            pending.Push(child);
                        }
                    }
                    return false;
                });

            bool RepeatedLifetime(IInvocationOperation registration, string name)
            {
                IArgumentOperation? argument = registration.Arguments.FirstOrDefault(item => item.Parameter?.Name == name);
                if (argument?.Value.ConstantValue is not { HasValue: true, Value: { } value })
                {
                    return false;
                }
                return lifetime!.GetMembers().OfType<IFieldSymbol>().Any(field => field.HasConstantValue
                    && field.Name is "Scoped" or "Transient" && Equals(field.ConstantValue, value));
            }

            bool IsConfiguredBuilder(IOperation? receiver, IParameterSymbol parameter)
            {
                if (receiver is null)
                {
                    return false;
                }
                receiver = Unwrap(receiver);
                if (receiver is IParameterReferenceOperation reference)
                {
                    return Equal(reference.Parameter, parameter);
                }
                if (receiver is not IInvocationOperation chain || !IsBuilder(chain.Type))
                {
                    return false;
                }
                IMethodSymbol method = chain.TargetMethod.ReducedFrom ?? chain.TargetMethod;
                if (method.Locations.Any(location => location.IsInSource))
                {
                    return false;
                }
                if (!method.IsStatic && IsBuilder(method.ContainingType))
                {
                    return IsConfiguredBuilder(chain.Instance, parameter);
                }
                // Only EF's referenced fluent extensions are assumed to preserve the builder.
                string assembly = method.ContainingAssembly.Identity.Name;
                return method.IsExtensionMethod && (assembly == "Microsoft.EntityFrameworkCore"
                        || assembly.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal))
                    && method.Parameters.Length > 0 && IsBuilder(method.Parameters[0].Type)
                    && IsConfiguredBuilder(chain.Arguments.FirstOrDefault(item => item.Parameter?.Ordinal == 0)?.Value, parameter);
            }
        });
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation { OperatorMethod: null } conversion:
                    operation = conversion.Operand;
                    break;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    break;
                default:
                    return operation;
            }
        }
    }

    private static bool Equal(ISymbol? left, ISymbol? right) => SymbolEqualityComparer.Default.Equals(left, right);
}
