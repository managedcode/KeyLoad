using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.CodeQuality;

/// <summary>Requires awaited ZoneTree work: no blocking on its tasks and no thread-pool dispatch of engine access.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SynchronousZoneTreeAccessAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Stable compiler and SARIF diagnostic identifier.</summary>
    public const string DiagnosticId = ZoneTreeAccessPolicy.DiagnosticId;
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, ZoneTreeAccessPolicy.Title,
        ZoneTreeAccessPolicy.Message, CodeQualityDiagnosticCategories.Architecture, DiagnosticSeverity.Error,
        isEnabledByDefault: true, description: ZoneTreeAccessPolicy.Description);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new System.ArgumentNullException(nameof(context));
        }
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, Microsoft.CodeAnalysis.OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeProperty, Microsoft.CodeAnalysis.OperationKind.PropertyReference);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (ZoneTreeAccessPolicy.Blocking(method) && ZoneTreeDispatchAnalysis.FromEngine(invocation.Instance) ||
            ZoneTreeAccessPolicy.Dispatch(method) && ZoneTreeDispatchAnalysis.ContainsAccess(invocation, context,
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.ToDisplayString()));
        }
    }

    private static void AnalyzeProperty(OperationAnalysisContext context)
    {
        var reference = (IPropertyReferenceOperation)context.Operation;
        if (reference.Property.Name == "Result" && ZoneTreeAccessPolicy.Awaitable(reference.Property.ContainingType)
            && ZoneTreeDispatchAnalysis.FromEngine(reference.Instance))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, reference.Syntax.GetLocation(), reference.Property.ToDisplayString()));
        }
    }
}
