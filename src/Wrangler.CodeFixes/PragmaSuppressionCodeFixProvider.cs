using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Wrangler.Analyzers;

namespace Wrangler.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PragmaSuppressionCodeFixProvider)), Shared]
public sealed class PragmaSuppressionCodeFixProvider : CodeFixProvider
{
    private const string Title = "Document suppressed rule titles";

    public override ImmutableArray<string> FixableDiagnosticIds => [PragmaSuppressionAnalyzer.TitleDiagnosticId];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The document has no syntax root.");
        Diagnostic diagnostic = context.Diagnostics[0];
        SourceText original = await context.Document.GetTextAsync(context.CancellationToken).ConfigureAwait(false);
        if (diagnostic.Location.SourceTree is not SyntaxTree diagnosticTree
            || !original.ContentEquals(diagnosticTree.GetText(context.CancellationToken)))
        {
            return;
        }

        PragmaWarningDirectiveTriviaSyntax? pragma = root
            .FindToken(diagnostic.Location.SourceSpan.Start, findInsideTrivia: true)
            .Parent?.AncestorsAndSelf().OfType<PragmaWarningDirectiveTriviaSyntax>().FirstOrDefault();
        if (pragma is null)
        {
            return;
        }

        PragmaSuppressionInfo? info = PragmaSuppressionInfo.Create(pragma, context.CancellationToken);
        AnalyzerConfigOptions options = context.Document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(
            pragma.SyntaxTree);
        if (info is null || !PragmaSuppressionAnalyzer.TryGetTitleValidation(options, out bool strict, out _))
        {
            return;
        }

        PragmaTitleCatalog catalog = PragmaTitleCatalog.Load(
            context.Document.Project.AnalyzerOptions.AdditionalFiles, context.CancellationToken);
        if (catalog.Error is not null || (strict && info.Ids.Any(id => !catalog.Titles.ContainsKey(id))))
        {
            return;
        }

        ImmutableDictionary<string, string> titles = FindAvailableTitles(
            context.Document.Project, info.Ids, catalog.Titles, context.CancellationToken);
        if (!info.TryCreateTitleChange(titles, out TextChange change))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                Title,
                cancellationToken =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return Task.FromResult(context.Document.WithText(original.WithChanges(change)));
                },
                equivalenceKey: Title),
            diagnostic);
    }

    private static ImmutableDictionary<string, string> FindAvailableTitles(
        Project project,
        ImmutableArray<string> ids,
        ImmutableDictionary<string, string> catalog,
        CancellationToken cancellationToken)
    {
        var needed = new HashSet<string>(ids.Where(id => !catalog.ContainsKey(id)), StringComparer.Ordinal);
        if (needed.Count == 0)
        {
            return catalog;
        }

        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        var conflicts = new HashSet<string>(StringComparer.Ordinal);
        foreach (AnalyzerReference reference in project.AnalyzerReferences)
        {
            foreach (DiagnosticAnalyzer analyzer in reference.GetAnalyzers(project.Language))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ImmutableArray<DiagnosticDescriptor> descriptors = analyzer.SupportedDiagnostics;
                if (descriptors.IsDefaultOrEmpty)
                {
                    continue;
                }

                foreach (DiagnosticDescriptor descriptor in descriptors)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!needed.Contains(descriptor.Id))
                    {
                        continue;
                    }

                    string title = descriptor.Title.ToString(CultureInfo.InvariantCulture);
                    if (found.TryGetValue(descriptor.Id, out string? previous)
                        && !previous.Equals(title, StringComparison.Ordinal))
                    {
                        conflicts.Add(descriptor.Id);
                    }

                    found[descriptor.Id] = title;
                }
            }
        }

        return catalog.AddRange(found.Where(entry => !conflicts.Contains(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value)));
    }
}
