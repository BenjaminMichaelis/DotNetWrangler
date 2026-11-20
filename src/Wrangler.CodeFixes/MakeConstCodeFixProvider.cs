using System.Collections.Immutable;
using System.Composition;
using Wrangler.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace Wrangler.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MakeConstCodeFixProvider)), Shared]
public sealed class MakeConstCodeFixProvider : CodeFixProvider
{
    private const string Title = "Make constant";

    public override ImmutableArray<string> FixableDiagnosticIds => [MakeConstAnalyzer.DiagnosticId];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The document has no syntax root.");

        Diagnostic diagnostic = context.Diagnostics[0];
        LocalDeclarationStatementSyntax? declaration = root
            .FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<LocalDeclarationStatementSyntax>()
            .FirstOrDefault();

        if (declaration is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: cancellationToken => MakeConstAsync(
                    context.Document,
                    declaration,
                    cancellationToken),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> MakeConstAsync(
        Document document,
        LocalDeclarationStatementSyntax localDeclaration,
        CancellationToken cancellationToken)
    {
        SyntaxToken firstToken = localDeclaration.GetFirstToken();
        SyntaxTriviaList leadingTrivia = firstToken.LeadingTrivia;
        LocalDeclarationStatementSyntax trimmedLocal = localDeclaration.ReplaceToken(
            firstToken,
            firstToken.WithLeadingTrivia(SyntaxTriviaList.Empty));

        SyntaxToken constToken = SyntaxFactory.Token(
            leadingTrivia,
            SyntaxKind.ConstKeyword,
            SyntaxFactory.TriviaList(SyntaxFactory.ElasticMarker));
        SyntaxTokenList newModifiers = trimmedLocal.Modifiers.Insert(0, constToken);
        VariableDeclarationSyntax variableDeclaration = localDeclaration.Declaration;
        TypeSyntax variableTypeName = variableDeclaration.Type;

        if (variableTypeName.IsVar)
        {
            SemanticModel semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The document has no semantic model.");

            IAliasSymbol? aliasInfo = semanticModel.GetAliasInfo(variableTypeName, cancellationToken);
            if (aliasInfo is null)
            {
                ITypeSymbol? type = semanticModel.GetTypeInfo(variableTypeName, cancellationToken).ConvertedType;
                if (type is not null && type.Name != "var")
                {
                    TypeSyntax typeName = SyntaxFactory.ParseTypeName(
                            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                        .WithLeadingTrivia(variableTypeName.GetLeadingTrivia())
                        .WithTrailingTrivia(variableTypeName.GetTrailingTrivia());
                    variableDeclaration = variableDeclaration.WithType(
                        typeName.WithAdditionalAnnotations(Simplifier.Annotation));
                }
            }
        }

        LocalDeclarationStatementSyntax formattedLocal = trimmedLocal
            .WithModifiers(newModifiers)
            .WithDeclaration(variableDeclaration)
            .WithAdditionalAnnotations(Formatter.Annotation);

        SyntaxNode oldRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The document has no syntax root.");
        SyntaxNode newRoot = oldRoot.ReplaceNode(localDeclaration, formattedLocal);
        return document.WithSyntaxRoot(newRoot);
    }
}
