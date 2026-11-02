using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.CodeQuality;

internal static class ObjectConversionAnalysis
{
    internal static void Analyze(OperationAnalysisContext context, DiagnosticDescriptor rule)
    {
        var conversion = (IConversionOperation)context.Operation;
        var source = conversion.Operand.Type;
        var target = conversion.Type;
        if (source is null || target is null || conversion.Conversion.IsIdentity || conversion.Conversion.IsUserDefined
            || !UntypedOperationSite.IsRuntime(conversion) || UntypedSiteReason.Present(conversion.Syntax, rule.Id))
        {
            return;
        }
        var boxing = Scalar(target)
            && (source.IsValueType || source.TypeKind == TypeKind.TypeParameter && !source.IsReferenceType);
        var objectCast = Scalar(source) && !Scalar(target);
        if (boxing || objectCast)
        {
            context.ReportDiagnostic(Diagnostic.Create(rule, conversion.Syntax.GetLocation(),
                boxing ? BoxingKind(source) : UntypedContractPolicy.ObjectCast,
                source.ToDisplayString(), target.ToDisplayString()) /* KLD0041: Roslyn requires object message arguments to report the conversion kind and its two resolved type names. */);
        }
    }

    internal static void AnalyzePattern(OperationAnalysisContext context, DiagnosticDescriptor rule)
    {
        var operation = (IIsPatternOperation)context.Operation;
        if (operation.Value.Type is not { } source || !Scalar(source)
            || !UntypedOperationSite.IsRuntime(operation) || UntypedSiteReason.Present(operation.Syntax, rule.Id))
        {
            return;
        }
        var pattern = operation.Pattern;
        while (pattern is INegatedPatternOperation negated)
        {
            pattern = negated.Pattern;
        }
        if (pattern is IDeclarationPatternOperation { DeclaredSymbol: not null, MatchedType: { } target }
            && !Scalar(target))
        {
            context.ReportDiagnostic(Diagnostic.Create(rule, operation.Syntax.GetLocation(),
                UntypedContractPolicy.ObjectCast, source.ToDisplayString(), target.ToDisplayString()) /* KLD0041: Roslyn requires object message arguments to report this captured pattern and its resolved source and target types. */);
        }
    }

    private static string BoxingKind(ITypeSymbol source)
        => source.IsValueType ? UntypedContractPolicy.Boxing : UntypedContractPolicy.PotentialBoxing;

    private static bool Scalar(ITypeSymbol type)
        => type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.Dynamic;
}
