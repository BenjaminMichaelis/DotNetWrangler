using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Wrangler.Analyzers;

internal sealed class PragmaSuppressionInfo
{
    private readonly PragmaWarningDirectiveTriviaSyntax pragma;
    private readonly SourceText text;
    private readonly ImmutableDictionary<string, string> labels;
    private readonly bool hasComment;
    private readonly TextSpan bodySpan;
    private readonly int codeEnd;
    private readonly string body;
    private readonly string? justificationField;
    private readonly string? justification;
    private readonly ImmutableArray<TitleEntry> entries;
    private readonly string? formatError;

    private PragmaSuppressionInfo(
        PragmaWarningDirectiveTriviaSyntax pragma,
        SourceText text,
        ImmutableArray<string> ids,
        ImmutableDictionary<string, string> labels,
        int codeEnd,
        bool hasComment,
        TextSpan bodySpan)
    {
        this.pragma = pragma;
        this.text = text;
        Ids = ids;
        this.labels = labels;
        this.codeEnd = codeEnd;
        this.hasComment = hasComment;
        this.bodySpan = bodySpan;
        body = text.ToString(bodySpan);
        entries = ParseBody(body, ids, labels, out justificationField, out justification, out formatError);
    }

    internal ImmutableArray<string> Ids { get; }
    internal bool HasJustification => !string.IsNullOrWhiteSpace(justification);

    internal static PragmaSuppressionInfo? Create(
        PragmaWarningDirectiveTriviaSyntax pragma,
        CancellationToken cancellationToken)
    {
        if (!pragma.IsActive
            || !pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword)
            || pragma.DisableOrRestoreKeyword.IsMissing
            || pragma.ContainsDiagnostics
            || pragma.ContainsSkippedText
            || (pragma.ErrorCodes.Count > 0 && pragma.ErrorCodes.SeparatorCount == pragma.ErrorCodes.Count))
        {
            return null;
        }

        ImmutableArray<string>.Builder ids = ImmutableArray.CreateBuilder<string>();
        ImmutableDictionary<string, string>.Builder labels = ImmutableDictionary.CreateBuilder<string, string>(
            StringComparer.Ordinal);
        foreach (ExpressionSyntax expression in pragma.ErrorCodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string id;
            if (expression is IdentifierNameSyntax identifier && !identifier.Identifier.IsMissing)
            {
                id = identifier.Identifier.ValueText;
            }
            else if (expression is LiteralExpressionSyntax literal && literal.Token.Value is int numeric && numeric >= 0)
            {
                id = "CS" + numeric.ToString("D4", CultureInfo.InvariantCulture);
            }
            else
            {
                return null;
            }

            if (!ids.Contains(id, StringComparer.Ordinal))
            {
                ids.Add(id);
            }

            labels[id] = id;
            labels[expression.ToString()] = id;
        }

        SourceText text = pragma.SyntaxTree.GetText(cancellationToken);
        int codeEnd = pragma.ErrorCodes.Count == 0
            ? pragma.DisableOrRestoreKeyword.Span.End
            : pragma.ErrorCodes.Last().Span.End;
        TextLine line = text.Lines.GetLineFromPosition(codeEnd);
        int commentStart = codeEnd;
        while (commentStart < line.End && char.IsWhiteSpace(text[commentStart]))
        {
            commentStart++;
        }

        bool hasComment = commentStart + 1 < line.End
            && text[commentStart] == '/'
            && text[commentStart + 1] == '/'
            && pragma.SyntaxTree.GetRoot(cancellationToken)
                .FindTrivia(commentStart, findInsideTrivia: true).IsKind(SyntaxKind.SingleLineCommentTrivia);
        int bodyStart = hasComment ? commentStart + 2 : codeEnd;
        int bodyEnd = hasComment ? line.End : codeEnd;
        while (bodyStart < bodyEnd && char.IsWhiteSpace(text[bodyStart]))
        {
            bodyStart++;
        }

        while (bodyEnd > bodyStart && char.IsWhiteSpace(text[bodyEnd - 1]))
        {
            bodyEnd--;
        }

