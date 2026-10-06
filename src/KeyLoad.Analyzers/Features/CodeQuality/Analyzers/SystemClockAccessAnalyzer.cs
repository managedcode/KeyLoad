using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

/// <summary>Requires TimeProvider for direct system clock access.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SystemClockAccessAnalyzer : DiagnosticAnalyzer
{
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
        System.ArgumentNullException.ThrowIfNull(context);
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzePropertyReference, Microsoft.CodeAnalysis.OperationKind.PropertyReference);
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context)
    {
        var property = ((IPropertyReferenceOperation)context.Operation).Property;
        if (!property.IsStatic || !IsCurrentTimeProperty(property))
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
}
