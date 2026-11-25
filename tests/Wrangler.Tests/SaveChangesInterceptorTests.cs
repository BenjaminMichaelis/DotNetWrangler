using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Wrangler.Analyzers;

namespace Wrangler.Tests;

[TestClass]
public sealed class SaveChangesInterceptorTests
{
    [TestMethod]
    [DataRow("SavingChanges", false)]
    [DataRow("SavingChanges", true)]
    [DataRow("SavedChanges", false)]
    [DataRow("SavedChanges", true)]
    [DataRow("SaveChangesFailed", false)]
    [DataRow("SaveChangesFailed", true)]
    [DataRow("SaveChangesCanceled", false)]
    [DataRow("SaveChangesCanceled", true)]
    [DataRow("ThrowingConcurrencyException", false)]
    [DataRow("ThrowingConcurrencyException", true)]
    public async Task OneCallback_ReportsMissingCounterpart(string name, bool async)
    {
        string callback = name + (async ? "Async" : "");
        string source = SaveChangesTestHelper.Source($"class Custom : SaveChangesInterceptor {{ {SaveChangesTestHelper.Method(callback)} }}");
        Diagnostic diagnostic = Assert.ContainsSingle(await SaveChangesTestHelper.Analyze(source));
        Assert.AreEqual(SaveChangesInterceptorAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.AreEqual($"Callback '{callback}' is implemented without its counterpart '{name + (async ? "" : "Async")}'", diagnostic.GetMessage());
        Assert.AreEqual(callback, source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }

    [TestMethod]
    [DataRow("SavingChanges")]
    [DataRow("SavedChanges")]
    [DataRow("SaveChangesFailed")]
    [DataRow("SaveChangesCanceled")]
    [DataRow("ThrowingConcurrencyException")]
    public async Task BothOrNeitherCallbacks_DoNotReport(string name)
    {
        string source = SaveChangesTestHelper.Source($$"""
            class Both : SaveChangesInterceptor { {{SaveChangesTestHelper.Method(name)}} {{SaveChangesTestHelper.Method(name + "Async")}} }
            class Neither : SaveChangesInterceptor { }
            class Control : SaveChangesInterceptor { {{SaveChangesTestHelper.Method(name)}} }
            """);
        Assert.HasCount(1, await SaveChangesTestHelper.Analyze(source));
    }

    [TestMethod]
    public async Task Inheritance_UsesCustomCallbacksWithoutRepeatingInheritedDiagnostics()
    {
        string source = SaveChangesTestHelper.Source($$"""
            class Base : SaveChangesInterceptor { {{SaveChangesTestHelper.Method("SavingChanges")}} }
            class InheritedOnly : Base { }
            class Completed : Base { {{SaveChangesTestHelper.Method("SavingChangesAsync")}} }
            class InheritedBoth : Completed { {{SaveChangesTestHelper.Method("SavingChanges")}} }
            class OverrideOne : Base { {{SaveChangesTestHelper.Method("SavingChanges")}} }
            """);
        ImmutableArray<Diagnostic> diagnostics = await SaveChangesTestHelper.Analyze(source);
        Assert.HasCount(2, diagnostics);
        Assert.AreEqual(source.IndexOf("SavingChanges", source.IndexOf("class Base", StringComparison.Ordinal), StringComparison.Ordinal), diagnostics[0].Location.SourceSpan.Start);
        Assert.AreEqual(source.IndexOf("SavingChanges", source.IndexOf("class OverrideOne", StringComparison.Ordinal), StringComparison.Ordinal), diagnostics[1].Location.SourceSpan.Start);
    }

    [TestMethod]
    public async Task ReferencedCustomBase_CountsAsImplementation()
    {
        MetadataReference reference = SaveChangesTestHelper.CreateReference(extraSource: SaveChangesTestHelper.Source($"class ExternalBase : SaveChangesInterceptor {{ {SaveChangesTestHelper.Method("SavingChanges")} }}").Replace("class ExternalBase", "public class ExternalBase", StringComparison.Ordinal));
        string source = SaveChangesTestHelper.Source($$"""
            class InheritedOnly : ExternalBase { }
            class Completed : ExternalBase { {{SaveChangesTestHelper.Method("SavingChangesAsync")}} }
            class Control : SaveChangesInterceptor { {{SaveChangesTestHelper.Method("SavingChangesAsync")}} }
            """);
        Diagnostic diagnostic = Assert.ContainsSingle(await SaveChangesTestHelper.Analyze(source, reference));
        Assert.AreEqual(source.IndexOf("SavingChangesAsync", source.IndexOf("class Control", StringComparison.Ordinal), StringComparison.Ordinal), diagnostic.Location.SourceSpan.Start);
    }

    [TestMethod]
    public async Task ExplicitAndPartialImplementations_AreMatchedSemantically()
    {
        string source = SaveChangesTestHelper.Source($$"""
            class ExplicitOne : ISaveChangesInterceptor { {{SaveChangesTestHelper.Method("SavingChanges", explicitImplementation: true)}} }
            partial class PartialBoth : SaveChangesInterceptor { {{SaveChangesTestHelper.Method("SavingChanges")}} }
            partial class PartialBoth { {{SaveChangesTestHelper.Method("SavingChangesAsync")}} }
            class ExplicitBoth : ISaveChangesInterceptor
            {
                {{SaveChangesTestHelper.Method("SavingChanges", explicitImplementation: true)}}
                {{SaveChangesTestHelper.Method("SavingChangesAsync", explicitImplementation: true)}}
            }
            """);
        Diagnostic diagnostic = Assert.ContainsSingle(await SaveChangesTestHelper.Analyze(source));
        Assert.AreEqual(source.IndexOf("SavingChanges", source.IndexOf("class ExplicitOne", StringComparison.Ordinal), StringComparison.Ordinal), diagnostic.Location.SourceSpan.Start);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task PartialCallbackMethods_UseBodyBearingImplementation(bool async, bool paired)
    {
        string callback = "SavingChanges" + (async ? "Async" : "");
        string partialCallback = PartialCallback(callback);
        string source = SaveChangesTestHelper.Source($$"""
            partial class Custom : SaveChangesInterceptor
            {
                {{partialCallback}}
                {{(paired ? PartialCallback("SavingChanges" + (async ? "" : "Async")) : "")}}
            }
            class Control : SaveChangesInterceptor { {{SaveChangesTestHelper.Method("SavedChanges")}} }
            """);
        ImmutableArray<Diagnostic> diagnostics = await SaveChangesTestHelper.Analyze(source);
        Assert.HasCount(paired ? 1 : 2, diagnostics);
        if (!paired)
        {
            Diagnostic diagnostic = diagnostics[0];
            Assert.AreEqual(SaveChangesInterceptorAnalyzer.DiagnosticId, diagnostic.Id);
            Assert.AreEqual($"Callback '{callback}' is implemented without its counterpart '{"SavingChanges" + (async ? "" : "Async")}'", diagnostic.GetMessage());
            int implementationOffset = source.IndexOf(partialCallback, StringComparison.Ordinal)
                + partialCallback.LastIndexOf(callback, StringComparison.Ordinal);
            Assert.AreEqual(implementationOffset, diagnostic.Location.SourceSpan.Start);
            Assert.AreEqual(callback.Length, diagnostic.Location.SourceSpan.Length);
        }

        static string PartialCallback(string name)
        {
            string implementation = SaveChangesTestHelper.Method(name).Replace("public override ", "public override partial ", StringComparison.Ordinal);
            string definition = implementation[..implementation.IndexOf(" =>", StringComparison.Ordinal)] + ";";
            return definition + "\n" + implementation;
        }
    }

    [TestMethod]
    public async Task LookalikeOverloads_DoNotCountAsCallbacksOrCounterparts()
    {
        string source = SaveChangesTestHelper.Source($$"""
            class Lookalike : SaveChangesInterceptor { public int SavingChanges(string value) => 0; }
            class Unrelated { public int SavingChanges(DbContextEventData data, InterceptionResult<int> result) => 0; }
            class WrongCounterpart : SaveChangesInterceptor
            {
                {{SaveChangesTestHelper.Method("SavingChanges")}}
                public Task SavingChangesAsync(string value) => Task.CompletedTask;
            }
            """);
        Assert.HasCount(1, await SaveChangesTestHelper.Analyze(source));
    }

    [TestMethod]
    [DataRow("throw new System.InvalidOperationException();")]
    [DataRow("return base.SavingChanges(data, result);")]
    public async Task CallbackBodies_DoNotChangeStructuralPolicy(string body)
    {
        string source = SaveChangesTestHelper.Source($"class Custom : SaveChangesInterceptor {{ public override InterceptionResult<int> SavingChanges(DbContextEventData data, InterceptionResult<int> result) {{ {body} }} }}");
        Assert.HasCount(1, await SaveChangesTestHelper.Analyze(source));
    }

    [TestMethod]
    public async Task Pragma_SuppressesOnlyScopedCallbacksAndRestoresWarning()
    {
        string source = SaveChangesTestHelper.Source($$"""
            class Custom : SaveChangesInterceptor
            {
            #pragma warning disable WRANGLER0006
                {{SaveChangesTestHelper.Method("SavingChanges")}}
            #pragma warning restore WRANGLER0006
                {{SaveChangesTestHelper.Method("SavedChanges")}}
            }
            """);
        Diagnostic diagnostic = Assert.ContainsSingle(await SaveChangesTestHelper.Analyze(source));
        Assert.AreEqual("SavedChanges", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }

    [TestMethod]
    public async Task GeneratedCode_IsSkipped()
    {
        string source = SaveChangesTestHelper.Source($"class Custom : SaveChangesInterceptor {{ {SaveChangesTestHelper.Method("SavingChanges")} }}");
        Assert.IsEmpty(await SaveChangesTestHelper.Analyze("// <auto-generated/>\n" + source));
        Assert.HasCount(1, await SaveChangesTestHelper.Analyze(source));
    }

    [TestMethod]
    public async Task PartialFilesAndGeneratedDeclarations_AreHandledWithoutDuplicates()
    {
        string source = SaveChangesTestHelper.Source($$"""
            partial class PartialBoth : SaveChangesInterceptor { {{SaveChangesTestHelper.Method("SavingChanges")}} }
            partial class GeneratedOnly : SaveChangesInterceptor { }
            class Control : SaveChangesInterceptor { {{SaveChangesTestHelper.Method("SavedChanges")}} }
            """);
        string generated = "// <auto-generated/>\n" + SaveChangesTestHelper.Source($$"""
            partial class PartialBoth { {{SaveChangesTestHelper.Method("SavingChangesAsync")}} }
            partial class GeneratedOnly { {{SaveChangesTestHelper.Method("SavingChanges")}} }
            """);
        Diagnostic diagnostic = Assert.ContainsSingle(await SaveChangesTestHelper.Analyze(source, SaveChangesTestHelper.Reference, additionalSource: generated));
        Assert.AreEqual("SavedChanges", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }

    [TestMethod]
    public async Task MissingAndSourceDefinedFrameworks_AreSkipped()
    {
        Assert.IsEmpty(await SaveChangesTestHelper.Analyze("class Unrelated { public void SavingChanges() {} }", reference: null));
        string lookalike = SaveChangesTestHelper.Source($"class Custom : SaveChangesInterceptor {{ {SaveChangesTestHelper.Method("SavingChanges")} }}") + SaveChangesTestHelper.ApiSource();
        Assert.IsEmpty(await SaveChangesTestHelper.Analyze(lookalike, reference: null));
        string source = SaveChangesTestHelper.Source($"class Custom : SaveChangesInterceptor {{ {SaveChangesTestHelper.Method("SavingChanges")} }}");
        Assert.IsEmpty(await SaveChangesTestHelper.Analyze(source, SaveChangesTestHelper.CreateReference(assemblyName: "Lookalike")));
        Assert.HasCount(1, await SaveChangesTestHelper.Analyze(source));
    }

    [TestMethod]
    public async Task OlderApi_OnlyChecksPresentPairs()
    {
        string source = SaveChangesTestHelper.Source($"class Custom : SaveChangesInterceptor {{ {SaveChangesTestHelper.Method("SavingChanges")} }}");
        Assert.IsEmpty(await SaveChangesTestHelper.Analyze(source, SaveChangesTestHelper.CreateReference(omit: "SavingChangesAsync")));
        Assert.HasCount(1, await SaveChangesTestHelper.Analyze(source));
    }

    [TestMethod]
    public async Task AbstractAndIncompleteDeclarations_AreNotImplementations()
    {
        string abstractSource = SaveChangesTestHelper.Source($$"""
            abstract class Abstract : SaveChangesInterceptor
            { public abstract override InterceptionResult<int> SavingChanges(DbContextEventData data, InterceptionResult<int> result); }
            class Control : SaveChangesInterceptor { {{SaveChangesTestHelper.Method("SavingChanges")}} }
            """);
        Assert.HasCount(1, await SaveChangesTestHelper.Analyze(abstractSource));
        string incomplete = SaveChangesTestHelper.Source("class Incomplete : SaveChangesInterceptor { public override InterceptionResult<int> SavingChanges(DbContextEventData data, InterceptionResult<int> result) { return default( ; }");
        Assert.IsEmpty(await SaveChangesTestHelper.Analyze(incomplete, SaveChangesTestHelper.Reference, allowCompilerErrors: true));
    }

    [TestMethod]
    public async Task DefaultConfiguration_DoesNotEnablePolicy()
    {
        string source = SaveChangesTestHelper.Source($"class Custom : SaveChangesInterceptor {{ {SaveChangesTestHelper.Method("SavingChanges")} }}");
        Assert.IsEmpty(await SaveChangesTestHelper.Analyze(source, enabled: false));
        Assert.HasCount(1, await SaveChangesTestHelper.Analyze(source));
    }
}

internal static class SaveChangesTestHelper
{
    internal static readonly ImmutableArray<MetadataReference> RuntimeReferences = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
        ?? throw new InvalidOperationException("Missing runtime references."))
        .Split(Path.PathSeparator)
        .Where(path => Path.GetDirectoryName(path) == Path.GetDirectoryName(typeof(object).Assembly.Location))
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();

    private static readonly string[] Callbacks = ["SavingChanges", "SavedChanges", "SaveChangesFailed", "SaveChangesCanceled", "ThrowingConcurrencyException"];

    internal static readonly MetadataReference Reference = CreateReference();

    internal static string Source(string classes) => """
        using Microsoft.EntityFrameworkCore.Diagnostics;
        using System.Threading;
        using System.Threading.Tasks;
        """ + "\n" + classes;

    internal static string Method(string name, bool explicitImplementation = false, bool api = false)
    {
        bool async = name.EndsWith("Async", StringComparison.Ordinal);
        string syncName = async ? name[..^5] : name;
        string eventType = syncName switch
        {
            "SavedChanges" => "SaveChangesCompletedEventData",
            "SaveChangesFailed" => "DbContextErrorEventData",
            "ThrowingConcurrencyException" => "ConcurrencyExceptionEventData",
            _ => "DbContextEventData",
        };
        string result = syncName switch
        {
            "SavingChanges" => "InterceptionResult<int>",
            "SavedChanges" => "int",
            "ThrowingConcurrencyException" => "InterceptionResult",
            _ => "void",
        };
        string returnType = async ? (result == "void" ? "Task" : $"ValueTask<{result}>") : result;
        string parameters = $"{eventType} data" + (result == "void" ? "" : $", {result} result")
            + (async ? ", CancellationToken cancellationToken = default" : "");
        if (explicitImplementation)
        {
            parameters = parameters.Replace(" = default", "", StringComparison.Ordinal);
        }
        string prefix = explicitImplementation ? "ISaveChangesInterceptor." : "";
        string modifiers = explicitImplementation ? "" : api ? "public virtual " : "public override ";
        string body = result == "void" && !async ? "{}" : async && result == "void" ? "=> Task.CompletedTask;" : "=> default;";
        return $"{modifiers}{returnType} {prefix}{name}({parameters}) {body}";
    }

    internal static string ApiSource(string? omit = null)
    {
        string[] methods = Callbacks.SelectMany(name => new[] { name, name + "Async" }).Where(name => name != omit).ToArray();
        string interfaceMethods = string.Join("\n", methods.Select(name => Method(name, api: true).Replace("public virtual ", "", StringComparison.Ordinal)));
        string defaults = string.Join("\n", methods.Select(name => Method(name, api: true)));
        return $$"""
            namespace Microsoft.EntityFrameworkCore.Diagnostics
            {
                using System.Threading;
                using System.Threading.Tasks;
                public class DbContextEventData {}
                public class SaveChangesCompletedEventData : DbContextEventData {}
                public class DbContextErrorEventData : DbContextEventData {}
                public class ConcurrencyExceptionEventData : DbContextEventData {}
                public struct InterceptionResult<T> {}
                public struct InterceptionResult {}
                public interface ISaveChangesInterceptor { {{interfaceMethods}} }
                public abstract class SaveChangesInterceptor : ISaveChangesInterceptor { {{defaults}} }
            }
            """;
    }

    internal static MetadataReference CreateReference(string? omit = null, string assemblyName = "Microsoft.EntityFrameworkCore", string? extraSource = null)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(assemblyName,
            [CSharpSyntaxTree.ParseText((extraSource ?? "") + "\n" + ApiSource(omit), new CSharpParseOptions(LanguageVersion.CSharp12))], RuntimeReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    internal static Task<ImmutableArray<Diagnostic>> Analyze(string source, bool enabled = true) => Analyze(source, Reference, enabled: enabled);

    internal static async Task<ImmutableArray<Diagnostic>> Analyze(string source, MetadataReference? reference,
        bool allowCompilerErrors = false, bool enabled = true, string? additionalSource = null)
    {
                IEnumerable<SyntaxTree> trees = [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), path: "Test.cs")];
                if (additionalSource is not null)
                {
                    trees = trees.Append(CSharpSyntaxTree.ParseText(additionalSource, new CSharpParseOptions(LanguageVersion.CSharp12), path: "Generated.g.cs"));
                }
                CSharpCompilation compilation = CSharpCompilation.Create("Tests", trees,
            reference is null ? RuntimeReferences : RuntimeReferences.Add(reference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithSpecificDiagnosticOptions(enabled
                    ? ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(SaveChangesInterceptorAnalyzer.DiagnosticId, ReportDiagnostic.Warn)
                    : ImmutableDictionary<string, ReportDiagnostic>.Empty));
        if (!allowCompilerErrors)
        {
            Assert.DoesNotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error, compilation.GetDiagnostics(),
                string.Join("\n", compilation.GetDiagnostics()));
        }
        return (await compilation.WithAnalyzers([new SaveChangesInterceptorAnalyzer()]).GetAnalyzerDiagnosticsAsync())
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ToImmutableArray();
    }
}
