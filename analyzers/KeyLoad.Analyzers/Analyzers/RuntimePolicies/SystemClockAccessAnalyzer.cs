using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.CodeQuality;

/// <summary>Requires TimeProvider for direct system clock access.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SystemClockAccessAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticsNamespace = "System.Diagnostics";
    private const string EnvironmentType = "Environment";
    private const string TickCountProperty = "TickCount";
    private const string TickCount64Property = "TickCount64";

    /// <summary>Stable identifier for this analyzer's diagnostic.</summary>
    public const string DiagnosticId = "KLD0022";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        CodeQualityDiagnosticText.SystemClockTitle,
        CodeQualityDiagnosticText.SystemClockMessage,
        CodeQualityDiagnosticCategories.Reliability,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: CodeQualityDiagnosticText.SystemClockDescription);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new System.ArgumentNullException(nameof(context));
        }
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzePropertyReference, Microsoft.CodeAnalysis.OperationKind.PropertyReference);
        context.RegisterOperationAction(AnalyzeFieldReference, Microsoft.CodeAnalysis.OperationKind.FieldReference);
        context.RegisterOperationAction(AnalyzeInvocation, Microsoft.CodeAnalysis.OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeCreation, Microsoft.CodeAnalysis.OperationKind.ObjectCreation);
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context)
    {
        if (context.Operation.Parent is INameOfOperation)
        {
            return;
        }
        var property = ((IPropertyReferenceOperation)context.Operation).Property;
        if (!IsStopwatch(property.ContainingType) && (!property.IsStatic || !IsCurrentTimeProperty(property)))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            context.Operation.Syntax.GetLocation(),
            property.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    private static bool IsCurrentTimeProperty(IPropertySymbol property)
    {
        if (property.ContainingType.Name == EnvironmentType &&
            property.ContainingType.ContainingNamespace.ToDisplayString() == CodeQualitySourceNames.SystemNamespace)
        {
            return property.Name is TickCountProperty or TickCount64Property;
        }
        if (property.ContainingType.SpecialType == SpecialType.System_DateTime)
        {
            return property.Name is nameof(System.DateTime.Now) or
                nameof(System.DateTime.Today) or
                nameof(System.DateTime.UtcNow);
        }

        return property.ContainingType.Name == nameof(System.DateTimeOffset) &&
               property.ContainingType.ContainingNamespace.ToDisplayString() ==
               CodeQualitySourceNames.SystemNamespace &&
               property.Name is nameof(System.DateTimeOffset.Now) or nameof(System.DateTimeOffset.UtcNow);
    }

    private static void AnalyzeFieldReference(OperationAnalysisContext context)
    {
        var field = ((IFieldReferenceOperation)context.Operation).Field;
        if (IsStopwatch(field.ContainingType))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation(),
                field.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var method = ((IInvocationOperation)context.Operation).TargetMethod;
        if (IsStopwatch(method.ContainingType))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation(),
                method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }
    }

    private static void AnalyzeCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Type is INamedTypeSymbol type && IsStopwatch(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation(),
                type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }
    }

    private static bool IsStopwatch(INamedTypeSymbol type) => type.Name == nameof(System.Diagnostics.Stopwatch) &&
        type.ContainingNamespace.ToDisplayString() == DiagnosticsNamespace;
}
