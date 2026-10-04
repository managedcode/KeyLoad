using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Features.CodeQuality;

/// <summary>Reports direct endpoint mappings in KeyLoad.Server Program.cs.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ProgramEndpointMappingAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Stable identifier for this analyzer's diagnostic.</summary>
    public const string DiagnosticId = "KLD0013";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        CodeQualityDiagnosticText.EndpointMappingTitle,
        CodeQualityDiagnosticText.EndpointMappingMessage,
        CodeQualityDiagnosticCategories.Architecture,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: CodeQualityDiagnosticText.EndpointMappingDescription);

    private const string ServerAssemblyName = "KeyLoad.Server";
    private const string AggregateMethodName = "MapKeyLoadApi";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        System.ArgumentNullException.ThrowIfNull(context);
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (!string.Equals(context.SemanticModel.Compilation.AssemblyName,
                ServerAssemblyName,
                StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(context.Node.SyntaxTree.FilePath),
                CodeQualitySourceNames.ProgramFile,
                StringComparison.OrdinalIgnoreCase) ||
            !context.Node.Ancestors().OfType<GlobalStatementSyntax>().Any() ||
            context.Node is not InvocationExpressionSyntax invocation ||
            !TryGetInvokedMethodName(invocation, out var methodName) ||
            !methodName.StartsWith(CodeQualitySourceNames.MapMethodPrefix, StringComparison.Ordinal) ||
            string.Equals(methodName, AggregateMethodName, StringComparison.Ordinal))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule,
            invocation.GetLocation(),
            AggregateMethodName,
            methodName));
    }

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