        return new PragmaSuppressionInfo(
            pragma, text, ids.ToImmutable(), labels.ToImmutable(), codeEnd, hasComment,
            TextSpan.FromBounds(bodyStart, bodyEnd));
    }

    internal bool HasPrecedingJustification(CancellationToken cancellationToken)
    {
        TextLine line = text.Lines.GetLineFromPosition(pragma.HashToken.SpanStart);
        if (line.LineNumber == 0)
        {
            return false;
        }

        TextLine previous = text.Lines[line.LineNumber - 1];
        int start = previous.Start;
        while (start < previous.End && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        if (start == previous.End)
        {
            return false;
        }

        SyntaxTrivia trivia = pragma.SyntaxTree.GetRoot(cancellationToken).FindTrivia(start, findInsideTrivia: true);
        if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.SpanStart != start)
        {
            return false;
        }

        string comment = trivia.ToString().Substring(2).Trim();
        return TryReadField(comment, 0, "Justification", StringComparison.OrdinalIgnoreCase, out int end)
            && !string.IsNullOrWhiteSpace(comment.Substring(end));
    }

    internal string? GetTitleProblem(IReadOnlyDictionary<string, string> titles, bool strict)
    {
        if (Ids.Length == 0)
        {
            return "Specify the suppressed diagnostic IDs and their titles instead of disabling every warning";
        }

        return formatError ?? GetEntryProblem(entries, Ids, titles, strict);
    }

    internal bool TryCreateTitleChange(
        IReadOnlyDictionary<string, string> titles,
        out TextChange change)
    {
        change = default;
        if (Ids.Length == 0 || formatError is not null || Ids.Any(id =>
            !titles.TryGetValue(id, out string? title)
            || string.IsNullOrWhiteSpace(title)
            || SourceText.From(title).Lines.Count != 1))
        {
            return false;
        }

        foreach (TitleEntry entry in entries)
        {
            if (!entry.Title.Equals(titles[entry.Id], StringComparison.Ordinal)
                && (entry.Title.IndexOf(';') >= 0 || (!entry.IsLabeled && entry.Title.Length > 0)))
            {
                return false;
            }
        }

        if (Ids.Length > 1 && justification is not null && ContainsLabel(justification, labels))
        {
            return false;
        }

        string replacement = Ids.Length == 1
            ? titles[Ids[0]]
            : string.Join("; ", Ids.Select(id => $"{id}: {titles[id]}"));
        if (justificationField is not null)
        {
            replacement += "; " + justificationField;
        }

        ImmutableArray<TitleEntry> proposed = ParseBody(
            replacement, Ids, labels, out _, out string? proposedReason, out string? proposedError);
        if (proposedError is not null
            || GetEntryProblem(proposed, Ids, titles, strict: true) is not null
            || !string.Equals(justification, proposedReason, StringComparison.Ordinal))
        {
            return false;
        }

        if (hasComment)
        {
            if (bodySpan.Length == 0 && bodySpan.Start >= 2 && text[bodySpan.Start - 1] == '/')
            {
                replacement = " " + replacement;
            }

            change = new TextChange(bodySpan, replacement);
        }
        else
        {
            TextLine line = text.Lines.GetLineFromPosition(codeEnd);
            for (int position = codeEnd; position < line.End; position++)
            {
                if (!char.IsWhiteSpace(text[position]))
                {
                    return false;
                }
            }

            change = new TextChange(new TextSpan(codeEnd, 0), " // " + replacement);
        }

        return true;
    }

    internal static int FindJustification(string value, out int fieldStart, out int reasonStart)
    {
        for (int position = 0; position < value.Length; position++)
        {
            if (position != 0 && value[position - 1] != ';')
            {
                continue;
            }

            int start = position;
            while (start < value.Length && char.IsWhiteSpace(value[start]))
            {
                start++;
            }

            if (TryReadField(value, start, "Justification", StringComparison.OrdinalIgnoreCase, out reasonStart))
            {
                fieldStart = start;
                return position == 0 ? 0 : position - 1;
            }
        }

        fieldStart = reasonStart = -1;
        return -1;
    }

    private static ImmutableArray<TitleEntry> ParseBody(
        string body,
        ImmutableArray<string> ids,
        ImmutableDictionary<string, string> labels,
        out string? justificationField,
        out string? justification,
        out string? error)
    {
        int reasonDelimiter = FindJustification(body, out int fieldStart, out int reasonStart);
        justificationField = reasonDelimiter < 0 ? null : body.Substring(fieldStart);
        justification = reasonDelimiter < 0 ? null : body.Substring(reasonStart);
        string titleText = (reasonDelimiter < 0 ? body : body.Substring(0, reasonDelimiter)).Trim();
        error = null;
        if (ids.Length == 0 || titleText.Length == 0)
        {
            return [];
        }

        if (ids.Length == 1)
        {
            bool labeled = TryReadLabel(titleText, 0, labels, out _, out int titleStart);
            return [new TitleEntry(ids[0], labeled ? titleText.Substring(titleStart).Trim() : titleText, labeled)];
        }

        ImmutableArray<TitleEntry>.Builder entries = ImmutableArray.CreateBuilder<TitleEntry>();
        int position = 0;
        while (position < titleText.Length)
        {
            if (!TryReadLabel(titleText, position, labels, out string? id, out int titleStart) || id is null)
            {
                error = "Use a labeled 'ID: Title' comment entry for each suppressed diagnostic when listing multiple IDs";
                return [];
            }

            if (entries.Any(entry => entry.Id == id))
            {
                error = $"The title for '{id}' is listed more than once";
                return [];
            }

            int next = titleText.Length;
            int nextStart = titleText.Length;
            for (int index = titleStart; index < titleText.Length; index++)
            {
                if (titleText[index] == ';' && TryReadLabel(titleText, index + 1, labels, out _, out _))
                {
                    next = index;
                    nextStart = index + 1;
                    break;
                }
            }

            entries.Add(new TitleEntry(id, titleText.Substring(titleStart, next - titleStart).Trim(), true));
            position = nextStart;
        }

        return entries.ToImmutable();
    }

    private static string? GetEntryProblem(
        ImmutableArray<TitleEntry> entries,
        ImmutableArray<string> ids,
        IReadOnlyDictionary<string, string> titles,
        bool strict)
    {
        foreach (string id in ids)
        {
            TitleEntry? entry = entries.FirstOrDefault(entry => entry.Id == id);
            if (!titles.TryGetValue(id, out string? expected))
            {
                if (strict)
                {
                    return $"No title catalog entry exists for '{id}'; add 'ID = Title' to {PragmaTitleCatalog.FileName} or use documented mode";
                }

                if (entry is null || entry.Title.Length == 0)
                {
                    return $"Document a nonempty rule title for '{id}' in the pragma's same-line comment";
                }
            }
            else if (entry is null || !entry.Title.Equals(expected, StringComparison.Ordinal))
            {
                return $"Use the rule title '{expected}' for '{id}' in the pragma's same-line comment";
            }
        }

        return null;
    }

    private static bool ContainsLabel(string value, ImmutableDictionary<string, string> labels)
    {
        for (int index = 0; index < value.Length; index++)
        {
            if ((index == 0 || value[index - 1] == ';') && TryReadLabel(value, index, labels, out _, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadLabel(
        string value,
        int start,
        ImmutableDictionary<string, string> labels,
        out string? id,
        out int titleStart)
    {
        int colon = start;
        while (colon < value.Length && value[colon] != ':' && value[colon] != ';')
        {
            colon++;
        }

        titleStart = colon + 1;
        id = null;
        return colon < value.Length && value[colon] == ':'
            && labels.TryGetValue(value.Substring(start, colon - start).Trim(), out id);
    }

    private static bool TryReadField(
        string value,
        int start,
        string name,
        StringComparison comparison,
        out int end)
    {
        end = start + name.Length;
        if (end > value.Length || string.Compare(value, start, name, 0, name.Length, comparison) != 0)
        {
            return false;
        }

        while (end < value.Length && char.IsWhiteSpace(value[end]))
        {
            end++;
        }

        return end < value.Length && value[end++] == ':';
    }

    private sealed class TitleEntry(string id, string title, bool isLabeled)
    {
        internal string Id { get; } = id;
        internal string Title { get; } = title;
        internal bool IsLabeled { get; } = isLabeled;
    }
}
