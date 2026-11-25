using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Wrangler.Analyzers;

namespace Wrangler.Tests;

[TestClass]
public sealed class SingletonInterceptorTests
{
    [TestMethod]
    [DataRow("new Fresh()", 1)]
    [DataRow("(IInterceptor)(new Fresh())", 1)]
    [DataRow("new Fresh(), new Ordinary(), new Derived()", 2)]
    [DataRow("new IInterceptor[] { new Fresh(), new Ordinary(), new Derived() }", 2)]
    [DataRow("new[] { new Fresh(), new Fresh() }", 2)]
    [DataRow("[new Fresh(), new Ordinary(), new Derived()]", 2)]
    [DataRow("(System.Collections.Generic.IEnumerable<IInterceptor>)new IInterceptor[] { new Fresh() }", 1)]
    public async Task OnConfiguring_DirectCreationsReportWithCreationSpans(string values, int count)
    {
        string source = SingletonTestHelper.Source(SingletonTestHelper.Context($"b.AddInterceptors({values});"));
        ImmutableArray<Diagnostic> diagnostics = await SingletonTestHelper.Analyze(source);
        Assert.HasCount(count, diagnostics);
        foreach (Diagnostic diagnostic in diagnostics)
        {
            string creation = source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);
            Assert.IsTrue(creation is "new Fresh()" or "new Derived()", creation);
            string type = creation == "new Fresh()" ? "Fresh" : "Derived";
            Assert.AreEqual("WRANGLER0007", diagnostic.Id);
            Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.AreEqual($"Reuse singleton interceptor '{type}' instead of allocating it during per-context options configuration", diagnostic.GetMessage());
        }
    }

    [TestMethod]
    [DataRow("b => b.AddInterceptors(new Fresh())", "")]
    [DataRow("(sp, b) => b.AddInterceptors(new Fresh())", "")]
    [DataRow("delegate(DbContextOptionsBuilder b) { b.AddInterceptors(new Fresh()); }", "")]
    [DataRow("b => b.EnableSensitiveDataLogging().AddInterceptors(new Fresh())", "")]
    [DataRow("b => b.UseSqlite(\"connection\").AddInterceptors(new Fresh())", "")]
    [DataRow("b => b.AddInterceptors(new Ordinary()).AddInterceptors(new Fresh())", "")]
    [DataRow("b => b.AddInterceptors(new Fresh())", ", optionsLifetime: ServiceLifetime.Transient")]
    [DataRow("b => b.AddInterceptors(new Fresh())", ", optionsLifetime: ServiceLifetime.Scoped, contextLifetime: ServiceLifetime.Transient")]
    [DataRow("b => b.AddInterceptors(new Fresh())", ", contextLifetime: ServiceLifetime.Scoped")]
    public async Task AddDbContext_CanonicalCallbacksAndRepeatedLifetimesReport(string callback, string lifetimes)
    {
        string source = SingletonTestHelper.Source($$"""
            class C : DbContext { }
            class Registration
            {
                void Configure(IServiceCollection services) => services.AddDbContext<C>({{callback}}{{lifetimes}});
            }
            """);
        AssertCreation(Assert.ContainsSingle(await SingletonTestHelper.Analyze(source)), source, "new Fresh()");
    }

    [TestMethod]
    public async Task GenericContextsAndServiceOverloads_UseCanonicalMethods()
    {
        string source = SingletonTestHelper.Source("""
            interface IContext { }
            class C<T> : DbContext, IContext
            {
                protected override void OnConfiguring(DbContextOptionsBuilder b)
                    => b.EnableSensitiveDataLogging().AddInterceptors(new Fresh());
            }
            class Registration
            {
                void Configure(IServiceCollection services)
                {
                    services.AddDbContext<IContext, C<int>>((sp, b) => b.AddInterceptors(new Derived()));
                    services.AddDbContext<IContext, C<string>>(b => b.AddInterceptors(new Fresh()));
                    EntityFrameworkServiceCollectionExtensions.AddDbContext<C<int>>(
                        optionsLifetime: ServiceLifetime.Transient, serviceCollection: services,
                        optionsAction: b => b.AddInterceptors(new Fresh()));
                    services.AddDbContext<C<int>>(b => ((DbContextOptionsBuilder<C<int>>)b)
                        .EnableSensitiveDataLogging().AddInterceptors(new Fresh()));
                }
            }
            """);
        ImmutableArray<Diagnostic> diagnostics = await SingletonTestHelper.Analyze(source);
        Assert.HasCount(5, diagnostics);
        foreach (Diagnostic diagnostic in diagnostics)
        {
            AssertCreation(diagnostic, source, diagnostic.GetMessage().Contains("'Derived'", StringComparison.Ordinal) ? "new Derived()" : "new Fresh()");
        }
    }

    [TestMethod]
    [DataRow("b.AddInterceptors(new Ordinary());")]
    [DataRow("var local = new Fresh(); b.AddInterceptors(local);")]
    [DataRow("b.AddInterceptors(Cached, Property, Factory());")]
    [DataRow("new DbContextOptionsBuilder().AddInterceptors(new Fresh());")]
    [DataRow("var other = new DbContextOptionsBuilder(); other.AddInterceptors(new Fresh());")]
    [DataRow("System.Action later = () => b.AddInterceptors(new Fresh()); later();")]
    [DataRow("void Later() => b.AddInterceptors(new Fresh()); Later();")]
    [DataRow("System.Func<Fresh> factory = () => new Fresh(); b.AddInterceptors(factory());")]
    [DataRow("b.CustomBuilder().AddInterceptors(new Fresh());")]
    [DataRow("Other.AddInterceptors(new Fresh());")]
    [DataRow("b.AddInterceptors([.. new[] { new Fresh() }]);")]
    public async Task NonDirectOrUnrelatedRegistrations_AreExcludedWithPositiveControl(string body)
    {
        string source = SingletonTestHelper.Source(SingletonTestHelper.Context(body + "\nb.AddInterceptors(new Derived());") + """
            static class Other { public static void AddInterceptors(IInterceptor value) { } }
            static class CustomExtensions
            {
                public static DbContextOptionsBuilder CustomBuilder(this DbContextOptionsBuilder b) => new();
            }
            """);
        AssertCreation(Assert.ContainsSingle(await SingletonTestHelper.Analyze(source)), source, "new Derived()");
    }

    [TestMethod]
    [DataRow("services.AddDbContext<C>(b => b.AddInterceptors(new Fresh()), optionsLifetime: ServiceLifetime.Singleton);")]
    [DataRow("services.AddDbContext<C>(b => b.AddInterceptors(new Fresh()), contextLifetime: ServiceLifetime.Singleton);")]
    [DataRow("services.AddDbContext<C>(b => b.AddInterceptors(new Fresh()), contextLifetime: ServiceLifetime.Singleton, optionsLifetime: ServiceLifetime.Transient);")]
    [DataRow("services.AddDbContext<C>((sp, b) => b.AddInterceptors(new Fresh()), optionsLifetime: unknown);")]
    [DataRow("services.AddDbContext<C>(b => b.AddInterceptors(new Fresh()), contextLifetime: unknown);")]
    [DataRow("services.AddDbContextPool<C>(b => b.AddInterceptors(new Fresh()));")]
    [DataRow("services.AddDbContextFactory<C>(b => b.AddInterceptors(new Fresh()));")]
    [DataRow("services.AddDbContext<C>(Options);")]
    [DataRow("services.AddDbContext<C>(Wrap(b => b.AddInterceptors(new Fresh())));")]
    [DataRow("services.AddDbContext<C>(b => { System.Action deferred = () => b.AddInterceptors(new Fresh()); });")]
    [DataRow("services.AddDbContext<C>(b => { void Deferred() => b.AddInterceptors(new Fresh()); });")]
    [DataRow("services.AddDbContext<C>(b => new DbContextOptionsBuilder().AddInterceptors(new Fresh()));")]
    [DataRow("services.AddDbContext<C>(b => Unknown(b, other => other.AddInterceptors(new Fresh())));")]
    [DataRow("new DbContextOptionsBuilder().AddInterceptors(new Fresh());")]
    public async Task OutsideKnownRepeatedCallbacks_IsExcludedWithPositiveControl(string registration)
    {
        string source = SingletonTestHelper.Source($$"""
            class C : DbContext { }
            class Registration
            {
                void Configure(IServiceCollection services, ServiceLifetime unknown)
                {
                    {{registration}}
                    services.AddDbContext<C>(b => b.AddInterceptors(new Derived()));
                }
                static void Options(DbContextOptionsBuilder b) => b.AddInterceptors(new Fresh());
                static System.Action<DbContextOptionsBuilder> Wrap(System.Action<DbContextOptionsBuilder> action) => action;
                static void Unknown(DbContextOptionsBuilder b, System.Action<DbContextOptionsBuilder> action) { }
            }
            """);
        AssertCreation(Assert.ContainsSingle(await SingletonTestHelper.Analyze(source)), source, "new Derived()");
    }

    [TestMethod]
    [DataRow("b = new DbContextOptionsBuilder();")]
    [DataRow("Replace(ref b);")]
    [DataRow("(b, _) = (new DbContextOptionsBuilder(), 0);")]
    public async Task WrittenConfigurationParameters_AreConservativelyExcluded(string write)
    {
        string source = SingletonTestHelper.Source($$"""
            class WrittenContext : DbContext
            {
                protected override void OnConfiguring(DbContextOptionsBuilder b)
                {
                    {{write}}
                    b.AddInterceptors(new Fresh());
                }
                static void Replace(ref DbContextOptionsBuilder b) => b = new();
            }
            class Registration
            {
                void Configure(IServiceCollection services)
                {
                    services.AddDbContext<WrittenContext>(b =>
                    {
                        b = new DbContextOptionsBuilder();
                        b.AddInterceptors(new Fresh());
                    });
                }
            }
            """ + SingletonTestHelper.Context("b.AddInterceptors(new Derived());"));
        AssertCreation(Assert.ContainsSingle(await SingletonTestHelper.Analyze(source)), source, "new Derived()");
    }

    [TestMethod]
    [DataRow("ref DbContextOptionsBuilder alias = ref b; alias = new DbContextOptionsBuilder();")]
    [DataRow("ref DbContextOptionsBuilder alias = ref b; _ = alias;")]
    [DataRow("var other = new DbContextOptionsBuilder(); ref var alias = ref other; alias = ref b; alias = new DbContextOptionsBuilder();")]
    [DataRow("var other = new DbContextOptionsBuilder(); ref var alias = ref other; alias = ref (System.DateTime.Now.Ticks > 0 ? ref other : ref b); alias = new DbContextOptionsBuilder();")]
    [DataRow("var other = new DbContextOptionsBuilder(); ref var alias = ref (System.DateTime.Now.Ticks > 0 ? ref b : ref other); alias = new DbContextOptionsBuilder();")]
    [DataRow("static ref DbContextOptionsBuilder Escape(ref DbContextOptionsBuilder value) => ref value; Escape(ref b) = new DbContextOptionsBuilder();")]
    public async Task WritableRefEscapes_AreExcludedWithPositiveControls(string escape)
    {
        string source = SingletonTestHelper.Source($$"""
            class EscapedContext : DbContext
            {
                protected override void OnConfiguring(DbContextOptionsBuilder b)
                {
                    {{escape}}
                    b.AddInterceptors(new Fresh());
                }
            }
            class Registration
            {
                void Configure(IServiceCollection services)
                {
                    services.AddDbContext<EscapedContext>(b =>
                    {
                        {{escape}}
                        b.AddInterceptors(new Fresh());
                    });
                    services.AddDbContext<EscapedContext>(b => b.AddInterceptors(new Derived()));
                }
            }
            """ + SingletonTestHelper.Context("b.AddInterceptors(new Derived());"));
        ImmutableArray<Diagnostic> diagnostics = await SingletonTestHelper.Analyze(source);
        Assert.HasCount(2, diagnostics);
        foreach (Diagnostic diagnostic in diagnostics)
        {
            AssertCreation(diagnostic, source, "new Derived()");
        }
    }

    [TestMethod]
    [DataRow("var alias = b; alias = new DbContextOptionsBuilder();")]
    [DataRow("ref readonly DbContextOptionsBuilder alias = ref b; _ = alias;")]
    public async Task NonWritableAliases_PreserveConfigurationParameterIdentity(string alias)
    {
        string source = SingletonTestHelper.Source(SingletonTestHelper.Context(alias + "\nb.AddInterceptors(new Fresh());"));
        AssertCreation(Assert.ContainsSingle(await SingletonTestHelper.Analyze(source)), source, "new Fresh()");
    }

    [TestMethod]
    public async Task UserDefinedBuilderConversions_AreNotAssumedToPreserveTheReceiver()
    {
        string source = SingletonTestHelper.Source("""
            class Replacement
            {
                public static explicit operator Replacement(DbContextOptionsBuilder b) => new();
                public static implicit operator DbContextOptionsBuilder(Replacement value) => new();
            }
            """ + SingletonTestHelper.Context("""
                ((DbContextOptionsBuilder)(Replacement)b).AddInterceptors(new Fresh());
                b.AddInterceptors(new Derived());
                """));
        AssertCreation(Assert.ContainsSingle(await SingletonTestHelper.Analyze(source)), source, "new Derived()");
    }

    [TestMethod]
    public async Task InheritedOverrideAndTargetTypedCreation_Report()
    {
        string source = SingletonTestHelper.Source("""
            class BaseContext : DbContext
            {
                protected override void OnConfiguring(DbContextOptionsBuilder b) { }
            }
            class Child : BaseContext
            {
                protected override void OnConfiguring(DbContextOptionsBuilder b)
                    => b.AddInterceptors(new Fresh[] { new() });
            }
            """);
        AssertCreation(Assert.ContainsSingle(await SingletonTestHelper.Analyze(source)), source, "new()");
    }

    [TestMethod]
    public async Task Pragma_RestoresDefaultWarningAfterScopedSuppression()
    {
        string source = SingletonTestHelper.Source(SingletonTestHelper.Context("""
            #pragma warning disable WRANGLER0007 // Reuse singleton interceptor instances; Justification: Intentional test allocation.
            b.AddInterceptors(new Fresh());
            #pragma warning restore WRANGLER0007
            b.AddInterceptors(new Derived());
            """));
        AssertCreation(Assert.ContainsSingle(await SingletonTestHelper.Analyze(source)), source, "new Derived()");
    }

    [TestMethod]
    public async Task MissingSourceDefinedAndWrongAssemblyFrameworks_AreSkipped()
    {
        string source = SingletonTestHelper.Source(SingletonTestHelper.Context("b.AddInterceptors(new Fresh());"));
        Assert.IsEmpty(await SingletonTestHelper.Analyze("class Unrelated { }", reference: null));
        Assert.IsEmpty(await SingletonTestHelper.Analyze(source + SingletonTestHelper.ApiSource, reference: null));
        Assert.IsEmpty(await SingletonTestHelper.Analyze(source, SingletonTestHelper.CreateReference("Lookalike")));
        AssertCreation(Assert.ContainsSingle(await SingletonTestHelper.Analyze(source)), source, "new Fresh()");
    }

    [TestMethod]
    public async Task GeneratedAndIncompleteCode_IsSkippedWithoutAnalyzerFailures()
    {
        string source = SingletonTestHelper.Source(SingletonTestHelper.Context("b.AddInterceptors(new Fresh());"));
        Assert.IsEmpty(await SingletonTestHelper.Analyze("// <auto-generated/>\n" + source));
        Assert.IsEmpty(await SingletonTestHelper.Analyze(
            SingletonTestHelper.Source(SingletonTestHelper.Context("b.AddInterceptors(new Fresh( ;")), allowCompilerErrors: true));
        AssertCreation(Assert.ContainsSingle(await SingletonTestHelper.Analyze(source)), source, "new Fresh()");
    }

    [TestMethod]
    public async Task NamesAlone_DoNotRecognizeConfigurationOrRegistrations()
    {
        string source = SingletonTestHelper.Source("""
            class NotContext
            {
                void OnConfiguring(DbContextOptionsBuilder b) => b.AddInterceptors(new Fresh());
            }
            static class FakeExtensions
            {
                public static void AddDbContext<T>(this object services, System.Action<DbContextOptionsBuilder> optionsAction) { }
            }
            class Registration
            {
                void Configure(object services) => services.AddDbContext<NotContext>(b => b.AddInterceptors(new Fresh()));
            }
            """ + SingletonTestHelper.Context("b.AddInterceptors(new Derived());"));
        AssertCreation(Assert.ContainsSingle(await SingletonTestHelper.Analyze(source)), source, "new Derived()");
    }

    private static void AssertCreation(Diagnostic diagnostic, string source, string expectedCreation)
    {
        Assert.AreEqual("WRANGLER0007", diagnostic.Id);
        Assert.AreEqual(expectedCreation, source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }
}

