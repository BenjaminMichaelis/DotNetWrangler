using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Wrangler.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MakeConstAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "WR0001";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "Variable can be made constant";
    private static readonly LocalizableString MessageFormat = "Variable '{0}' can be made constant";
    private static readonly LocalizableString Description = "Variables that aren't modified should be made constants.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: HelpLinks.ForRule(DiagnosticId));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.LocalDeclarationStatement);
    }

    private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        var localDeclaration = (LocalDeclarationStatementSyntax)context.Node;
        if (localDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword))
        {
            return;
        }

        TypeSyntax variableTypeName = localDeclaration.Declaration.Type;
        ITypeSymbol? variableType = context.SemanticModel
            .GetTypeInfo(variableTypeName, context.CancellationToken)
            .ConvertedType;
        if (variableType is null)
        {
            return;
        }

        foreach (VariableDeclaratorSyntax variable in localDeclaration.Declaration.Variables)
        {
            EqualsValueClauseSyntax? initializer = variable.Initializer;
            if (initializer is null)
            {
                return;
            }

            Optional<object?> constantValue = context.SemanticModel
                .GetConstantValue(initializer.Value, context.CancellationToken);
            if (!constantValue.HasValue)
            {
                return;
            }

            Conversion conversion = context.SemanticModel.ClassifyConversion(initializer.Value, variableType);
            if (!conversion.Exists || conversion.IsUserDefined)
            {
                return;
            }

            if (constantValue.Value is string)
            {
                if (variableType.SpecialType != SpecialType.System_String)
                {
                    return;
                }
            }
            else if (variableType.IsReferenceType && constantValue.Value is not null)
            {
                return;
            }
        }

        DataFlowAnalysis? dataFlowAnalysis = context.SemanticModel.AnalyzeDataFlow(localDeclaration);
        if (dataFlowAnalysis is null || !dataFlowAnalysis.Succeeded)
        {
            return;
        }

        foreach (VariableDeclaratorSyntax variable in localDeclaration.Declaration.Variables)
        {
            ISymbol? variableSymbol = context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken);
            if (variableSymbol is null || dataFlowAnalysis.WrittenOutside.Contains(variableSymbol))
            {
                return;
            }
        }

        string variableName = localDeclaration.Declaration.Variables[0].Identifier.ValueText;
        context.ReportDiagnostic(Diagnostic.Create(Rule, localDeclaration.GetLocation(), variableName));
    }
}
