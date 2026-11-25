using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Wrangler.Analyzers;

internal sealed class PragmaTitleCatalog(
    ImmutableDictionary<string, string> titles,
    string? error)
{
    private const string WranglerDiagnosticIdPrefix = "WRANGLER";

    internal const string FileName = "Wrangler.PragmaTitles.txt";

    internal ImmutableDictionary<string, string> Titles { get; } = titles;
    internal string? Error { get; } = error;

    internal static PragmaTitleCatalog Load(
        ImmutableArray<AdditionalText> additionalFiles,
        CancellationToken cancellationToken)
    {
        ImmutableDictionary<string, string>.Builder titles = ImmutableDictionary.CreateBuilder<string, string>(
            StringComparer.Ordinal);
        IEnumerable<DiagnosticDescriptor> descriptors = new NullForgivingOperatorAnalyzer().SupportedDiagnostics
            .Concat(new PragmaSuppressionAnalyzer().SupportedDiagnostics)
            .Concat(new SaveChangesInterceptorAnalyzer().SupportedDiagnostics);
        foreach (DiagnosticDescriptor descriptor in descriptors)
        {
            titles.Add(descriptor.Id, descriptor.Title.ToString(CultureInfo.InvariantCulture));
        }

        foreach (AdditionalText file in additionalFiles
            .Where(file => Path.GetFileName(file.Path).Equals(FileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Microsoft.CodeAnalysis.Text.SourceText? text = file.GetText(cancellationToken);
            if (text is null)
            {
                return Invalid($"Cannot read '{file.Path}'");
            }

            for (int lineNumber = 0; lineNumber < text.Lines.Count; lineNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string line = text.Lines[lineNumber].ToString().Trim().TrimStart('\uFEFF').TrimStart();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator < 0)
                {
                    return InvalidAtLine("Expected 'ID = Title'");
                }

                string id = line.Substring(0, separator).Trim();
                string title = line.Substring(separator + 1).Trim();
                if (!SyntaxFacts.IsValidIdentifier(id) || id.StartsWith("@", StringComparison.Ordinal))
                {
                    return InvalidAtLine($"'{id}' is not a diagnostic ID; write numeric warnings as CS0168");
                }

                if (title.Length == 0)
                {
                    return InvalidAtLine($"The title for '{id}' is empty");
                }

                if (id.Length == WranglerDiagnosticIdPrefix.Length + 4
                    && id.StartsWith(WranglerDiagnosticIdPrefix, StringComparison.Ordinal)
                    && id.Skip(WranglerDiagnosticIdPrefix.Length).All(char.IsDigit))
                {
                    return InvalidAtLine($"'{id}' is reserved for Wrangler's own descriptors");
                }

                if (titles.ContainsKey(id))
                {
                    return InvalidAtLine($"Duplicate title entry for '{id}'");
                }

                if (PragmaSuppressionInfo.FindJustification(title, out _, out _) >= 0)
                {
                    return InvalidAtLine($"The title for '{id}' contains the reserved 'Justification:' field");
                }

                titles.Add(id, title);

                PragmaTitleCatalog InvalidAtLine(string message) =>
                    Invalid($"{file.Path}({lineNumber + 1}): {message}");
            }
        }

        return new PragmaTitleCatalog(titles.ToImmutable(), null);
    }

    private static PragmaTitleCatalog Invalid(string error) =>
        new(ImmutableDictionary<string, string>.Empty, $"Invalid {FileName}: {error}");
}
