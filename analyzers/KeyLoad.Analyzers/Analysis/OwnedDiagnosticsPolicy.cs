using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.CodeQuality;

internal static class OwnedDiagnosticsPolicy
{
    private const string AssemblyName = "KeyLoad.Diagnostics";
    private const string Bank = "KeyLoad.Diagnostics.ResourceExecution.DatabasePhaseBank";
    private const string Telemetry = "KeyLoad.Diagnostics.ResourceExecution.DatabasePhaseTelemetry";
    private const string Initialize = "Initialize";
    private const string StripeCount = "stripeCount";
    private const string MaximumCasAttempts = "maximumCasAttempts";

    internal static bool IsMethod(Compilation compilation, IMethodSymbol method) =>
        method.ContainingAssembly.Name == AssemblyName &&
        (method.MethodKind == MethodKind.Constructor &&
            MagicRuntimeOperations.IsMetadataType(compilation, method.ContainingType, Bank) ||
         method.Name == Initialize &&
            MagicRuntimeOperations.IsMetadataType(compilation, method.ContainingType, Telemetry));

    internal static bool IsArgument(IArgumentOperation argument) =>
        argument.Parameter is { Type.SpecialType: SpecialType.System_Int32 } parameter &&
        parameter.Name is StripeCount or MaximumCasAttempts;
}
