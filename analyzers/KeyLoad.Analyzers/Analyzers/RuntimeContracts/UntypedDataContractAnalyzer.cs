using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.CodeQuality;

/// <summary>Requires typed contracts or an exact-site explanation for erased data and object conversions.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UntypedDataContractAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Stable identifier for erased data declarations and uses.</summary>
    public const string ContractDiagnosticId = UntypedContractPolicy.ContractId;
    private static readonly DiagnosticDescriptor ContractRule = new(ContractDiagnosticId,
        UntypedContractPolicy.ContractTitle, UntypedContractPolicy.ContractMessage,
        CodeQualityDiagnosticCategories.Architecture, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: UntypedContractPolicy.ContractDescription);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [ContractRule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new System.ArgumentNullException(nameof(context));
        }
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(Register);
    }

    private static void Register(CompilationStartAnalysisContext context)
    {
        var facts = new UntypedTypeFacts(context.Compilation);
        context.RegisterSyntaxNodeAction(node => UntypedDeclarationAnalysis.Analyze(node, facts, ContractRule),
            SyntaxKind.MethodDeclaration, SyntaxKind.LocalFunctionStatement, SyntaxKind.DelegateDeclaration,
            SyntaxKind.PropertyDeclaration, SyntaxKind.IndexerDeclaration, SyntaxKind.FieldDeclaration,
            SyntaxKind.EventDeclaration, SyntaxKind.EventFieldDeclaration, SyntaxKind.VariableDeclaration,
            SyntaxKind.Parameter,
            SyntaxKind.OperatorDeclaration, SyntaxKind.ConversionOperatorDeclaration,
            SyntaxKind.ParenthesizedLambdaExpression);
        context.RegisterOperationAction(operation => UntypedUseAnalysis.Analyze(operation, facts, ContractRule),
            Microsoft.CodeAnalysis.OperationKind.Invocation, Microsoft.CodeAnalysis.OperationKind.PropertyReference,
            Microsoft.CodeAnalysis.OperationKind.FieldReference, Microsoft.CodeAnalysis.OperationKind.ObjectCreation,
            Microsoft.CodeAnalysis.OperationKind.ArrayCreation, Microsoft.CodeAnalysis.OperationKind.CollectionExpression,
            Microsoft.CodeAnalysis.OperationKind.DynamicInvocation,
            Microsoft.CodeAnalysis.OperationKind.DynamicMemberReference);
    }
}
