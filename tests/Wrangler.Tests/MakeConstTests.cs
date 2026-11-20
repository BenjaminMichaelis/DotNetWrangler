using System.Collections.Immutable;
using Wrangler.Analyzers;
using Wrangler.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
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

        string fixedCode = await ApplyFirstCodeFixAsync(document, diagnostics[0]);
        StringAssert.Contains(fixedCode, "const int value = 0;");
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

        string fixedCode = await ApplyFirstCodeFixAsync(document, diagnostics[0]);
        StringAssert.Contains(fixedCode, "const string value = \"text\";");
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
    public async Task MultipleVariablesWithNonConstantInitializer_DoesNotReportDiagnostic()
    {
        const string source = """
            class Program
            {
                static void Main()
                {
                    int first = 0, second = System.DateTime.Now.Day;
                }
            }
            """;

        using AdhocWorkspace workspace = CreateWorkspace(source, out Document document);
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(document);
        Assert.AreEqual(0, diagnostics.Length);
    }

    private static async Task<string> ApplyFirstCodeFixAsync(Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);
        await new MakeConstCodeFixProvider().RegisterCodeFixesAsync(context);
        Assert.AreEqual(1, actions.Count);

        IEnumerable<CodeActionOperation> operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        ApplyChangesOperation change = operations.OfType<ApplyChangesOperation>().Single();
        Document changedDocument = change.ChangedSolution.GetDocument(document.Id)
            ?? throw new InvalidOperationException("The code fix removed the document.");
        return (await changedDocument.GetTextAsync()).ToString();
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
        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MakeConstAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }
}