internal static class SingletonTestHelper
{
    internal static readonly ImmutableArray<MetadataReference> RuntimeReferences = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
        ?? throw new InvalidOperationException("Missing runtime references."))
        .Split(Path.PathSeparator)
        .Where(path => Path.GetDirectoryName(path) == Path.GetDirectoryName(typeof(object).Assembly.Location))
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();

    internal static readonly MetadataReference DependencyInjectionReference = CompileReference(
        "Microsoft.Extensions.DependencyInjection.Abstractions", """
        namespace Microsoft.Extensions.DependencyInjection
        {
            public interface IServiceCollection { }
            public enum ServiceLifetime { Singleton, Scoped, Transient }
        }
        """, RuntimeReferences);

    internal static readonly MetadataReference Reference = CreateReference();

    internal static string Source(string classes) => """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Diagnostics;
        using Microsoft.Extensions.DependencyInjection;
        class Fresh : IMaterializationInterceptor { }
        class Derived : Fresh { }
        class Ordinary : SaveChangesInterceptor { }
        """ + "\n" + classes;

    internal static string Context(string body) => $$"""
        class C : DbContext
        {
            static readonly Fresh Cached = new();
            static Fresh Property => Cached;
            static Fresh Factory() => new();
            protected override void OnConfiguring(DbContextOptionsBuilder b)
            {
                {{body}}
            }
        }
        """;

    internal static string ApiSource => """
        namespace Microsoft.EntityFrameworkCore.Diagnostics
        {
            public interface IInterceptor { }
            public interface ISingletonInterceptor : IInterceptor { }
            public struct MaterializationInterceptionData { }
            public struct InterceptionResult { }
            public struct InterceptionResult<T> { }
            public interface IMaterializationInterceptor : ISingletonInterceptor
            {
                InterceptionResult<object> CreatingInstance(MaterializationInterceptionData materializationData, InterceptionResult<object> result) => result;
                object CreatedInstance(MaterializationInterceptionData materializationData, object entity) => entity;
                InterceptionResult InitializingInstance(MaterializationInterceptionData materializationData, object entity, InterceptionResult result) => result;
                object InitializedInstance(MaterializationInterceptionData materializationData, object entity) => entity;
            }
            public interface ISaveChangesInterceptor : IInterceptor { }
            public abstract class SaveChangesInterceptor : ISaveChangesInterceptor { }
        }
        namespace Microsoft.EntityFrameworkCore
        {
            using Microsoft.EntityFrameworkCore.Diagnostics;
            public class DbContext
            {
                protected virtual void OnConfiguring(DbContextOptionsBuilder optionsBuilder) { }
            }
            public class DbContextOptionsBuilder
            {
                public virtual DbContextOptionsBuilder AddInterceptors(params IInterceptor[] interceptors) => this;
                public virtual DbContextOptionsBuilder AddInterceptors(System.Collections.Generic.IEnumerable<IInterceptor> interceptors) => this;
                public virtual DbContextOptionsBuilder EnableSensitiveDataLogging(bool enabled = true) => this;
            }
            public class DbContextOptionsBuilder<TContext> : DbContextOptionsBuilder where TContext : DbContext
            {
                public new virtual DbContextOptionsBuilder<TContext> AddInterceptors(params IInterceptor[] interceptors) => this;
                public new virtual DbContextOptionsBuilder<TContext> AddInterceptors(System.Collections.Generic.IEnumerable<IInterceptor> interceptors) => this;
                public new virtual DbContextOptionsBuilder<TContext> EnableSensitiveDataLogging(bool enabled = true) => this;
            }
            public static class SqliteDbContextOptionsBuilderExtensions
            {
                public static DbContextOptionsBuilder UseSqlite(this DbContextOptionsBuilder builder, string connectionString) => builder;
            }
        }
        namespace Microsoft.Extensions.DependencyInjection
        {
            using Microsoft.EntityFrameworkCore;
            public static class EntityFrameworkServiceCollectionExtensions
            {
        """ + "\n" + string.Join("\n", new[] { "TContext", "TContextService, TContextImplementation" }.SelectMany(generics =>
            new[] { "System.Action<DbContextOptionsBuilder>", "System.Action<System.IServiceProvider, DbContextOptionsBuilder>" }.Select(callback =>
                $"public static IServiceCollection AddDbContext<{generics}>(this IServiceCollection serviceCollection, {callback} optionsAction"
                + (callback == "System.Action<DbContextOptionsBuilder>" ? " = null" : "")
                + ", ServiceLifetime contextLifetime = ServiceLifetime.Scoped, ServiceLifetime optionsLifetime = ServiceLifetime.Scoped) "
                + (generics == "TContext" ? "where TContext : DbContext" : "where TContextImplementation : DbContext, TContextService")
                + " => serviceCollection;"))) + "\n" + """
                public static IServiceCollection AddDbContextPool<TContext>(this IServiceCollection services, System.Action<DbContextOptionsBuilder> optionsAction) where TContext : DbContext => services;
                public static IServiceCollection AddDbContextFactory<TContext>(this IServiceCollection services, System.Action<DbContextOptionsBuilder> optionsAction) where TContext : DbContext => services;
            }
        }
        """;

    internal static MetadataReference CreateReference(string assemblyName = "Microsoft.EntityFrameworkCore") =>
        CompileReference(assemblyName, ApiSource, RuntimeReferences.Add(DependencyInjectionReference));

    private static MetadataReference CompileReference(string name, string source, ImmutableArray<MetadataReference> references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12))], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    internal static Task<ImmutableArray<Diagnostic>> Analyze(string source, bool allowCompilerErrors = false) =>
        Analyze(source, Reference, allowCompilerErrors);

    internal static async Task<ImmutableArray<Diagnostic>> Analyze(string source, MetadataReference? reference, bool allowCompilerErrors = false)
    {
        CSharpCompilation compilation = CSharpCompilation.Create("Tests",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), path: "Test.cs")],
            reference is null ? RuntimeReferences.Add(DependencyInjectionReference) : RuntimeReferences.Add(DependencyInjectionReference).Add(reference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        if (!allowCompilerErrors)
        {
            Assert.DoesNotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error, compilation.GetDiagnostics(),
                string.Join("\n", compilation.GetDiagnostics()));
        }
        return (await compilation.WithAnalyzers([new SingletonInterceptorAnalyzer()]).GetAnalyzerDiagnosticsAsync())
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ToImmutableArray();
    }
}
