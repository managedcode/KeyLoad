using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Features.CodeQuality;

/// <summary>Requires explicit approval for implementation terminology outside its approved meanings.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnauthorizedImplementationTermAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Stable compiler and SARIF diagnostic identifier.</summary>
    public const string DiagnosticId = ImplementationTermCatalog.DiagnosticId;
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, ImplementationTermCatalog.Title,
        ImplementationTermCatalog.Message, CodeQualityDiagnosticCategories.Architecture, DiagnosticSeverity.Error,
        isEnabledByDefault: true, description: ImplementationTermCatalog.Description);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.AssemblyName?.StartsWith(ImplementationTermCatalog.NamespacePrefix, StringComparison.Ordinal) == true)
            {
                var testOwners = new Lazy<System.Collections.Generic.HashSet<INamedTypeSymbol>>(
                    () => ImplementationTestOwnership.Read(start.Compilation, start.CancellationToken));
                start.RegisterSyntaxTreeAction(tree => AnalyzeTree(tree, start.Compilation, testOwners));
            }
        });
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context, Compilation compilation,
        Lazy<System.Collections.Generic.HashSet<INamedTypeSymbol>> testOwners)
    {
        var root = context.Tree.GetRoot(context.CancellationToken);
        var model = compilation.GetSemanticModel(context.Tree);
        var permissions = new ImplementationTermPermissions(model, testOwners);
        foreach (var token in root.DescendantTokens())
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (token.IsKind(SyntaxKind.IdentifierToken) || token.Parent is LiteralExpressionSyntax ||
                token.IsKind(SyntaxKind.InterpolatedStringTextToken))
            {
                AnalyzeText(context, permissions, token.Parent!, token.ValueText, token.GetLocation());
            }
        }
        foreach (var trivia in root.DescendantTrivia().Where(IsComment))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            AnalyzeText(context, permissions, trivia.Token.Parent ?? root, trivia.ToFullString(), trivia.GetLocation());
        }
    }

    private static bool IsComment(SyntaxTrivia trivia) => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
        trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
        trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);

    private static void AnalyzeText(SyntaxTreeAnalysisContext context, ImplementationTermPermissions permissions,
        SyntaxNode node, string text, Location location)
    {
        foreach (var match in ImplementationTermReader.Read(text).Distinct())
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!permissions.Allows(match.Term, text, node, context.CancellationToken))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, match.Text));
            }
        }
    }
}
