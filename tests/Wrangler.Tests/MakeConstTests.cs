using System.Collections.Immutable;
using Wrangler.Analyzers;
using Wrangler.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Wrangler.Tests;

[TestClass]
public sealed class MakeConstTests
{
    [TestMethod]
    public async Task LocalIntCouldBeConstant_AddsConst()
    {
        const string source = """
            class Program
            {
                static void Main()
                {
                    int value = 0;
                    System.Console.WriteLine(value);
                }
            }
            """;

        using AdhocWorkspace workspace = CreateWorkspace(source, out Document document);
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(document);
        Assert.AreEqual(1, diagnostics.Length);

        await AssertCodeFixAsync(document, diagnostics[0], SpecialType.System_Int32, 0);
    }

    [TestMethod]
    public async Task VarStringCouldBeConstant_ReplacesVarWithInferredType()
    {
        const string source = """
            class Program
            {
                static void Main()
                {
                    var value = "text";
                }
            }
            """;

        using AdhocWorkspace workspace = CreateWorkspace(source, out Document document);
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(document);
        Assert.AreEqual(1, diagnostics.Length);

        await AssertCodeFixAsync(document, diagnostics[0], SpecialType.System_String, "text");
    }

    [TestMethod]
    public async Task ReassignedVariable_DoesNotReportDiagnostic()
    {
        const string source = """
            class Program
            {
                static void Main()
                {
                    int value = 0;
                    value++;
                }
            }
            """;

        using AdhocWorkspace workspace = CreateWorkspace(source, out Document document);
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(document);
        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    public async Task AlreadyConstant_DoesNotReportDiagnostic()
    {
        const string source = """
            class Program
            {
                static void Main()
                {
                    const int value = 0;
                }
            }
            """;

        using AdhocWorkspace workspace = CreateWorkspace(source, out Document document);
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(document);
        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    public async Task DeclarationWithoutInitializer_DoesNotReportDiagnostic()
    {
        const string source = """
            class Program
            {
                static void Main()
                {
                    int value;
                    value = 0;
                }
            }
            """;

        using AdhocWorkspace workspace = CreateWorkspace(source, out Document document);
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(document);
        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    public async Task NonConstantInitializer_DoesNotReportDiagnostic()
    {
        const string source = """
            class Program
            {
                static void Main()
                {
                    int value = System.DateTime.Now.Day;
                }
            }
            """;

        using AdhocWorkspace workspace = CreateWorkspace(source, out Document document);
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(document);
        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    public async Task ObjectInitializedWithString_DoesNotReportDiagnostic()
    {
        const string source = """
            class Program
            {
                static void Main()
                {
                    object value = "text";
                }
            }
            """;

        using AdhocWorkspace workspace = CreateWorkspace(source, out Document document);
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(document);
        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    public async Task MultipleVariablesWithNonConstantInitializer_DoesNotProduceInvalidCodeFix()
    {
        const string source = """
            class Program
            {
                static void Main()
                {
                    int first = 0, second = System.DateTime.Now.Day;
                    System.Console.WriteLine(first + second);
                }
            }
            """;

        using AdhocWorkspace workspace = CreateWorkspace(source, out Document document);
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(document);
        Assert.IsFalse(diagnostics.Any(diagnostic => diagnostic.GetMessage().Contains("'second'", StringComparison.Ordinal)));

        SyntaxNode originalRoot = await document.GetSyntaxRootAsync()
            ?? throw new InvalidOperationException("The document has no syntax root.");
        VariableDeclaratorSyntax originalSecond = originalRoot.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Single(variable => variable.Identifier.ValueText == "second");
        ExpressionSyntax originalInitializer = originalSecond.Initializer?.Value
            ?? throw new InvalidOperationException("The original runtime-valued variable has no initializer.");
        ExpressionStatementSyntax originalUse = originalRoot.DescendantNodes().OfType<ExpressionStatementSyntax>().Single();

        foreach (Diagnostic diagnostic in diagnostics)
        {
            Assert.AreEqual(MakeConstAnalyzer.DiagnosticId, diagnostic.Id);
            Document changedDocument = await ApplyFirstCodeFixAsync(document, diagnostic);
            Assert.AreEqual(0, (await GetAnalyzerDiagnosticsAsync(changedDocument)).Length);

            SyntaxNode changedRoot = await changedDocument.GetSyntaxRootAsync()
                ?? throw new InvalidOperationException("The fixed document has no syntax root.");
            SemanticModel semanticModel = await changedDocument.GetSemanticModelAsync()
                ?? throw new InvalidOperationException("The fixed document has no semantic model.");
            VariableDeclaratorSyntax first = changedRoot.DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Single(variable => variable.Identifier.ValueText == "first");
            VariableDeclaratorSyntax second = changedRoot.DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Single(variable => variable.Identifier.ValueText == "second");
            ILocalSymbol firstSymbol = Assert.IsInstanceOfType<ILocalSymbol>(semanticModel.GetDeclaredSymbol(first));
            ILocalSymbol secondSymbol = Assert.IsInstanceOfType<ILocalSymbol>(semanticModel.GetDeclaredSymbol(second));
            Assert.IsTrue(firstSymbol.IsConst);
            Assert.AreEqual(0, firstSymbol.ConstantValue);
            Assert.IsFalse(secondSymbol.IsConst);
            ExpressionSyntax changedInitializer = second.Initializer?.Value
                ?? throw new InvalidOperationException("The fixed runtime-valued variable has no initializer.");
            Assert.IsTrue(originalInitializer.IsEquivalentTo(changedInitializer));
            Assert.IsTrue(originalUse.IsEquivalentTo(changedRoot.DescendantNodes().OfType<ExpressionStatementSyntax>().Single()));
        }
    }

    private static async Task AssertCodeFixAsync(
        Document document,
        Diagnostic diagnostic,
        SpecialType expectedType,
        object expectedValue)
    {
        SyntaxNode originalRoot = await document.GetSyntaxRootAsync()
            ?? throw new InvalidOperationException("The document has no syntax root.");
        LocalDeclarationStatementSyntax originalDeclaration = originalRoot.DescendantNodes()
            .OfType<LocalDeclarationStatementSyntax>()
            .Single();
        VariableDeclaratorSyntax originalVariable = originalDeclaration.Declaration.Variables.Single();
        Assert.AreEqual(MakeConstAnalyzer.DiagnosticId, diagnostic.Id);
        StringAssert.Contains(diagnostic.GetMessage(), originalVariable.Identifier.ValueText);
        Assert.AreEqual(originalRoot.SyntaxTree, diagnostic.Location.SourceTree);
        Assert.IsTrue(diagnostic.Location.SourceSpan.Length > 0);
        Assert.IsTrue(originalDeclaration.Span.Contains(diagnostic.Location.SourceSpan));

        Document changedDocument = await ApplyFirstCodeFixAsync(document, diagnostic);
        Assert.AreEqual(0, (await GetAnalyzerDiagnosticsAsync(changedDocument)).Length);
        SyntaxNode changedRoot = await changedDocument.GetSyntaxRootAsync()
            ?? throw new InvalidOperationException("The fixed document has no syntax root.");
        LocalDeclarationStatementSyntax changedDeclaration = changedRoot.DescendantNodes()
            .OfType<LocalDeclarationStatementSyntax>()
            .Single();
        VariableDeclaratorSyntax changedVariable = changedDeclaration.Declaration.Variables.Single();
        Assert.AreEqual(originalVariable.Identifier.ValueText, changedVariable.Identifier.ValueText);
        SemanticModel semanticModel = await changedDocument.GetSemanticModelAsync()
            ?? throw new InvalidOperationException("The fixed document has no semantic model.");
        ILocalSymbol symbol = Assert.IsInstanceOfType<ILocalSymbol>(semanticModel.GetDeclaredSymbol(changedVariable));
        Assert.IsTrue(symbol.IsConst);
        Assert.AreEqual(expectedType, symbol.Type.SpecialType);
        Assert.AreEqual(expectedValue, symbol.ConstantValue);
        Assert.IsTrue(originalRoot.IsEquivalentTo(changedRoot.ReplaceNode(changedDeclaration, originalDeclaration)));
    }

    private static async Task<Document> ApplyFirstCodeFixAsync(Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);
        await new MakeConstCodeFixProvider().RegisterCodeFixesAsync(context);
        Assert.IsTrue(actions.Count > 0, "No code fix was registered for the diagnostic.");

        IEnumerable<CodeActionOperation> operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        ApplyChangesOperation change = operations.OfType<ApplyChangesOperation>().Single();
        return change.ChangedSolution.GetDocument(document.Id)
            ?? throw new InvalidOperationException("The code fix removed the document.");
    }

    private static AdhocWorkspace CreateWorkspace(string source, out Document document)
    {
        string trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("The runtime did not provide trusted platform assemblies.");
        ImmutableArray<MetadataReference> references = trustedAssemblies
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();

        var workspace = new AdhocWorkspace();
        ProjectId projectId = ProjectId.CreateNewId();
        DocumentId documentId = DocumentId.CreateNewId(projectId);
        Solution solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "TestProject",
                "TestProject",
                LanguageNames.CSharp,
                parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: references))
            .AddDocument(documentId, "Test.cs", SourceText.From(source));
        if (!workspace.TryApplyChanges(solution))
        {
            workspace.Dispose();
            throw new InvalidOperationException("Could not create the test workspace.");
        }

        document = workspace.CurrentSolution.GetDocument(documentId)
            ?? throw new InvalidOperationException("Could not create the test document.");
        return workspace;
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(Document document)
    {
        Compilation compilation = await document.Project.GetCompilationAsync()
            ?? throw new InvalidOperationException("Could not create the test compilation.");
        Diagnostic[] errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(diagnostic => diagnostic.ToString())));
        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MakeConstAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }
}
