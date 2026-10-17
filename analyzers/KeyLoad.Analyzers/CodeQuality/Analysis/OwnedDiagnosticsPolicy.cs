using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class OwnedDiagnosticsPolicy
{
    private const string AssemblyName = "KeyLoad.Diagnostics";
    private const string Bank = "KeyLoad.Diagnostics.Features.ResourceExecution.DatabasePhaseBank";
    private const string Telemetry = "KeyLoad.Diagnostics.Features.ResourceExecution.DatabasePhaseTelemetry";
    private const string Initialize = "Initialize";
    private const string StripeCount = "stripeCount";
    private const string MaximumCasAttempts = "maximumCasAttempts";

    internal static bool IsMethod(Compilation compilation, IMethodSymbol method) =>
        method.ContainingAssembly.Name == AssemblyName &&
        (method.MethodKind == MethodKind.Constructor &&
            MagicRuntimeOperations.IsNativeMetadataType(compilation, method.ContainingType, Bank) ||
         method.Name == Initialize &&
            MagicRuntimeOperations.IsNativeMetadataType(compilation, method.ContainingType, Telemetry));

    internal static bool IsArgument(IArgumentOperation argument) =>
        argument.Parameter is { Type.SpecialType: SpecialType.System_Int32 } parameter &&
        parameter.Name is StripeCount or MaximumCasAttempts;
}
