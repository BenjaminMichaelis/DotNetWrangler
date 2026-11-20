using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Wrangler.Analyzers;

namespace Wrangler.Tests;

[TestClass]
public sealed class AnalyzerConfigurationTests
{
    [TestMethod]
    [DataRow("WR0001", "none")]
    [DataRow("WR0001", "warning")]
    [DataRow("WR0001", "error")]
    [DataRow("WR0002", "none")]
    [DataRow("WR0002", "warning")]
    [DataRow("WR0002", "error")]
    [DataRow("WR0003", "none")]
    [DataRow("WR0003", "warning")]
    [DataRow("WR0003", "error")]
    [DataRow("WR0004", "none")]
    [DataRow("WR0004", "warning")]
    [DataRow("WR0004", "error")]
    public async Task EditorConfig_ConfiguresEachRuleIndependently(string ruleId, string severity)
    {
        const string source = """
            class Person
            {
                public string FirstName { get; set; } = null!;

                public int NameLength(string? name) => name!.Length;

                public int GetAnswer()
                {
                    int answer = 42;
                    return answer;
                }
            }

            class PersonTests
            {
                public string? Name { get; set; }

                [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
                public void NameIsSet()
                {
                    _ = Name!.Length;
                }
            }
            """;
        string[] ruleIds = ["WR0001", "WR0002", "WR0003", "WR0004"];
        string editorConfig = "root = true\n\n[*.cs]\n" + string.Join(
            "\n",
            ruleIds.Select(id => $"dotnet_diagnostic.{id}.severity = {(id == ruleId ? severity : "warning")}"));
        string trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("The runtime did not provide trusted platform assemblies.");
        string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new InvalidOperationException("Could not locate the runtime directory.");
        IEnumerable<MetadataReference> references = trustedAssemblies.Split(Path.PathSeparator)
            .Where(path => Path.GetDirectoryName(path) == runtimeDirectory)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(TestMethodAttribute).Assembly.Location));

        using var workspace = new AdhocWorkspace();
        ProjectId projectId = ProjectId.CreateNewId();
        string projectDirectory = Path.Combine(Path.GetTempPath(), nameof(AnalyzerConfigurationTests));
        Solution solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "TestProject",
                "TestProject",
                LanguageNames.CSharp,
                parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
                compilationOptions: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable),
                metadataReferences: references))
            .AddDocument(
                DocumentId.CreateNewId(projectId),
                "Test.cs",
                SourceText.From(source),
                filePath: Path.Combine(projectDirectory, "Test.cs"))
            .AddAnalyzerConfigDocument(
                DocumentId.CreateNewId(projectId),
                ".editorconfig",
                SourceText.From(editorConfig),
                filePath: Path.Combine(projectDirectory, ".editorconfig"));
        Assert.IsTrue(workspace.TryApplyChanges(solution));
        Project project = workspace.CurrentSolution.GetProject(projectId)
            ?? throw new InvalidOperationException("Could not create the test project.");
        Compilation compilation = await project.GetCompilationAsync()
            ?? throw new InvalidOperationException("Could not create the test compilation.");
        Assert.IsFalse(compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers(
                [new MakeConstAnalyzer(), new NullForgivingOperatorAnalyzer()],
                project.AnalyzerOptions)
            .GetAnalyzerDiagnosticsAsync();

        string[] expectedIds = ruleIds
            .Where(id => id != ruleId || severity != "none")
            .ToArray();
        Assert.AreEqual(expectedIds.Length, diagnostics.Length);
        foreach (string expectedId in expectedIds)
        {
            Diagnostic diagnostic = Assert.ContainsSingle(diagnostics.Where(item => item.Id == expectedId));
            DiagnosticSeverity expectedSeverity = expectedId == ruleId && severity == "error"
                ? DiagnosticSeverity.Error
                : DiagnosticSeverity.Warning;
            Assert.AreEqual(expectedSeverity, diagnostic.Severity);

            string construct = expectedId switch
            {
                "WR0001" => "int answer = 42;",
                "WR0002" => "name!",
                "WR0003" => "null!",
                "WR0004" => "Name!",
                _ => throw new InvalidOperationException($"Unexpected diagnostic ID '{expectedId}'."),
            };
            var expectedSpan = new TextSpan(source.IndexOf(construct, StringComparison.Ordinal), construct.Length);
            Assert.IsTrue(diagnostic.Location.SourceSpan.Length > 0);
            Assert.IsTrue(expectedSpan.Contains(diagnostic.Location.SourceSpan));
            Assert.AreEqual(compilation.SyntaxTrees.Single(), diagnostic.Location.SourceTree);
            if (expectedId == "WR0001")
            {
                StringAssert.Contains(diagnostic.GetMessage(), "answer");
            }
        }
    }
}
