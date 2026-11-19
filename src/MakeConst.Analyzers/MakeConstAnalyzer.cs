using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MakeConst.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MakeConstAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "MAKECONST001";
    private const string Category = "Naming";

    private static readonly LocalizableString Title = "Type name contains lowercase letters";
    private static readonly LocalizableString MessageFormat = "Type name '{0}' contains lowercase letters";
    private static readonly LocalizableString Description = "Type names should not contain lowercase letters.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.NamedType);
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context)
    {
        var typeSymbol = (INamedTypeSymbol)context.Symbol;
        if (!typeSymbol.Name.Any(char.IsLower))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, typeSymbol.Locations[0], typeSymbol.Name));
    }
}
