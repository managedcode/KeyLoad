using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

/// <summary>Requires native typed synchronization outside Orleans activation-owned state.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TypedSynchronizationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Stable identifier for this analyzer's diagnostic.</summary>
    public const string DiagnosticId = "KLD0034";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        CodeQualityDiagnosticText.TypedSynchronizationTitle,
        CodeQualityDiagnosticText.TypedSynchronizationMessage,
        CodeQualityDiagnosticCategories.Reliability,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: CodeQualityDiagnosticText.TypedSynchronizationDescription);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static startContext =>
        {
            var lockType = GetMetadataType(startContext.Compilation, ThreadingMetadataNames.Lock);
            var monitorType = GetMetadataType(startContext.Compilation, ThreadingMetadataNames.Monitor);
            var grainType = GetMetadataType(startContext.Compilation, OrleansMetadataNames.Grain);
            startContext.RegisterOperationAction(
                operationContext => AnalyzeLock(operationContext, lockType, grainType),
                Microsoft.CodeAnalysis.OperationKind.Lock);
            startContext.RegisterOperationAction(
                operationContext => AnalyzeInvocation(operationContext, monitorType, lockType, grainType),
                Microsoft.CodeAnalysis.OperationKind.Invocation);
        });
    }

    private static INamedTypeSymbol? GetMetadataType(Compilation compilation, string metadataName)
    {
        var type = compilation.GetTypeByMetadataName(metadataName);
        return type is not null && type.Locations.All(static location => location.IsInMetadata)
            ? type
            : null;
    }

    private static void AnalyzeLock(
        OperationAnalysisContext context,
        INamedTypeSymbol? lockType,
        INamedTypeSymbol? grainType)
    {
        var value = ((ILockOperation)context.Operation).LockedValue;
        if (lockType is not null &&
            SymbolEqualityComparer.Default.Equals(value.Type, lockType) &&
            !IsGrainMember(context, grainType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, value.Syntax.GetLocation(), value.Syntax.ToString()));
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol? monitorType,
        INamedTypeSymbol? lockType,
        INamedTypeSymbol? grainType)
    {
        var method = ((IInvocationOperation)context.Operation).TargetMethod;
        var isMonitor = monitorType is not null &&
            SymbolEqualityComparer.Default.Equals(method.ContainingType, monitorType);
        var isGrainLock = lockType is not null &&
            SymbolEqualityComparer.Default.Equals(method.ContainingType, lockType) &&
            method.Name is ThreadingMetadataNames.Enter or ThreadingMetadataNames.EnterScope or ThreadingMetadataNames.TryEnter &&
            IsGrainMember(context, grainType);
        if (!isMonitor && !isGrainLock)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation(),
            method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    private static bool IsGrainMember(OperationAnalysisContext context, INamedTypeSymbol? grainType)
    {
        if (grainType is null)
        {
            return false;
        }

        for (var type = context.ContainingSymbol.ContainingType; type is not null; type = type.BaseType)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (SymbolEqualityComparer.Default.Equals(type, grainType))
            {
                return true;
            }
        }

        return false;
    }
}
