using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Wrangler.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SaveChangesInterceptorAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "WRANGLER0006";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Implement both synchronous and asynchronous SaveChanges interceptor callbacks",
        "Callback '{0}' is implemented without its counterpart '{1}'",
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: false,
        helpLinkUri: HelpLinks.ForRule(DiagnosticId));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(startContext =>
        {
            Compilation compilation = startContext.Compilation;
            INamedTypeSymbol? contract = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor");
            if (contract is null || contract.TypeKind != TypeKind.Interface || contract.Locations.Any(location => location.IsInSource)
                || contract.ContainingAssembly.Identity.Name != "Microsoft.EntityFrameworkCore")
            {
                return;
            }

            INamedTypeSymbol? defaults = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor");
            if (defaults is not null && !SymbolEqualityComparer.Default.Equals(defaults.ContainingAssembly, contract.ContainingAssembly))
            {
                defaults = null;
            }

            var pairs = ImmutableArray.CreateBuilder<(IMethodSymbol Sync, IMethodSymbol Async)>();
            AddPair("SavingChanges", "DbContextEventData", "InterceptionResult`1");
            AddPair("SavedChanges", "SaveChangesCompletedEventData", "System.Int32");
            AddPair("SaveChangesFailed", "DbContextErrorEventData", "System.Void");
            AddPair("SaveChangesCanceled", "DbContextEventData", "System.Void");
            AddPair("ThrowingConcurrencyException", "ConcurrencyExceptionEventData", "InterceptionResult");
            ImmutableArray<(IMethodSymbol Sync, IMethodSymbol Async)> callbacks = pairs.ToImmutable();
            if (callbacks.IsEmpty)
            {
                return;
            }

            startContext.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                if (type.TypeKind != TypeKind.Class
                    || !type.AllInterfaces.Any(item => SymbolEqualityComparer.Default.Equals(item, contract)))
                {
                    return;
                }

                foreach ((IMethodSymbol sync, IMethodSymbol async) in callbacks)
                {
                    symbolContext.CancellationToken.ThrowIfCancellationRequested();
                    IMethodSymbol? syncImplementation = FindImplementation(type, sync, symbolContext.CancellationToken);
                    IMethodSymbol? asyncImplementation = FindImplementation(type, async, symbolContext.CancellationToken);
                    bool hasSync = IsCustom(syncImplementation);
                    bool hasAsync = IsCustom(asyncImplementation);
                    if (hasSync == hasAsync)
                    {
                        continue;
                    }

                    IMethodSymbol implementation = (hasSync ? syncImplementation : asyncImplementation)!;
                    implementation = implementation.PartialImplementationPart ?? implementation;
                    // Diagnose the declaration introducing the imbalance, not every inheriting class.
                    if (!SymbolEqualityComparer.Default.Equals(implementation.ContainingType, type))
                    {
                        continue;
                    }

                    foreach (SyntaxReference reference in implementation.DeclaringSyntaxReferences)
                    {
                        if (reference.GetSyntax(symbolContext.CancellationToken) is MethodDeclarationSyntax declaration
                            && !declaration.ContainsDiagnostics
                            && (declaration.Body is not null || declaration.ExpressionBody is not null))
                        {
                            symbolContext.ReportDiagnostic(Diagnostic.Create(
                                Rule, declaration.Identifier.GetLocation(),
                                hasSync ? sync.Name : async.Name, hasSync ? async.Name : sync.Name));
                            break;
                        }
                    }
                }

                bool IsCustom(IMethodSymbol? method) => method is not null && !method.IsAbstract
                    && method.ContainingType.TypeKind != TypeKind.Interface
                    && !SymbolEqualityComparer.Default.Equals(method.ContainingType, defaults);
            }, SymbolKind.NamedType);

            void AddPair(string name, string eventName, string resultName)
            {
                ITypeSymbol? eventType = EfType(eventName);
                ITypeSymbol? resultType = resultName.StartsWith("System.", StringComparison.Ordinal)
                    ? compilation.GetTypeByMetadataName(resultName) : EfType(resultName);
                if (resultType is INamedTypeSymbol { Arity: 1 } generic)
                {
                    resultType = generic.Construct(compilation.GetSpecialType(SpecialType.System_Int32));
                }

                INamedTypeSymbol? cancellation = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
                INamedTypeSymbol? task = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
                INamedTypeSymbol? valueTask = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
                if (eventType is null || resultType is null || cancellation is null || task is null || valueTask is null)
                {
                    return;
                }

                bool returnsVoid = resultType.SpecialType == SpecialType.System_Void;
                ITypeSymbol asyncResult = returnsVoid ? task : valueTask.Construct(resultType);
                ITypeSymbol[] syncParameters = returnsVoid ? [eventType] : [eventType, resultType];
                ITypeSymbol[] asyncParameters = [.. syncParameters, cancellation];
                IMethodSymbol? sync = Match(name, resultType, syncParameters);
                IMethodSymbol? async = Match(name + "Async", asyncResult, asyncParameters);
                if (sync is not null && async is not null)
                {
                    pairs.Add((sync, async));
                }

                IMethodSymbol? Match(string methodName, ITypeSymbol returnType, ITypeSymbol[] parameters)
                {
                    IMethodSymbol[] matches = contract.GetMembers(methodName).OfType<IMethodSymbol>()
                        .Where(method => !method.IsStatic && method.Arity == 0
                            && SymbolEqualityComparer.Default.Equals(method.ReturnType, returnType)
                            && method.Parameters.Length == parameters.Length
                            && method.Parameters.Select((parameter, index) => parameter.RefKind == RefKind.None
                                && SymbolEqualityComparer.Default.Equals(parameter.Type, parameters[index])).All(value => value))
                        .ToArray();
                    return matches.Length == 1 ? matches[0] : null;
                }
            }

            INamedTypeSymbol? EfType(string name)
            {
                INamedTypeSymbol? type = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Diagnostics." + name);
                return type is not null && SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, contract.ContainingAssembly)
                    ? type : null;
            }
        });
    }

    private static IMethodSymbol? FindImplementation(INamedTypeSymbol type, IMethodSymbol callback, CancellationToken cancellationToken)
    {
        if (type.FindImplementationForInterfaceMember(callback) is not IMethodSymbol implementation)
        {
            return null;
        }

        // Interface mapping can name a base virtual slot; use the effective override.
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (IMethodSymbol method in current.GetMembers().OfType<IMethodSymbol>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (IMethodSymbol? overridden = method; overridden is not null; overridden = overridden.OverriddenMethod)
                {
                    if (SymbolEqualityComparer.Default.Equals(overridden, implementation))
                    {
                        return method;
                    }
                }
            }
        }

        return implementation;
    }
}
