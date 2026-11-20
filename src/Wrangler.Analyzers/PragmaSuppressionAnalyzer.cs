using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Wrangler.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PragmaSuppressionAnalyzer : DiagnosticAnalyzer
{
    public const string TitleDiagnosticId = "WRANGLER0005";
    public const string JustificationDiagnosticId = "WRANGLER0006";
    internal const string TitleValidationOption = "wrangler_pragma_title_validation";

    internal static readonly DiagnosticDescriptor TitleRule = new(
        TitleDiagnosticId,
        "Document suppressed diagnostic IDs and titles",
        "{0}",
        "Maintainability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: false,
        description: "Specify warning suppression IDs and document each rule's title in a same-line comment.",
        helpLinkUri: HelpLinks.ForRule(TitleDiagnosticId));

    internal static readonly DiagnosticDescriptor JustificationRule = new(
        JustificationDiagnosticId,
        "Justify warning suppressions",
        "Add 'Justification: <reason>' to the pragma comment or the immediately preceding standalone // comment",
        "Maintainability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: false,
        description: "Explain warning suppressions with an explicit, nonempty justification.",
        helpLinkUri: HelpLinks.ForRule(JustificationDiagnosticId));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [TitleRule, JustificationRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(compilationContext =>
        {
            PragmaTitleCatalog catalog = PragmaTitleCatalog.Load(
                compilationContext.Options.AdditionalFiles,
                compilationContext.CancellationToken);
            compilationContext.RegisterSyntaxNodeAction(
                nodeContext => AnalyzePragma(nodeContext, catalog),
                SyntaxKind.PragmaWarningDirectiveTrivia);
        });
    }

    internal static bool TryGetTitleValidation(
        AnalyzerConfigOptions options,
        out bool strict,
        out string? error)
    {
        strict = false;
        error = null;
        if (!options.TryGetValue(TitleValidationOption, out string? value))
        {
            return true;
        }

        value = value.Trim();
        if (value.Equals("documented", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("strict", StringComparison.OrdinalIgnoreCase))
        {
            strict = true;
            return true;
        }

        error = $"Invalid {TitleValidationOption} value '{value}'; use 'documented' or 'strict'";
        return false;
    }

    private static void AnalyzePragma(SyntaxNodeAnalysisContext context, PragmaTitleCatalog catalog)
    {
        var pragma = (PragmaWarningDirectiveTriviaSyntax)context.Node;
        PragmaSuppressionInfo? info = PragmaSuppressionInfo.Create(pragma, context.CancellationToken);
        if (info is null)
        {
            return;
        }

        // The end token already uses this directive's new suppression state.
        Location location = pragma.DisableOrRestoreKeyword.GetLocation();
        if (!info.HasJustification && !info.HasPrecedingJustification(context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(JustificationRule, location));
        }

        AnalyzerConfigOptions options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(pragma.SyntaxTree);
        if (!TryGetTitleValidation(options, out bool strict, out string? error))
        {
            context.ReportDiagnostic(Diagnostic.Create(TitleRule, location, error));
            return;
        }

        string? problem = catalog.Error ?? info.GetTitleProblem(catalog.Titles, strict);
        if (problem is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(TitleRule, location, problem));
        }
    }
}
