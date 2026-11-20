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
    [DataRow("int", "default(int)!")]
    [DataRow("int", "default!")]
    [DataRow("System.Guid", "default!")]
    [DataRow("int?", "default(int?)!")]
    public async Task ForgivingValueTypeDefault_ReportsGeneralRule(string type, string initializer)
    {
        string source = $$"""
            class Example
            {
                public {{type}} Value => {{initializer}};
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual(NullForgivingOperatorAnalyzer.NullForgivingOperatorDiagnosticId, diagnostics[0].Id);
    }

    [TestMethod]
    [DataRow("where T : struct", "WR0002")]
    [DataRow("where T : notnull", "WR0002")]
    [DataRow("", "WR0002")]
    [DataRow("where T : class", "WR0003")]
    public async Task ForgivingGenericDefault_UsesTypeConstraints(string constraint, string expectedId)
    {
        string source = $$"""
            class Example<T> {{constraint}}
            {
                public T Value => default(T)!;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual(expectedId, diagnostics[0].Id);
    }

    [TestMethod]
    [DataRow(XunitFramework, "Xunit.Fact")]
    [DataRow(TUnitFramework, "TUnit.Core.Test")]
    public async Task ForgivingValueTypeDefaultInTestClass_ReportsGeneralRule(string framework, string testAttribute)
    {
        string source = $$"""
            class PersonTests
            {
                [{{testAttribute}}]
                public async System.Threading.Tasks.Task HasCount()
                {
                    await System.Threading.Tasks.Task.Yield();
                    int count = default(int)!;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source, framework);

        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual(NullForgivingOperatorAnalyzer.NullForgivingOperatorDiagnosticId, diagnostics[0].Id);
    }

    private const string MSTestFramework = """
        namespace Microsoft.VisualStudio.TestTools.UnitTesting
        {
            public class TestMethodAttribute : System.Attribute { }
            public class DataTestMethodAttribute : TestMethodAttribute { }
            public static class Assert { }
        }
        """;

    private const string XunitFramework = """
        namespace Xunit
        {
            public class FactAttribute : System.Attribute { }
            public class TheoryAttribute : FactAttribute { }
            public static class Assert
            {
                public static T Throws<T>(System.Action action) where T : System.Exception =>
                    throw new System.NotSupportedException();
            }
        }
        """;

    private const string NUnitFramework = """
        namespace NUnit.Framework
        {
            public class TestAttribute : System.Attribute { }
            public class TestCaseAttribute(params object[] arguments) : System.Attribute { }
            public class TestCaseAttribute<T>(T argument) : TestCaseAttribute(argument) { }
            public class TestCaseSourceAttribute(string sourceName) : System.Attribute { }
            public class TheoryAttribute : System.Attribute { }
            public static class Assert { }
        }
        """;

    private const string TUnitCore = """
        namespace TUnit.Core
        {
            public sealed class TestAttribute : System.Attribute { }
        }
        """;

    private const string TUnitFramework = TUnitCore + """

        namespace TUnit.Assertions
        {
            public static class Assert { }
        }
        """;

    [TestMethod]
    [DataRow(MSTestFramework, "Microsoft.VisualStudio.TestTools.UnitTesting", "TestMethod", "Assert.IsNotNull(Name)")]
    [DataRow(MSTestFramework, "Microsoft.VisualStudio.TestTools.UnitTesting", "DataTestMethod", "Assert.IsNotNull(Name)")]
    [DataRow(XunitFramework, "Xunit", "Fact", "Assert.NotNull(Name)")]
    [DataRow(XunitFramework, "Xunit", "Theory", "Assert.NotNull(Name)")]
    [DataRow(NUnitFramework, "NUnit.Framework", "Test", "Assert.That(Name, Is.Not.Null)")]
    [DataRow(NUnitFramework, "NUnit.Framework", "TestCase(\"name\")", "Assert.That(Name, Is.Not.Null)", "string name")]
    [DataRow(NUnitFramework, "NUnit.Framework", "TestCase<string>(\"name\")", "Assert.That(Name, Is.Not.Null)", "string name")]
    [DataRow(NUnitFramework, "NUnit.Framework", "TestCaseSource(nameof(Names))", "Assert.That(Name, Is.Not.Null)", "string name")]
    [DataRow(NUnitFramework, "NUnit.Framework", "Theory", "Assert.That(Name, Is.Not.Null)")]
    [DataRow(TUnitFramework, "TUnit.Core", "Test", "await Assert.That(Name).IsNotNull()")]
    public async Task ForgivingInTestClass_ReportsUseAssertion(
        string framework,
        string frameworkNamespace,
        string testAttribute,
        string expectedAssertion,
        string parameters = "")
    {
        string source = $$"""
            using {{frameworkNamespace}};

            class PersonTests
            {
                public string? Name { get; set; }
                public static string[] Names => ["name"];

                [{{testAttribute}}]
                public void NameIsSet({{parameters}}) { }

                private async System.Threading.Tasks.Task<int> NameLength()
                {
                    await System.Threading.Tasks.Task.Yield();
                    return Name!.Length;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source, framework);

        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual(NullForgivingOperatorAnalyzer.UseAssertionInTestsDiagnosticId, diagnostics[0].Id);
        StringAssert.Contains(diagnostics[0].GetMessage(), $"'{expectedAssertion}'");
    }

    [TestMethod]
    [DataRow(XunitFramework, "Xunit.Fact")]
    [DataRow(NUnitFramework, "NUnit.Framework.Test")]
    [DataRow(TUnitFramework, "TUnit.Core.Test")]
    public async Task ForgivingInDerivedTestClass_ReportsUseAssertion(string framework, string testAttribute)
    {
        string source = $$"""
            abstract class TestBase
            {
                [{{testAttribute}}]
                public void Runs() { }
            }

            class PersonTests : TestBase
            {
                public string? Name { get; set; }
                private async System.Threading.Tasks.Task<int> NameLength()
                {
                    await System.Threading.Tasks.Task.Yield();
                    return Name!.Length;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source, framework);

        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual(NullForgivingOperatorAnalyzer.UseAssertionInTestsDiagnosticId, diagnostics[0].Id);
    }

    [TestMethod]
    [DataRow(XunitFramework)]
    [DataRow(NUnitFramework)]
    [DataRow(TUnitFramework)]
    public async Task ForgivingOutsideTestClassWithFrameworkReferenced_ReportsNullForgivingOperator(string framework)
    {
        const string source = """
            class Person
            {
                public string? Name { get; set; }
                public int NameLength() => Name!.Length;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source, framework);

        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual(NullForgivingOperatorAnalyzer.NullForgivingOperatorDiagnosticId, diagnostics[0].Id);
    }

    [TestMethod]
    [DataRow(XunitFramework, "Xunit.Fact")]
    [DataRow(NUnitFramework, "NUnit.Framework.Test")]
    [DataRow(TUnitFramework, "TUnit.Core.Test")]
    public async Task ForgivingWithFrameworkDefinedInSource_ReportsNullForgivingOperator(string framework, string testAttribute)
    {
        string source = $$"""
            {{framework}}

            class PersonTests
            {
                public string? Name { get; set; }

                [{{testAttribute}}]
                public void NameIsSet() { }

                private async System.Threading.Tasks.Task<int> NameLength()
                {
                    await System.Threading.Tasks.Task.Yield();
                    return Name!.Length;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual(NullForgivingOperatorAnalyzer.NullForgivingOperatorDiagnosticId, diagnostics[0].Id);
    }

    [TestMethod]
    [DataRow(XunitFramework, "Xunit.Fact")]
    [DataRow(NUnitFramework, "NUnit.Framework.Test")]
    [DataRow(TUnitFramework, "TUnit.Core.Test")]
    public async Task ForgivingNullLiteralInTestClass_ReportsFabricatedNonNullValue(string framework, string testAttribute)
    {
        string source = $$"""
            class PersonTests
            {
                public string Name { get; set; } = null!;

                [{{testAttribute}}]
                public void NameIsSet() { }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source, framework);

        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual(NullForgivingOperatorAnalyzer.FabricatedNonNullValueDiagnosticId, diagnostics[0].Id);
    }

    [TestMethod]
    public async Task ForgivingInTUnitClassWithoutAssertions_ReportsNullForgivingOperator()
    {
        const string source = """
            class PersonTests
            {
                public string? Name { get; set; }

                [TUnit.Core.Test]
                public void NameIsSet() { }

                private int NameLength() => Name!.Length;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source, TUnitCore);

        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual(NullForgivingOperatorAnalyzer.NullForgivingOperatorDiagnosticId, diagnostics[0].Id);
    }

    [TestMethod]
    [DataRow("public PersonTests() { _ = GetValue()!; }")]
    [DataRow("private string value = GetValue()!;")]
    [DataRow("public string Value { get; } = GetValue()!;")]
    [DataRow("public string Value => GetValue()!;")]
    [DataRow("private string Helper() => GetValue()!;")]
    [DataRow("private async Task Helper() { await Task.Yield(); lock (this) { _ = GetValue()!; } }")]
    [DataRow("private async Task Helper() { await Task.Yield(); try { } catch (Exception) when (GetValue()!.Length > 0) { } }")]
    [DataRow("private async Task Helper() { await Task.Yield(); string Local() => GetValue()!; }")]
    [DataRow("private async Task Helper() { await Task.Yield(); Func<string> read = () => GetValue()!; }")]
    public async Task ForgivingInTUnitNonAwaitableContext_ReportsGeneralRule(string member)
    {
        string source = $$"""
            using System;
            using System.Threading.Tasks;

            class PersonTests
            {
                [TUnit.Core.Test]
                public void NameIsSet() { }

                private static string? GetValue() => null;
                {{member}}
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source, TUnitFramework);

        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual(NullForgivingOperatorAnalyzer.NullForgivingOperatorDiagnosticId, diagnostics[0].Id);
    }

    [TestMethod]
    public async Task ForgivingInTUnitAsyncLambdaInsideConstructor_ReportsUseAssertion()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;

            class PersonTests
            {
                [TUnit.Core.Test]
                public void NameIsSet() { }

                public PersonTests()
                {
                    Func<Task<int>> read = async () =>
                    {
                        await Task.Yield();
                        string? name = GetValue();
                        return name!.Length;
                    };
                }

                private static string? GetValue() => null;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source, TUnitFramework);

        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual(NullForgivingOperatorAnalyzer.UseAssertionInTestsDiagnosticId, diagnostics[0].Id);
    }

    [TestMethod]
    [DataRow("unsafe class PersonTests", "}")]
    [DataRow("unsafe class Container { class PersonTests", "}}")]
    public async Task ForgivingInTUnitUnsafeType_ReportsGeneralRule(string declaration, string closingBraces)
    {
        string source = $$"""
            using System.Threading.Tasks;

            {{declaration}}
            {
                [TUnit.Core.Test]
                public async Task NameIsSet()
                {
                    string? name = GetValue();
                    _ = name!.Length;
                }

                private static string? GetValue() => null;
            {{closingBraces}}
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source, TUnitFramework, allowUnsafe: true);

        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual(NullForgivingOperatorAnalyzer.NullForgivingOperatorDiagnosticId, diagnostics[0].Id);
    }

    [TestMethod]
    public async Task IntentionalNullArgument_PragmaSuppressesOnlyScopedUse()
    {
        const string source = """
            using System;
            using Xunit;

            class PersonTests
            {
                [Fact]
                public void ConstructorRejectsNull()
                {
            #pragma warning disable WR0003 // Null input intentionally exercises argument validation.
                    Assert.Throws<ArgumentNullException>(() => new Person(null!));
            #pragma warning restore WR0003
                    Assert.Throws<ArgumentNullException>(() => new Person(null!));
                }

                private sealed class Person
                {
                    public Person(string name) => ArgumentNullException.ThrowIfNull(name);
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source, XunitFramework);

        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual(NullForgivingOperatorAnalyzer.FabricatedNonNullValueDiagnosticId, diagnostics[0].Id);
        Assert.AreEqual(source.LastIndexOf("null!", StringComparison.Ordinal), diagnostics[0].Location.SourceSpan.Start);
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

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
        string source,
        string? referencedFrameworkSource = null,
        bool allowUnsafe = false)
    {
        string trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("The runtime did not provide trusted platform assemblies.");
        string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new InvalidOperationException("Could not locate the runtime directory.");
        List<MetadataReference> references = trustedAssemblies.Split(Path.PathSeparator)
            .Where(path => Path.GetDirectoryName(path) == runtimeDirectory)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable,
            allowUnsafe: allowUnsafe);

        if (referencedFrameworkSource is not null)
        {
            CSharpCompilation framework = CSharpCompilation.Create(
                "TestFramework",
                [CSharpSyntaxTree.ParseText(referencedFrameworkSource, parseOptions)],
                references,
                compilationOptions);
            Assert.IsFalse(framework.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            references.Add(framework.ToMetadataReference());
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestProject",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            compilationOptions);
        Assert.IsFalse(compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        return await compilation
            .WithAnalyzers([new NullForgivingOperatorAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();
    }
}
