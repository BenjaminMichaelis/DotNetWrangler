using System.Collections.Immutable;
using MakeConst.Analyzers;
using MakeConst.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace MakeConst.Tests;

[TestClass]
public sealed class MakeConstTests
{
    [TestMethod]
    public async Task LowercaseTypeName_IsRenamedToUppercase()
    {
        const string source = """
            class program
            {
                static void Main()
                {
                    var value = new program();
                }
            }
            """;

        using AdhocWorkspace workspace = CreateWorkspace(source, out Document document);
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(document);
        Assert.AreEqual(1, diagnostics.Length);

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostics[0],
            (action, _) => actions.Add(action),
            CancellationToken.None);
        await new MakeConstCodeFixProvider().RegisterCodeFixesAsync(context);
        Assert.AreEqual(1, actions.Count);

        IEnumerable<CodeActionOperation> operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        ApplyChangesOperation change = operations.OfType<ApplyChangesOperation>().Single();
        Document changedDocument = change.ChangedSolution.GetDocument(document.Id)
            ?? throw new InvalidOperationException("The code fix removed the document.");
        string fixedCode = (await changedDocument.GetTextAsync()).ToString().Trim();

        Assert.AreEqual(
            """
            class PROGRAM
            {
                static void Main()
                {
                    var value = new PROGRAM();
                }
            }
            """,
            fixedCode);
    }

    [TestMethod]
    public async Task UppercaseTypeName_HasNoDiagnostic()
    {
        using AdhocWorkspace workspace = CreateWorkspace("class PROGRAM { }", out Document document);
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(document);
        Assert.AreEqual(0, diagnostics.Length);
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
