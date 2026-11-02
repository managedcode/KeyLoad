using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.CodeQuality;

internal static class UntypedDeclarationAnalysis
{
    internal static void Analyze(SyntaxNodeAnalysisContext context, UntypedTypeFacts facts, DiagnosticDescriptor rule)
    {
        var candidate = Type(context.Node);
        if (!candidate.Found || candidate.Value is not { } syntax || UntypedSiteReason.Present(syntax, rule.Id))
        {
            return;
        }
        var type = context.SemanticModel.GetTypeInfo(syntax, context.CancellationToken).Type;
        if (facts.Contains(type, context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(rule, syntax.GetLocation(), UntypedContractPolicy.Declaration,
                type!.ToDisplayString()) /* KLD0041: Roslyn requires object message arguments for this declaration role and its resolved erased type name. */);
        }
    }

    private static (bool Found, TypeSyntax? Value) Type(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax method => (true, method.ReturnType),
        LocalFunctionStatementSyntax local => (true, local.ReturnType),
        DelegateDeclarationSyntax declaration => (true, declaration.ReturnType),
        PropertyDeclarationSyntax property => (true, property.Type),
        IndexerDeclarationSyntax indexer => (true, indexer.Type),
        FieldDeclarationSyntax field => (true, field.Declaration.Type),
        VariableDeclarationSyntax variable when variable.Parent is not (FieldDeclarationSyntax or EventFieldDeclarationSyntax)
            => (true, variable.Type),
        EventDeclarationSyntax declaration => (true, declaration.Type),
        EventFieldDeclarationSyntax field => (true, field.Declaration.Type),
        ParameterSyntax parameter => (true, parameter.Type),
        OperatorDeclarationSyntax operation => (true, operation.ReturnType),
        ConversionOperatorDeclarationSyntax conversion => (true, conversion.Type),
        ParenthesizedLambdaExpressionSyntax lambda => (true, lambda.ReturnType),
        _ => (false, default)
    };
}
