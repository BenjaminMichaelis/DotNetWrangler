using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Wrangler.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NullForgivingOperatorAnalyzer : DiagnosticAnalyzer
{
    public const string NullForgivingOperatorDiagnosticId = "WRANGLER0002";
    public const string FabricatedNonNullValueDiagnosticId = "WRANGLER0003";
    public const string UseAssertionInTestsDiagnosticId = "WRANGLER0004";
    private const string Category = "Reliability";

    private static readonly (string TestMethodAttributeMetadataName, string AssertTypeMetadataName, string Assertion, bool RequiresAwait)[] KnownTestFrameworks =
    [
        ("Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute", "Microsoft.VisualStudio.TestTools.UnitTesting.Assert", "Assert.IsNotNull({0})", false),
        ("Xunit.FactAttribute", "Xunit.Assert", "Assert.NotNull({0})", false),
        ("NUnit.Framework.TestAttribute", "NUnit.Framework.Assert", "Assert.That({0}, Is.Not.Null)", false),
        ("NUnit.Framework.TestCaseAttribute", "NUnit.Framework.Assert", "Assert.That({0}, Is.Not.Null)", false),
        ("NUnit.Framework.TestCaseSourceAttribute", "NUnit.Framework.Assert", "Assert.That({0}, Is.Not.Null)", false),
        ("NUnit.Framework.TheoryAttribute", "NUnit.Framework.Assert", "Assert.That({0}, Is.Not.Null)", false),
        ("TUnit.Core.TestAttribute", "TUnit.Assertions.Assert", "await Assert.That({0}).IsNotNull()", true),
    ];

    internal static readonly DiagnosticDescriptor NullForgivingOperatorRule = new(
        NullForgivingOperatorDiagnosticId,
        "Null-forgiving operator hides a possible null value",
        "The null-forgiving operator on '{0}' overrides the compiler's nullable analysis",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Only use '!' when you can prove the value is non-null and the compiler cannot. "
            + "Prefer handling null explicitly (a null check, '?.', or a throw for contract violations) or "
            + "expressing the guarantee with nullable attributes such as [NotNullWhen] or [MemberNotNull]. "
            + "For justified uses, including intentional null-input tests, use a scoped pragma suppression with a reason.",
        helpLinkUri: HelpLinks.ForRule(NullForgivingOperatorDiagnosticId));

    internal static readonly DiagnosticDescriptor FabricatedNonNullValueRule = new(
        FabricatedNonNullValueDiagnosticId,
        "Null-forgiving operator fabricates a non-null value",
        "'{0}' assigns null to a location declared as non-null",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "'null!' and 'default!' make the compiler believe a null value is non-null, moving the "
            + "NullReferenceException to a later, harder-to-diagnose location. Declare the member as nullable, "
            + "use 'required' members, or initialize it with a real value in the constructor. Do not replace it "
            + "with an arbitrary default (such as an empty first name) that silently hides missing data. "
            + "Deliberately passing null to test argument validation is valid; use a scoped pragma suppression with a reason.",
        helpLinkUri: HelpLinks.ForRule(FabricatedNonNullValueDiagnosticId));

    internal static readonly DiagnosticDescriptor UseAssertionInTestsRule = new(
        UseAssertionInTestsDiagnosticId,
        "Use a not-null assertion instead of the null-forgiving operator in tests",
        "Use '{0}' instead of the null-forgiving operator so the test fails with a clear message",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "In tests, '!' turns an unexpected null into a NullReferenceException somewhere later in the test. "
            + "A not-null assertion reports the failed expectation before the value is used. Nullable-warning handling "
            + "depends on the assertion API and the framework's analyzers.",
        helpLinkUri: HelpLinks.ForRule(UseAssertionInTestsDiagnosticId));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [NullForgivingOperatorRule, FabricatedNonNullValueRule, UseAssertionInTestsRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(compilationContext =>
        {
            ImmutableArray<TestFramework> testFrameworks = FindReferencedTestFrameworks(compilationContext.Compilation);
            var testClassAssertions = new ConcurrentDictionary<INamedTypeSymbol, TestFramework?>(SymbolEqualityComparer.Default);
            compilationContext.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeNode(nodeContext, testFrameworks, testClassAssertions),
                SyntaxKind.SuppressNullableWarningExpression);
        });
    }

    private static ImmutableArray<TestFramework> FindReferencedTestFrameworks(Compilation compilation)
    {
        ImmutableArray<TestFramework>.Builder frameworks = ImmutableArray.CreateBuilder<TestFramework>();
        foreach ((string testMethodAttributeMetadataName, string assertTypeMetadataName, string assertion, bool requiresAwait) in KnownTestFrameworks)
        {
            INamedTypeSymbol? testMethodAttribute = compilation.GetTypeByMetadataName(testMethodAttributeMetadataName);
            INamedTypeSymbol? assertType = compilation.GetTypeByMetadataName(assertTypeMetadataName);
            if (IsReferencedType(compilation, testMethodAttribute) && IsReferencedType(compilation, assertType))
            {
                frameworks.Add(new TestFramework(testMethodAttribute!, assertion, requiresAwait));
            }
        }

        return frameworks.ToImmutable();
    }

    private static bool IsReferencedType(Compilation compilation, INamedTypeSymbol? type) =>
        type is not null && !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly);

    private static TestFramework? FindNotNullAssertion(INamedTypeSymbol type, ImmutableArray<TestFramework> testFrameworks)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (IMethodSymbol method in current.GetMembers().OfType<IMethodSymbol>())
            {
                foreach (AttributeData attribute in method.GetAttributes())
                {
                    foreach (TestFramework framework in testFrameworks)
                    {
                        if (InheritsFromOrEquals(attribute.AttributeClass, framework.TestMethodAttribute))
                        {
                            return framework;
                        }
                    }
                }
            }
        }

        return null;
    }

    private static bool InheritsFromOrEquals(INamedTypeSymbol? type, INamedTypeSymbol baseType)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static void AnalyzeNode(
        SyntaxNodeAnalysisContext context,
        ImmutableArray<TestFramework> testFrameworks,
        ConcurrentDictionary<INamedTypeSymbol, TestFramework?> testClassAssertions)
    {
        var suppression = (PostfixUnaryExpressionSyntax)context.Node;
        ExpressionSyntax operand = suppression.Operand;
        while (operand is ParenthesizedExpressionSyntax parenthesized)
        {
            operand = parenthesized.Expression;
        }

        string operandText = suppression.Operand.ToString();
        if (operand.Kind() is SyntaxKind.NullLiteralExpression or SyntaxKind.DefaultLiteralExpression or SyntaxKind.DefaultExpression)
        {
            DiagnosticDescriptor rule = operand.IsKind(SyntaxKind.NullLiteralExpression)
                || context.SemanticModel.GetTypeInfo(operand, context.CancellationToken).Type is { IsReferenceType: true }
                    ? FabricatedNonNullValueRule
                    : NullForgivingOperatorRule;
            context.ReportDiagnostic(Diagnostic.Create(rule, suppression.GetLocation(), operandText));
            return;
        }

        TestFramework? framework = null;
        if (!testFrameworks.IsEmpty
            && (context.ContainingSymbol as INamedTypeSymbol ?? context.ContainingSymbol?.ContainingType) is { } containingType)
        {
            framework = testClassAssertions.GetOrAdd(containingType, type => FindNotNullAssertion(type, testFrameworks));
        }

        context.ReportDiagnostic(framework is null || (framework.RequiresAwait && !CanAwait(suppression))
            ? Diagnostic.Create(NullForgivingOperatorRule, suppression.GetLocation(), operandText)
            : Diagnostic.Create(
                UseAssertionInTestsRule,
                suppression.GetLocation(),
                string.Format(CultureInfo.InvariantCulture, framework.NotNullAssertion, operandText)));
    }

    private static bool CanAwait(SyntaxNode node)
    {
        if (node.Ancestors().Any(IsUnsafeContext))
        {
            return false;
        }

        foreach (SyntaxNode ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax function:
                    return function.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
                case LocalFunctionStatementSyntax function:
                    return function.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case MethodDeclarationSyntax method:
                    return method.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case MemberDeclarationSyntax or AccessorDeclarationSyntax or ParameterSyntax or AttributeSyntax
                    or LockStatementSyntax or FixedStatementSyntax
                    or CatchFilterClauseSyntax or QueryExpressionSyntax:
                    return false;
            }
        }

        return false;
    }

    private static bool IsUnsafeContext(SyntaxNode node)
    {
        SyntaxTokenList modifiers = node switch
        {
            BaseTypeDeclarationSyntax declaration => declaration.Modifiers,
            BaseMethodDeclarationSyntax declaration => declaration.Modifiers,
            BasePropertyDeclarationSyntax declaration => declaration.Modifiers,
            BaseFieldDeclarationSyntax declaration => declaration.Modifiers,
            LocalFunctionStatementSyntax declaration => declaration.Modifiers,
            _ => default,
        };
        return node is UnsafeStatementSyntax || modifiers.Any(SyntaxKind.UnsafeKeyword);
    }

    private sealed class TestFramework(INamedTypeSymbol testMethodAttribute, string notNullAssertion, bool requiresAwait)
    {
        public INamedTypeSymbol TestMethodAttribute { get; } = testMethodAttribute;

        public string NotNullAssertion { get; } = notNullAssertion;

        public bool RequiresAwait { get; } = requiresAwait;
    }
}
