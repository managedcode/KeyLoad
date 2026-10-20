using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

/// <summary>Requires synchronous execution engine access while preserving asynchronous Orleans and transport calls.</summary>
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
        context.RegisterOperationAction(AnalyzeUsing, Microsoft.CodeAnalysis.OperationKind.Using, Microsoft.CodeAnalysis.OperationKind.UsingDeclaration);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if ((ZoneTreeAccessPolicy.Execution(method) || invocation.Instance is not null &&
            ZoneTreeDispatchAnalysis.RuntimeResource(invocation.Instance)) && ZoneTreeAccessPolicy.Asynchronous(method) ||
            ZoneTreeAccessPolicy.Dispatch(method) && ZoneTreeDispatchAnalysis.ContainsAccess(invocation, context,
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.ToDisplayString()));
        }
    }

    private static void AnalyzeProperty(OperationAnalysisContext context)
    {
        var reference = (IPropertyReferenceOperation)context.Operation;
        if (ZoneTreeAccessPolicy.Execution(reference.Property) && ZoneTreeAccessPolicy.Awaitable(reference.Property.Type))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, reference.Syntax.GetLocation(), reference.Property.ToDisplayString()));
        }
    }

    private static void AnalyzeUsing(OperationAnalysisContext context)
    {
        var resource = context.Operation switch
        {
            IUsingOperation { IsAsynchronous: true } operation => operation.Resources,
            IUsingDeclarationOperation { IsAsynchronous: true } declaration => declaration.DeclarationGroup,
            _ => null
        };
        if (resource is not null && ZoneTreeDispatchAnalysis.RuntimeResource(resource))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, resource.Syntax.GetLocation(), "DisposeAsync"));
        }
    }
}
