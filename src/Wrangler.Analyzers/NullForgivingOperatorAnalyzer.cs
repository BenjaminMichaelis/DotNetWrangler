using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Wrangler.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NullForgivingOperatorAnalyzer : DiagnosticAnalyzer
{
    public const string NullForgivingOperatorDiagnosticId = "WR0002";
    public const string FabricatedNonNullValueDiagnosticId = "WR0003";
    private const string Category = "Reliability";

    internal static readonly DiagnosticDescriptor NullForgivingOperatorRule = new(
        NullForgivingOperatorDiagnosticId,
        "Null-forgiving operator hides a possible null value",
        "The null-forgiving operator on '{0}' overrides the compiler's nullable analysis",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Only use '!' when you can prove the value is non-null and the compiler cannot. "
            + "Prefer handling null explicitly (a null check, '?.', or a throw for contract violations), "
            + "expressing the guarantee with nullable attributes such as [NotNullWhen] or [MemberNotNull], "
            + "or asserting non-null in tests. If '!' is genuinely required, suppress this diagnostic "
            + "with a justification explaining why the value cannot be null.");

    internal static readonly DiagnosticDescriptor FabricatedNonNullValueRule = new(
        FabricatedNonNullValueDiagnosticId,
        "Null-forgiving operator fabricates a non-null value",
        "'{0}' assigns null to a location declared as non-null",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "'null!' and 'default!' make the compiler believe a null value is non-null, moving the "
            + "NullReferenceException to a later, harder-to-diagnose location. Declare the member as nullable, "
            + "use 'required' members, or initialize it with a real value in the constructor. Do not replace it "
            + "with an arbitrary default (such as an empty first name) that silently hides missing data.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [NullForgivingOperatorRule, FabricatedNonNullValueRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.SuppressNullableWarningExpression);
    }

    private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        var suppression = (PostfixUnaryExpressionSyntax)context.Node;
        ExpressionSyntax operand = suppression.Operand;
        while (operand is ParenthesizedExpressionSyntax parenthesized)
        {
            operand = parenthesized.Expression;
        }

        DiagnosticDescriptor rule = operand.Kind() is SyntaxKind.NullLiteralExpression
            or SyntaxKind.DefaultLiteralExpression
            or SyntaxKind.DefaultExpression
            ? FabricatedNonNullValueRule
            : NullForgivingOperatorRule;

        context.ReportDiagnostic(Diagnostic.Create(rule, suppression.GetLocation(), suppression.Operand.ToString()));
    }
}
