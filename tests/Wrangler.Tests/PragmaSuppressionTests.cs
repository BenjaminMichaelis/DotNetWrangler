using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using Wrangler.Analyzers;
using Wrangler.CodeFixes;

namespace Wrangler.Tests;

[TestClass]
public sealed class PragmaSuppressionTests
{
    private const string Title2 = "Null-forgiving operator hides a possible null value";
    private const string Title3 = "Null-forgiving operator fabricates a non-null value";
    private const string Reason = "; Justification: Tests null rejection.";

    [TestMethod]
    [DataRow("// Justification: Tests null rejection.\n#pragma warning disable WRANGLER0002 // " + Title3)]
    [DataRow("#pragma warning disable WRANGLER0002 // " + Title3 + Reason)]
    [DataRow("#pragma warning disable WRANGLER0002 // WRANGLER0002: " + Title3 + Reason)]
    [DataRow("#pragma warning disable WRANGLER0002 // " + Title3 + "; justification \t: Tests null rejection; not defaults #1.")]
    [DataRow("#pragma warning disable WRANGLER0001, WRANGLER0002 // WRANGLER0002: " + Title3 + "; WRANGLER0001: " + Title2 + Reason)]
    [DataRow("#pragma warning disable CONTOSO001 // Custom rule title" + Reason)]
    [DataRow("#pragma warning disable 0168 // Unused local variable" + Reason)]
    [DataRow("#pragma warning disable 0168, 168, CS0168 // Unused local variable" + Reason)]
    public async Task DocumentedSuppression_DoesNotReport(string source)
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(source, out Document document);
        Assert.IsEmpty(await PragmaTestHelper.GetDiagnosticsAsync(document));
    }

    [TestMethod]
    [DataRow("#pragma warning disable WRANGLER0002", "WRANGLER0004,WRANGLER0005")]
    [DataRow("#pragma warning disable", "WRANGLER0004,WRANGLER0005")]
    [DataRow("#pragma warning disable // Justification: Intentional.", "WRANGLER0004")]
    [DataRow("#pragma warning disable WRANGLER0002 // " + Title3, "WRANGLER0005")]
    [DataRow("#pragma warning disable WRANGLER0002 // Justification: Tests null rejection.", "WRANGLER0004")]
    [DataRow("#pragma warning disable WRANGLER0002 // Wrong title" + Reason, "WRANGLER0004")]
    [DataRow("#pragma warning disable WRANGLER0002 // " + Title3 + "; Justification:   ", "WRANGLER0005")]
    [DataRow("#pragma warning disable WRANGLER0001, WRANGLER0002 // WRANGLER0001: " + Title2 + Reason, "WRANGLER0004")]
    [DataRow("#pragma warning disable WRANGLER0001, WRANGLER0002 // Bare title" + Reason, "WRANGLER0004")]
    [DataRow("#pragma warning disable WRANGLER0001, WRANGLER0002 // WRANGLER0001: " + Title2 + "; WRANGLER0001: " + Title2 + Reason, "WRANGLER0004")]
    [DataRow("#pragma warning disable WRANGLER0002 // " + Title3 + " - Justification: Tests null rejection.", "WRANGLER0004,WRANGLER0005")]
    [DataRow("#pragma warning disable WRANGLER0002 // " + Title3 + "." + Reason, "WRANGLER0004")]
    public async Task MissingMetadata_ReportsIndependentRulesAtDisableKeyword(string source, string expectedIds)
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(source, out Document document);
        ImmutableArray<Diagnostic> diagnostics = await PragmaTestHelper.GetDiagnosticsAsync(document);
        CollectionAssert.AreEqual(expectedIds.Split(','), diagnostics.Select(diagnostic => diagnostic.Id).ToArray());
        foreach (Diagnostic diagnostic in diagnostics)
        {
            Assert.AreEqual("disable", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
            Assert.IsFalse(diagnostic.IsSuppressed);
        }
    }

    [TestMethod]
    [DataRow("// Justification: Explains this directive.\n", true)]
    [DataRow("    // justification : Explains this directive.\r\n", true)]
    [DataRow("// Justification: TODO\n", true)]
    [DataRow("// Justification:   \n", false)]
    [DataRow("// An unrelated comment.\n", false)]
    [DataRow("// Justification: Too far away.\n\n", false)]
    [DataRow("class C { } // Justification: Belongs to the preceding code.\n", false)]
    [DataRow("/// Justification: Documentation comment.\n", false)]
    [DataRow("/* Justification: Block comment. */\n", false)]
    [DataRow("// Justification: First line.\n// Continuation, not an attached justification.\n", false)]
    public async Task PrecedingJustification_RequiresAnImmediatelyAdjacentStandaloneLine(string prefix, bool accepted)
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(
            prefix + "#pragma warning disable WRANGLER0002 // " + Title3, out Document document);
        ImmutableArray<Diagnostic> diagnostics = await PragmaTestHelper.GetDiagnosticsAsync(document);
        CollectionAssert.AreEqual(
            accepted ? Array.Empty<string>() : ["WRANGLER0005"],
            diagnostics.Select(diagnostic => diagnostic.Id).ToArray());
    }

    [TestMethod]
    public async Task PrecedingJustification_DoesNotCarryAcrossStackedDirectives()
    {
        string source = "// Justification: First directive only.\n"
            + "#pragma warning disable WRANGLER0002 // " + Title3 + "\n"
            + "#pragma warning disable WRANGLER0001 // " + Title2;
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(source, out Document document);
        Diagnostic diagnostic = Assert.ContainsSingle(await PragmaTestHelper.GetDiagnosticsAsync(document));
        Assert.AreEqual("WRANGLER0005", diagnostic.Id);
        Assert.AreEqual(2, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    [TestMethod]
    [DataRow("#if false\n#pragma warning disable WRANGLER0002\n#endif", "Test.cs")]
    [DataRow("#pragma warning restore WRANGLER0002", "Test.cs")]
    [DataRow("#pragma warning disable WRANGLER0002", "Test.g.cs")]
    [DataRow("// <auto-generated/>\n#pragma warning disable WRANGLER0002", "Test.cs")]
    public async Task InactiveGeneratedAndRestoreDirectives_AreIgnored(string source, string fileName)
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(source, out Document document, fileName: fileName);
        Assert.IsEmpty(await PragmaTestHelper.GetDiagnosticsAsync(document));
    }

    [TestMethod]
    [DataRow("#pragma warning disable WRANGLER0002,")]
    [DataRow("#pragma warning enable WRANGLER0002")]
    [DataRow("#pragma warning disable WRANGLER0002 +")]
    [DataRow("#pragma warning disable \"WRANGLER0002\"")]
    [DataRow("#pragma warning disable WRANGLER0002 /* note */")]
    public async Task IncompleteDirectives_DoNotThrowOrReportMetadata(string source)
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(source, out Document document);
        Assert.IsEmpty(await PragmaTestHelper.GetDiagnosticsAsync(document, assertValidSource: false));
    }

    [TestMethod]
    public async Task DefaultSettings_LeaveBothRulesDisabled()
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(
            "#pragma warning disable WRANGLER0002", out Document document, editorConfig: "root = true\n[*.cs]");
        Assert.IsEmpty(await PragmaTestHelper.GetDiagnosticsAsync(document));
    }

    [TestMethod]
    [DataRow("CONTOSO001", "Prefer C# interfaces; allow x = y #1")]
    [DataRow("0168", "Unused variable")]
    [DataRow("0168, 168, CS0168", "Unused variable")]
    public async Task AdditionalCatalog_PreservesPunctuationAndNormalizesNumericIds(string ids, string title)
    {
        string id = ids == "CONTOSO001" ? ids : "CS0168";
        string source = $"#pragma warning disable {ids} // {title}{Reason}";
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(
            source, out Document document, catalog: $"# Catalog comment\n{id} = {title}\n",
            editorConfig: PragmaTestHelper.EnabledConfig + "\nwrangler_pragma_title_validation = strict");
        Assert.IsEmpty(await PragmaTestHelper.GetDiagnosticsAsync(document));
    }

    [TestMethod]
    [DataRow("documented")]
    [DataRow("strict")]
    public async Task CatalogTitles_AreVerifiedInBothModes(string mode)
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(
            "#pragma warning disable CONTOSO001 // Wrong" + Reason, out Document document,
            catalog: "CONTOSO001 = Prefer C# interfaces",
            editorConfig: PragmaTestHelper.EnabledConfig + $"\nwrangler_pragma_title_validation = {mode}");
        Diagnostic diagnostic = Assert.ContainsSingle(await PragmaTestHelper.GetDiagnosticsAsync(document));
        Assert.AreEqual("WRANGLER0004", diagnostic.Id);
        StringAssert.Contains(diagnostic.GetMessage(), "Prefer C# interfaces");
    }

    [TestMethod]
    [DataRow("not an entry", "Expected 'ID = Title'")]
    [DataRow("CONTOSO001 = ", "empty")]
    [DataRow("0168 = Unused local", "CS0168")]
    [DataRow("WRANGLER0002 = Not authoritative", "reserved")]
    [DataRow("WRANGLER0099 = Future Wrangler rule", "reserved")]
    [DataRow("CONTOSO001 = First\nCONTOSO001 = Second", "Duplicate")]
    [DataRow("CONTOSO001 = Justification: Not a title", "reserved")]
    [DataRow("CONTOSO001 = Title; Justification: Not title text", "reserved")]
    public async Task InvalidCatalog_IsReportedAtPragmaWithoutHidingJustification(string catalog, string message)
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(
            "#pragma warning disable CONTOSO001 // A title" + Reason, out Document document, catalog: catalog);
        Diagnostic diagnostic = Assert.ContainsSingle(await PragmaTestHelper.GetDiagnosticsAsync(document));
        Assert.AreEqual("WRANGLER0004", diagnostic.Id);
        StringAssert.Contains(diagnostic.GetMessage(), message);
        Assert.AreEqual("disable", (await document.GetTextAsync()).ToString(diagnostic.Location.SourceSpan));
    }

    [TestMethod]
    public async Task UnreadableCatalog_IsReportedExplicitly()
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(
            "#pragma warning disable CONTOSO001 // A title" + Reason, out Document document);
        Diagnostic diagnostic = Assert.ContainsSingle(await PragmaTestHelper.GetDiagnosticsAsync(
            document, additionalFiles: [new UnreadableCatalog()]));
        StringAssert.Contains(diagnostic.GetMessage(), "Cannot read");
    }

    [TestMethod]
    public async Task DuplicateIdsAcrossCatalogFiles_RejectTheEntireCatalog()
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(
            "#pragma warning disable CONTOSO001 // First title" + Reason,
            out Document document, catalog: "CONTOSO001 = First title");
        document = document.Project.AddAdditionalDocument(
            "Wrangler.PragmaTitles.txt", SourceText.From("CONTOSO001 = Second title"),
            filePath: Path.Combine(Path.GetTempPath(), "OtherCatalog", "Wrangler.PragmaTitles.txt"))
            .Project.GetDocument(document.Id) ?? throw new InvalidOperationException("Missing document.");
        Diagnostic diagnostic = Assert.ContainsSingle(await PragmaTestHelper.GetDiagnosticsAsync(document));
        StringAssert.Contains(diagnostic.GetMessage(), "Duplicate");
        Assert.IsEmpty(await PragmaTestHelper.GetCodeActionsAsync(document, diagnostic));
    }

    [TestMethod]
    public async Task DisjointCatalogFiles_AreMergedForValidationAndFixes()
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(
            "#pragma warning disable CONTOSO001, CONTOSO002 // Justification: Intentional.",
            out Document document, catalog: "CONTOSO001 = First title",
            editorConfig: PragmaTestHelper.EnabledConfig + "\nwrangler_pragma_title_validation = strict");
        document = document.Project.AddAdditionalDocument(
            "Wrangler.PragmaTitles.txt", SourceText.From("CONTOSO002 = Second title"),
            filePath: Path.Combine(Path.GetTempPath(), "OtherCatalog", "Wrangler.PragmaTitles.txt"))
            .Project.GetDocument(document.Id) ?? throw new InvalidOperationException("Missing document.");
        Diagnostic diagnostic = Assert.ContainsSingle(await PragmaTestHelper.GetDiagnosticsAsync(document));
        CodeAction action = Assert.ContainsSingle(await PragmaTestHelper.GetCodeActionsAsync(document, diagnostic));
        Document changed = await PragmaTestHelper.ApplyAsync(document, action);
        Assert.AreEqual(
            "#pragma warning disable CONTOSO001, CONTOSO002 // CONTOSO001: First title; CONTOSO002: Second title; Justification: Intentional.",
            (await changed.GetTextAsync()).ToString());
        Assert.IsEmpty(await PragmaTestHelper.GetDiagnosticsAsync(changed));
    }

    [TestMethod]
    public async Task EveryWranglerDescriptor_IsAvailableIncludingPolicyRules()
    {
        ImmutableArray<DiagnosticDescriptor> descriptors = typeof(PragmaSuppressionAnalyzer).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            .Select(type => (DiagnosticAnalyzer)(Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"Could not create analyzer '{type.Name}'.")))
            .SelectMany(analyzer => analyzer.SupportedDiagnostics)
            .ToImmutableArray();
        string source = string.Join("\n", descriptors.Select(descriptor =>
            $"#pragma warning disable {descriptor.Id} // {descriptor.Title.ToString(CultureInfo.InvariantCulture)}{Reason}\n"
            + $"#pragma warning restore {descriptor.Id}"));
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(
            source, out Document document,
            editorConfig: PragmaTestHelper.EnabledConfig + "\nwrangler_pragma_title_validation = strict");
        Assert.IsEmpty(await PragmaTestHelper.GetDiagnosticsAsync(document));

        string incorrectTitles = string.Join("\n", descriptors.Select(descriptor =>
            $"#pragma warning disable {descriptor.Id} // Incorrect title{Reason}\n#pragma warning restore {descriptor.Id}"));
        ImmutableArray<Diagnostic> diagnostics = await PragmaTestHelper.GetDiagnosticsAsync(
            document.WithText(SourceText.From(incorrectTitles)));
        Assert.HasCount(descriptors.Length, diagnostics);
        Assert.DoesNotContain(diagnostic => diagnostic.Id != "WRANGLER0004", diagnostics);
        for (int index = 0; index < descriptors.Length; index++)
        {
            StringAssert.Contains(diagnostics[index].GetMessage(), $"'{descriptors[index].Id}'");
        }
    }

    [TestMethod]
    [DataRow("WRANGLER0004", "Document suppressed diagnostic IDs and titles")]
    [DataRow("WRANGLER0005", "Justify warning suppressions")]
    public async Task PolicyRules_HonorScopedSuppressionAndRestoration(string id, string title)
    {
        string violation = id == "WRANGLER0004" ? "Wrong title" + Reason : Title3;
        string source = $"#pragma warning disable {id} // {title}{Reason}\n"
            + $"#pragma warning disable WRANGLER0002 // {violation}\n"
            + $"#pragma warning restore {id}\n"
            + $"#pragma warning disable WRANGLER0002 // {violation}";
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(source, out Document document);
        ImmutableArray<Diagnostic> diagnostics = await PragmaTestHelper.GetDiagnosticsAsync(document, includeSuppressed: true);
        Assert.HasCount(2, diagnostics);
        Assert.DoesNotContain(diagnostic => diagnostic.Id != id, diagnostics);
        Assert.IsTrue(diagnostics[0].IsSuppressed);
        Assert.IsFalse(diagnostics[1].IsSuppressed);
        Assert.AreEqual(3, diagnostics[1].Location.GetLineSpan().StartLinePosition.Line);
    }

    [TestMethod]
    public async Task IdeConstructedSiblingComment_IsReadLikeParsedComment()
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace("class C { }", out Document document);
        SyntaxNode root = await document.GetSyntaxRootAsync()
            ?? throw new InvalidOperationException("No syntax root.");
        SyntaxTrivia directive = SyntaxFactory.Trivia(SyntaxFactory.PragmaWarningDirectiveTrivia(
            SyntaxFactory.Token(SyntaxKind.DisableKeyword),
            SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(SyntaxFactory.IdentifierName("WRANGLER0002")),
            isActive: true));
        SyntaxToken token = root.GetFirstToken();
        root = root.ReplaceToken(token, token.WithLeadingTrivia(
            SyntaxFactory.Comment("// Justification: Tests null rejection."),
            SyntaxFactory.CarriageReturnLineFeed,
            directive,
            SyntaxFactory.Comment(" // " + Title3),
            SyntaxFactory.CarriageReturnLineFeed));
        document = document.WithSyntaxRoot(Formatter.Format(root, workspace));
        StringAssert.Contains((await document.GetTextAsync()).ToString(), "disable WRANGLER0002 // " + Title3);
        Assert.IsEmpty(await PragmaTestHelper.GetDiagnosticsAsync(document));
    }

    [TestMethod]
    public async Task ConstructedBlockComment_DoesNotGetSwallowedByInsertedTitle()
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace("class C { }", out Document document);
        SyntaxNode root = await document.GetSyntaxRootAsync()
            ?? throw new InvalidOperationException("No syntax root.");
        SyntaxTrivia directive = SyntaxFactory.Trivia(SyntaxFactory.PragmaWarningDirectiveTrivia(
            SyntaxFactory.Token(SyntaxKind.DisableKeyword),
            SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(SyntaxFactory.IdentifierName("WRANGLER0002")),
            isActive: true));
        SyntaxToken token = root.GetFirstToken();
        root = root.ReplaceToken(token, token.WithLeadingTrivia(
            directive, SyntaxFactory.Space, SyntaxFactory.Comment("/* note */"), SyntaxFactory.CarriageReturnLineFeed));
        document = document.WithSyntaxRoot(Formatter.Format(root, workspace));
        StringAssert.Contains((await document.GetTextAsync()).ToString(), "disable WRANGLER0002 /* note */");
        Diagnostic diagnostic = Assert.ContainsSingle(
            item => item.Id == "WRANGLER0004", await PragmaTestHelper.GetDiagnosticsAsync(document));
        Assert.IsEmpty(await PragmaTestHelper.GetCodeActionsAsync(document, diagnostic));
    }

    [TestMethod]
    [DataRow("#pragma warning disable WRANGLER0002", "#pragma warning disable WRANGLER0002 // " + Title3)]
    [DataRow("#pragma warning disable WRANGLER0002  ", "#pragma warning disable WRANGLER0002 // " + Title3 + "  ")]
    [DataRow("#pragma warning disable WRANGLER0002 //", "#pragma warning disable WRANGLER0002 // " + Title3)]
    [DataRow("#pragma warning disable WRANGLER0002 // Justification: Tests null rejection.", "#pragma warning disable WRANGLER0002 // " + Title3 + Reason)]
    [DataRow("#pragma warning disable WRANGLER0002 // WRANGLER0002: Old title" + Reason, "#pragma warning disable WRANGLER0002 // " + Title3 + Reason)]
    [DataRow("#pragma warning disable WRANGLER0002 // WRANGLER0002:" + Reason, "#pragma warning disable WRANGLER0002 // " + Title3 + Reason)]
    [DataRow("#pragma warning disable WRANGLER0001, WRANGLER0002 // WRANGLER0001: " + Title2 + Reason,
        "#pragma warning disable WRANGLER0001, WRANGLER0002 // WRANGLER0001: " + Title2 + "; WRANGLER0002: " + Title3 + Reason)]
    [DataRow("#pragma warning disable WRANGLER0001, WRANGLER0002 // WRANGLER0001:; WRANGLER0002: Wrong" + Reason,
        "#pragma warning disable WRANGLER0001, WRANGLER0002 // WRANGLER0001: " + Title2 + "; WRANGLER0002: " + Title3 + Reason)]
    [DataRow("#pragma warning disable WRANGLER0002 //  WRANGLER0002: Wrong ; JUSTification \t:  Why; more #text  \r\nclass C { }\r\n",
        "#pragma warning disable WRANGLER0002 //  " + Title3 + "; JUSTification \t:  Why; more #text  \r\nclass C { }\r\n")]
    public async Task TitleFix_ChangesOnlySafeTitleMetadata(string source, string expected)
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(source, out Document document);
        ImmutableArray<Diagnostic> before = await PragmaTestHelper.GetDiagnosticsAsync(document);
        Diagnostic diagnostic = Assert.ContainsSingle(item => item.Id == "WRANGLER0004", before);
        CodeAction action = Assert.ContainsSingle(await PragmaTestHelper.GetCodeActionsAsync(document, diagnostic));
        Document changed = await PragmaTestHelper.ApplyAsync(document, action);
        Assert.AreEqual(expected, (await changed.GetTextAsync()).ToString());
        ImmutableArray<Diagnostic> after = await PragmaTestHelper.GetDiagnosticsAsync(changed);
        Assert.DoesNotContain(diagnostic => diagnostic.Id == "WRANGLER0004", after);
        Assert.HasCount(
            before.Count(diagnostic => diagnostic.Id == "WRANGLER0005"),
            after.Where(diagnostic => diagnostic.Id == "WRANGLER0005"));
    }

    [TestMethod]
    [DataRow("#pragma warning disable")]
    [DataRow("#pragma warning disable CONTOSO001")]
    [DataRow("#pragma warning disable WRANGLER0002 // Deliberate null-input test.")]
    [DataRow("#pragma warning disable WRANGLER0002 // WRANGLER0002: Old title; preserve this thought" + Reason)]
    [DataRow("#pragma warning disable WRANGLER0001, WRANGLER0002 // An ambiguous comment" + Reason)]
    [DataRow("#pragma warning disable WRANGLER0001, WRANGLER0002 // WRANGLER0001: Old; WRANGLER0001: Again" + Reason)]
    [DataRow("#pragma warning disable WRANGLER0001, WRANGLER0002 // Justification: A reason; WRANGLER0001: A misplaced title")]
    public async Task AmbiguousOrUnknownMetadata_HasNoAutomaticFix(string source)
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(source, out Document document);
        Diagnostic diagnostic = Assert.ContainsSingle(
            item => item.Id == "WRANGLER0004", await PragmaTestHelper.GetDiagnosticsAsync(document));
        Assert.IsEmpty(await PragmaTestHelper.GetCodeActionsAsync(document, diagnostic));
    }

    [TestMethod]
    public async Task MissingExternalTitle_FixUsesProjectAnalyzerDescriptors()
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(
            "#pragma warning disable CONTOSO001 // Justification: Intentional.", out Document document);
        document = document.Project.AddAnalyzerReference(new AnalyzerImageReference([new ExternalAnalyzer("Prefer C# interfaces; avoid blocking")]))
            .GetDocument(document.Id) ?? throw new InvalidOperationException("Missing document.");
        Diagnostic diagnostic = Assert.ContainsSingle(await PragmaTestHelper.GetDiagnosticsAsync(document));
        CodeAction action = Assert.ContainsSingle(await PragmaTestHelper.GetCodeActionsAsync(document, diagnostic));
        Document changed = await PragmaTestHelper.ApplyAsync(document, action);
        Assert.AreEqual(
            "#pragma warning disable CONTOSO001 // Prefer C# interfaces; avoid blocking; Justification: Intentional.",
            (await changed.GetTextAsync()).ToString());
        Assert.IsEmpty(await PragmaTestHelper.GetDiagnosticsAsync(changed));
    }

    [TestMethod]
    public async Task CatalogTitleFix_PrefersProjectApprovedTitleOverDiscoveredDescriptor()
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(
            "#pragma warning disable CONTOSO001 // CONTOSO001: Wrong" + Reason, out Document document,
            catalog: "CONTOSO001 = Prefer C# interfaces; allow x = y");
        document = document.Project.AddAnalyzerReference(new AnalyzerImageReference([new ExternalAnalyzer("Different package title")]))
            .GetDocument(document.Id) ?? throw new InvalidOperationException("Missing document.");
        Diagnostic diagnostic = Assert.ContainsSingle(await PragmaTestHelper.GetDiagnosticsAsync(document));
        CodeAction action = Assert.ContainsSingle(await PragmaTestHelper.GetCodeActionsAsync(document, diagnostic));
        Document changed = await PragmaTestHelper.ApplyAsync(document, action);
        Assert.AreEqual(
            "#pragma warning disable CONTOSO001 // Prefer C# interfaces; allow x = y" + Reason,
            (await changed.GetTextAsync()).ToString());
        Assert.IsEmpty(await PragmaTestHelper.GetDiagnosticsAsync(changed));
    }

    [TestMethod]
    public async Task ConflictingProjectDescriptors_DoNotProduceGuessedTitle()
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(
            "#pragma warning disable CONTOSO001" + " // Justification: Intentional.", out Document document);
        document = document.Project.AddAnalyzerReference(new AnalyzerImageReference(
            [new ExternalAnalyzer("First title"), new ExternalAnalyzer("Conflicting title")]))
            .GetDocument(document.Id) ?? throw new InvalidOperationException("Missing document.");
        Diagnostic diagnostic = Assert.ContainsSingle(await PragmaTestHelper.GetDiagnosticsAsync(document));
        Assert.IsEmpty(await PragmaTestHelper.GetCodeActionsAsync(document, diagnostic));
    }

    [TestMethod]
    public async Task DefaultDescriptorArray_DoesNotPreventDiscoveringAvailableTitle()
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(
            "#pragma warning disable CONTOSO001 // Justification: Intentional.", out Document document);
        document = document.Project.AddAnalyzerReference(new AnalyzerImageReference(
            [new ExternalAnalyzer(null), new ExternalAnalyzer("Available title")]))
            .GetDocument(document.Id) ?? throw new InvalidOperationException("Missing document.");
        Diagnostic diagnostic = Assert.ContainsSingle(await PragmaTestHelper.GetDiagnosticsAsync(document));
        CodeAction action = Assert.ContainsSingle(await PragmaTestHelper.GetCodeActionsAsync(document, diagnostic));
        Document changed = await PragmaTestHelper.ApplyAsync(document, action);
        Assert.AreEqual(
            "#pragma warning disable CONTOSO001 // Available title; Justification: Intentional.",
            (await changed.GetTextAsync()).ToString());
        Assert.IsEmpty(await PragmaTestHelper.GetDiagnosticsAsync(changed));
    }

    [TestMethod]
    [DataRow("First line\nsecond line")]
    [DataRow("First line\r\n#pragma warning disable")]
    [DataRow("First line\u0085second line")]
    [DataRow("First line\u2028second line")]
    [DataRow("First line\u2029second line")]
    public async Task MultilineExternalTitles_DoNotOfferSourceChangingFix(string title)
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(
            "#pragma warning disable CONTOSO001 // Justification: Intentional.", out Document document);
        document = document.Project.AddAnalyzerReference(new AnalyzerImageReference([new ExternalAnalyzer(title)]))
            .GetDocument(document.Id) ?? throw new InvalidOperationException("Missing document.");
        Diagnostic diagnostic = Assert.ContainsSingle(await PragmaTestHelper.GetDiagnosticsAsync(document));
        Assert.IsEmpty(await PragmaTestHelper.GetCodeActionsAsync(document, diagnostic));
    }

    [TestMethod]
    public async Task StrictUnknownTitle_DoesNotOfferFixThatLeavesViolation()
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(
            "#pragma warning disable CONTOSO001 // A documented title" + Reason, out Document document,
            editorConfig: PragmaTestHelper.EnabledConfig + "\nwrangler_pragma_title_validation = strict");
        document = document.Project.AddAnalyzerReference(new AnalyzerImageReference([new ExternalAnalyzer("A documented title")]))
            .GetDocument(document.Id) ?? throw new InvalidOperationException("Missing document.");
        Diagnostic diagnostic = Assert.ContainsSingle(await PragmaTestHelper.GetDiagnosticsAsync(document));
        StringAssert.Contains(diagnostic.GetMessage(), "catalog entry");
        Assert.IsEmpty(await PragmaTestHelper.GetCodeActionsAsync(document, diagnostic));
    }

    [TestMethod]
    public async Task StaleDiagnostic_DoesNotEditChangedDocument()
    {
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(
            "#pragma warning disable WRANGLER0002", out Document document);
        Diagnostic diagnostic = Assert.ContainsSingle(
            item => item.Id == "WRANGLER0004", await PragmaTestHelper.GetDiagnosticsAsync(document));
        Document changed = document.WithText(SourceText.From("class Changed { }"));
        Assert.IsEmpty(await PragmaTestHelper.GetCodeActionsAsync(changed, diagnostic));
    }

    [TestMethod]
    public async Task FixAll_HandlesAdjacentDirectivesWithoutOverlappingEdits()
    {
        const string source = "#pragma warning disable WRANGLER0002\n#pragma warning disable WRANGLER0001\nclass C { }";
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(source, out Document document);
        ImmutableArray<Diagnostic> diagnostics = (await PragmaTestHelper.GetDiagnosticsAsync(document))
            .Where(diagnostic => diagnostic.Id == "WRANGLER0004").ToImmutableArray();
        var provider = new PragmaSuppressionCodeFixProvider();
        var context = new FixAllContext(
            document, provider, FixAllScope.Document, "Document suppressed rule titles",
            provider.FixableDiagnosticIds, new FixedDiagnostics(diagnostics), CancellationToken.None);
        CodeAction action = await provider.GetFixAllProvider().GetFixAsync(context)
            ?? throw new InvalidOperationException("No Fix All action.");
        Document changed = await PragmaTestHelper.ApplyAsync(document, action);
        Assert.AreEqual(
            $"#pragma warning disable WRANGLER0002 // {Title3}\n#pragma warning disable WRANGLER0001 // {Title2}\nclass C {{ }}",
            (await changed.GetTextAsync()).ToString());
        Assert.DoesNotContain(
            diagnostic => diagnostic.Id == "WRANGLER0004", await PragmaTestHelper.GetDiagnosticsAsync(changed));
    }

    private sealed class UnreadableCatalog : AdditionalText
    {
        public override string Path => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Wrangler.PragmaTitles.txt");
        public override SourceText? GetText(CancellationToken cancellationToken = default) => null;
    }

    // Justification: The synthetic analyzer is instantiated directly, never loaded as a compiler extension.
