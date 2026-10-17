using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Features.CodeQuality;

/// <summary>Keeps executable Program.cs files thin composition roots.</summary>
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

    private static readonly ImmutableHashSet<string> LifecycleMethods =
        ImmutableHashSet.Create(StringComparer.Ordinal,
            CodeQualityLifecycleMethodNames.CreateBuilder,
            CodeQualityLifecycleMethodNames.CreateDefault,
            CodeQualityLifecycleMethodNames.Build,
            CodeQualityLifecycleMethodNames.Run,
            CodeQualityLifecycleMethodNames.RunAsync);

    private static readonly ImmutableArray<string> AggregateMethodPrefixes =
        CodeQualityCompositionMethods.All;

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

        foreach (var member in compilationUnit.Members)
        {
            if (member is GlobalStatementSyntax globalStatement &&
                IsAllowedCompositionStatement(globalStatement.Statement))
            {
                continue;
            }

            if (member is ClassDeclarationSyntax classDeclaration &&
                string.Equals(classDeclaration.Identifier.ValueText, CodeQualitySourceNames.ProgramType, StringComparison.Ordinal) &&
                classDeclaration.BaseList is null &&
                classDeclaration.Members.Count == 0)
            {
                continue;
            }

            var source = member.ToString().ReplaceLineEndings(CodeQualitySourceNames.ProgramLineReplacement).Trim();
            if (source.Length > CodeQualitySourceLimits.DiagnosticExcerptLength)
            {
                source = string.Concat(
                    source.AsSpan(0, CodeQualitySourceLimits.DiagnosticExcerptPrefixLength),
                    CodeQualitySourceNames.TruncationEllipsis.AsSpan());
            }

            context.ReportDiagnostic(Diagnostic.Create(Rule, member.GetLocation(), source));
        }
    }

    private static bool IsAllowedCompositionStatement(StatementSyntax statement)
    {
        if (statement is not LocalDeclarationStatementSyntax and
            not ExpressionStatementSyntax and
            not ReturnStatementSyntax)
        {
            return false;
        }

        var invocations = statement.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .ToArray();
        return invocations.Length > 0 && invocations.All(invocation =>
            TryGetInvokedMethodName(invocation, out var methodName) &&
            IsAllowedMethod(methodName));
    }

    private static bool IsAllowedMethod(string methodName) =>
        LifecycleMethods.Contains(methodName) ||
        AggregateMethodPrefixes.Any(prefix => methodName.StartsWith(prefix, StringComparison.Ordinal));

    private static bool TryGetInvokedMethodName(
        InvocationExpressionSyntax invocation,
        out string methodName)
    {
        methodName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => string.Empty
        };
        return methodName.Length > 0;
    }
}
