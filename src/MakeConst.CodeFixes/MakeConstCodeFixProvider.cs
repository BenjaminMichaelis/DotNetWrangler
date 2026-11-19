using System.Collections.Immutable;
using System.Composition;
using MakeConst.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;

namespace MakeConst.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MakeConstCodeFixProvider)), Shared]
public sealed class MakeConstCodeFixProvider : CodeFixProvider
{
    private const string Title = "Make type name uppercase";

    public override ImmutableArray<string> FixableDiagnosticIds => [MakeConstAnalyzer.DiagnosticId];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The document has no syntax root.");

        Diagnostic diagnostic = context.Diagnostics[0];
        SyntaxNode node = root.FindNode(diagnostic.Location.SourceSpan);
        TypeDeclarationSyntax? declaration = node.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();

        if (declaration is null)
        {
            return;
        }

        SemanticModel semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The document has no semantic model.");
        INamedTypeSymbol? symbol = semanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
        if (symbol is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedSolution: cancellationToken => RenameTypeAsync(
                    context.Document,
                    symbol,
                    cancellationToken),
                equivalenceKey: Title),
            diagnostic);
    }

    private static Task<Solution> RenameTypeAsync(
        Document document,
        INamedTypeSymbol typeSymbol,
        CancellationToken cancellationToken)
    {
        return Renamer.RenameSymbolAsync(
            document.Project.Solution,
            typeSymbol,
            typeSymbol.Name.ToUpperInvariant(),
            document.Project.Solution.Workspace.Options,
            cancellationToken);
    }
}