#pragma warning disable RS1001 // Missing diagnostic analyzer attribute
    private sealed class ExternalAnalyzer(string? title) : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            title is null ? default : [new("CONTOSO001", title, title, "Tests", DiagnosticSeverity.Warning, isEnabledByDefault: true)];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
        }
    }
#pragma warning restore RS1001

    private sealed class FixedDiagnostics(ImmutableArray<Diagnostic> diagnostics) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
    }
}

internal static class PragmaTestHelper
{
    internal const string EnabledConfig = """
        root = true
        [*.cs]
        dotnet_diagnostic.WRANGLER0004.severity = warning
        dotnet_diagnostic.WRANGLER0005.severity = warning
        """;

    internal static AdhocWorkspace CreateWorkspace(
        string source,
        out Document document,
        string? editorConfig = null,
        string? catalog = null,
        string fileName = "Test.cs")
    {
        var workspace = new AdhocWorkspace();
        ProjectId projectId = ProjectId.CreateNewId();
        DocumentId documentId = DocumentId.CreateNewId(projectId);
        string directory = Path.Combine(Path.GetTempPath(), nameof(PragmaSuppressionTests));
        Solution solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId, VersionStamp.Create(), "PragmaTests", "PragmaTests", LanguageNames.CSharp,
                parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]))
            .AddDocument(documentId, fileName, SourceText.From(source), filePath: Path.Combine(directory, fileName))
            .AddAnalyzerConfigDocument(
                DocumentId.CreateNewId(projectId), ".editorconfig", SourceText.From(editorConfig ?? EnabledConfig),
                filePath: Path.Combine(directory, ".editorconfig"));
        if (catalog is not null)
        {
            solution = solution.AddAdditionalDocument(
                DocumentId.CreateNewId(projectId), "Wrangler.PragmaTitles.txt", SourceText.From(catalog),
                filePath: Path.Combine(directory, "Wrangler.PragmaTitles.txt"));
        }

        Assert.IsTrue(workspace.TryApplyChanges(solution));
        document = workspace.CurrentSolution.GetDocument(documentId)
            ?? throw new InvalidOperationException("Could not create the pragma test document.");
        return workspace;
    }

    internal static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        Document document,
        bool includeSuppressed = false,
        bool assertValidSource = true,
        ImmutableArray<AdditionalText> additionalFiles = default)
    {
        Compilation compilation = await document.Project.GetCompilationAsync()
            ?? throw new InvalidOperationException("Could not create the pragma test compilation.");
        if (assertValidSource)
        {
            ImmutableArray<Diagnostic> errors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToImmutableArray();
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        AnalyzerOptions options = additionalFiles.IsDefault
            ? document.Project.AnalyzerOptions
            : new AnalyzerOptions(additionalFiles, document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider);
        var driverOptions = new CompilationWithAnalyzersOptions(
            options, onAnalyzerException: null, concurrentAnalysis: true, logAnalyzerExecutionTime: false,
            reportSuppressedDiagnostics: includeSuppressed);
        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers([new PragmaSuppressionAnalyzer()], driverOptions).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostic => diagnostic.Id == "AD0001", diagnostics, string.Join("\n", diagnostics));
        return diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal).ToImmutableArray();
    }

    internal static async Task<List<CodeAction>> GetCodeActionsAsync(Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        await new PragmaSuppressionCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(
            document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        return actions;
    }

    internal static async Task<Document> ApplyAsync(Document document, CodeAction action)
    {
        ImmutableArray<CodeActionOperation> operations = await action.GetOperationsAsync(CancellationToken.None);
        ApplyChangesOperation change = Assert.ContainsSingle(operations.OfType<ApplyChangesOperation>());
        return change.ChangedSolution.GetDocument(document.Id)
            ?? throw new InvalidOperationException("The code fix removed the document.");
    }
}
