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
    [DataRow("WRANGLER0001", "none")]
    [DataRow("WRANGLER0001", "warning")]
    [DataRow("WRANGLER0001", "error")]
    [DataRow("WRANGLER0002", "none")]
    [DataRow("WRANGLER0002", "warning")]
    [DataRow("WRANGLER0002", "error")]
    [DataRow("WRANGLER0003", "none")]
    [DataRow("WRANGLER0003", "warning")]
    [DataRow("WRANGLER0003", "error")]
    [DataRow("WRANGLER0004", "none")]
    [DataRow("WRANGLER0004", "warning")]
    [DataRow("WRANGLER0004", "error")]
    [DataRow("WRANGLER0005", "none")]
    [DataRow("WRANGLER0005", "warning")]
    [DataRow("WRANGLER0005", "error")]
    [DataRow("WRANGLER0007", "none")]
    [DataRow("WRANGLER0007", "warning")]
    [DataRow("WRANGLER0007", "error")]
    public async Task EditorConfig_ConfiguresEachRuleIndependently(string ruleId, string severity)
    {
        const string source = """
            #pragma warning disable CONTOSO001

            class Person
            {
                public string FirstName { get; set; } = null!;

                public int NameLength(string? name) => name!.Length;

            }

            class SingletonPolicy : Microsoft.EntityFrameworkCore.Diagnostics.ISingletonInterceptor { }
            class Context : Microsoft.EntityFrameworkCore.DbContext
            {
                protected override void OnConfiguring(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder builder)
                    => builder.AddInterceptors(new SingletonPolicy());
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
        string[] ruleIds = ["WRANGLER0001", "WRANGLER0002", "WRANGLER0003", "WRANGLER0004", "WRANGLER0005", "WRANGLER0007"];
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
            .Append(MetadataReference.CreateFromFile(typeof(TestMethodAttribute).Assembly.Location))
            .Append(SingletonTestHelper.DependencyInjectionReference)
            .Append(SingletonTestHelper.Reference);

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
                parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
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
        Assert.DoesNotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error, compilation.GetDiagnostics());

        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers(
                [new NullForgivingOperatorAnalyzer(), new PragmaSuppressionAnalyzer(), new SingletonInterceptorAnalyzer()],
                project.AnalyzerOptions)
            .GetAnalyzerDiagnosticsAsync();

        string[] expectedIds = ruleIds
            .Where(id => id != ruleId || severity != "none")
            .ToArray();
        Assert.HasCount(expectedIds.Length, diagnostics);
        foreach (string expectedId in expectedIds)
        {
            Diagnostic diagnostic = Assert.ContainsSingle(item => item.Id == expectedId, diagnostics);
            DiagnosticSeverity expectedSeverity = expectedId == ruleId && severity == "error"
                ? DiagnosticSeverity.Error
                : DiagnosticSeverity.Warning;
            Assert.AreEqual(expectedSeverity, diagnostic.Severity);

            string construct = expectedId switch
            {
                "WRANGLER0001" => "name!",
                "WRANGLER0002" => "null!",
                "WRANGLER0003" => "Name!",
                "WRANGLER0004" or "WRANGLER0005" => "disable",
                "WRANGLER0007" => "new SingletonPolicy()",
                _ => throw new InvalidOperationException($"Unexpected diagnostic ID '{expectedId}'."),
            };
            var expectedSpan = new TextSpan(source.IndexOf(construct, StringComparison.Ordinal), construct.Length);
            Assert.IsTrue(diagnostic.Location.SourceSpan.Length > 0);
            Assert.IsTrue(expectedSpan.Contains(diagnostic.Location.SourceSpan));
            Assert.AreEqual(Assert.ContainsSingle(compilation.SyntaxTrees), diagnostic.Location.SourceTree);
        }
    }

    [TestMethod]
    [DataRow("none", "none", "valid")]
    [DataRow("warning", "none", "valid")]
    [DataRow("none", "warning", "valid")]
    [DataRow("warning", "warning", "valid")]
    [DataRow("none", "none", "invalid-mode")]
    [DataRow("warning", "none", "invalid-mode")]
    [DataRow("none", "warning", "invalid-mode")]
    [DataRow("warning", "warning", "invalid-mode")]
    [DataRow("none", "none", "invalid-catalog")]
    [DataRow("warning", "none", "invalid-catalog")]
    [DataRow("none", "warning", "invalid-catalog")]
    [DataRow("warning", "warning", "invalid-catalog")]
    public async Task PragmaPolicies_StayIndependentWhenConfigurationIsInvalid(
        string titleSeverity, string reasonSeverity, string configuration)
    {
        string editorConfig = $"""
            root = true
            [*.cs]
            dotnet_diagnostic.WRANGLER0004.severity = {titleSeverity}
            dotnet_diagnostic.WRANGLER0005.severity = {reasonSeverity}
            wrangler_pragma_title_validation = {(configuration == "invalid-mode" ? "unsupported" : "documented")}
            """;
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(
            "#pragma warning disable CONTOSO001", out Document document, editorConfig,
            catalog: configuration == "invalid-catalog" ? "Invalid catalog" : null);
        ImmutableArray<Diagnostic> diagnostics = await PragmaTestHelper.GetDiagnosticsAsync(document);
        Assert.HasCount(titleSeverity == "none" ? 0 : 1, diagnostics.Where(diagnostic => diagnostic.Id == "WRANGLER0004"));
        Assert.HasCount(reasonSeverity == "none" ? 0 : 1, diagnostics.Where(diagnostic => diagnostic.Id == "WRANGLER0005"));
        if (titleSeverity != "none" && configuration != "valid")
        {
            Diagnostic diagnostic = Assert.ContainsSingle(item => item.Id == "WRANGLER0004", diagnostics);
            StringAssert.Contains(diagnostic.GetMessage(), "Invalid");
        }
    }

    [TestMethod]
    [DataRow("Test.cs", 0)]
    [DataRow("Scoped.cs", 1)]
    public async Task PragmaModeAndSeverity_HonorFileSpecificEditorConfigSections(string fileName, int expectedCount)
    {
        string editorConfig = PragmaTestHelper.EnabledConfig + """

            wrangler_pragma_title_validation = documented

            [Scoped.cs]
            dotnet_diagnostic.WRANGLER0004.severity = error
            wrangler_pragma_title_validation = StRiCt
            """;
        using AdhocWorkspace workspace = PragmaTestHelper.CreateWorkspace(
            "#pragma warning disable CONTOSO001 // A custom title; Justification: Intentional.",
            out Document document, editorConfig, fileName: fileName);
        ImmutableArray<Diagnostic> diagnostics = await PragmaTestHelper.GetDiagnosticsAsync(document);
        Assert.HasCount(expectedCount, diagnostics);
        if (expectedCount > 0)
        {
            Assert.AreEqual("WRANGLER0004", diagnostics[0].Id);
            Assert.AreEqual(DiagnosticSeverity.Error, diagnostics[0].Severity);
            StringAssert.Contains(diagnostics[0].GetMessage(), "catalog entry");
        }
    }
}
