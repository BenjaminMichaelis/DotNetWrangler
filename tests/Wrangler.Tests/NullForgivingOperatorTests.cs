using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Wrangler.Analyzers;

namespace Wrangler.Tests;

[TestClass]
public sealed class NullForgivingOperatorTests
{
    [TestMethod]
    public async Task ForgivingPossiblyNullExpression_ReportsNullForgivingOperator()
    {
        const string source = """
            class Person
            {
                public string? Name { get; set; }
                public int NameLength() => Name!.Length;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual(NullForgivingOperatorAnalyzer.NullForgivingOperatorDiagnosticId, diagnostics[0].Id);
        StringAssert.Contains(diagnostics[0].GetMessage(), "'Name'");
    }

    [TestMethod]
    [DataRow("null!")]
    [DataRow("default!")]
    [DataRow("default(string)!")]
    [DataRow("(null)!")]
    public async Task ForgivingNullLiteral_ReportsFabricatedNonNullValue(string initializer)
    {
        string source = $$"""
            class Person
            {
                public string FirstName { get; set; } = {{initializer}};
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual(NullForgivingOperatorAnalyzer.FabricatedNonNullValueDiagnosticId, diagnostics[0].Id);
    }

    [TestMethod]
    public async Task LogicalNotAndNullConditional_DoNotReportDiagnostic()
    {
        const string source = """
            class Person
            {
                public string? Name { get; set; }
                public bool HasNoName() => !(Name?.Length > 0);
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        Assert.AreEqual(0, diagnostics.Length);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source)
    {
        string trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("The runtime did not provide trusted platform assemblies.");
        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestProject",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            trustedAssemblies.Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        return await compilation
            .WithAnalyzers([new NullForgivingOperatorAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();
    }
}
