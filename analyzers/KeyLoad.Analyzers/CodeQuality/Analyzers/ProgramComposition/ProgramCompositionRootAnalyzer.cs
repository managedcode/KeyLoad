using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Features.CodeQuality;

/// <summary>Keeps process composition visible in executable Program.cs files.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ProgramCompositionRootAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Stable identifier for this analyzer's diagnostic.</summary>
    public const string DiagnosticId = "KLD0020";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        CodeQualityDiagnosticText.CompositionRootTitle,
        CodeQualityDiagnosticText.CompositionRootMessage,
        CodeQualityDiagnosticCategories.Architecture,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: CodeQualityDiagnosticText.CompositionRootDescription);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        System.ArgumentNullException.ThrowIfNull(context);
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        if (!string.Equals(Path.GetFileName(context.Tree.FilePath),
                CodeQualitySourceNames.ProgramFile,
                StringComparison.OrdinalIgnoreCase) ||
            context.Tree.GetRoot(context.CancellationToken) is not CompilationUnitSyntax compilationUnit)
        {
            return;
        }

        var statements = compilationUnit.Members.OfType<GlobalStatementSyntax>().ToArray();
        if (statements.Length != 1)
        {
            return;
        }

        var statement = statements[0].Statement;
        var expression = statement switch
        {
            ExpressionStatementSyntax expressionStatement => expressionStatement.Expression,
            ReturnStatementSyntax returnStatement => returnStatement.Expression,
            _ => null
        };
        if (expression is AwaitExpressionSyntax awaited)
        {
            expression = awaited.Expression;
        }
        if (expression is InvocationExpressionSyntax configured && configured.Expression is MemberAccessExpressionSyntax configuration
            && configuration.Name.Identifier.ValueText == CodeQualityLifecycleMethodNames.ConfigureAwait)
        {
            expression = configuration.Expression;
        }
        if (expression is not InvocationExpressionSyntax forwarded || forwarded.Expression is not MemberAccessExpressionSyntax invocation
            || forwarded.DescendantNodes().OfType<InvocationExpressionSyntax>().Any())
        {
            return;
        }
        var methodName = invocation.Name.Identifier.ValueText;
        if (methodName is not CodeQualityLifecycleMethodNames.Run and not CodeQualityLifecycleMethodNames.RunAsync)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, statement.GetLocation(), invocation.ToString()));
    }
}
