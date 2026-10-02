using System;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class CodeQualityAssemblyNames
{
    public const string OrleansContracts = "KeyLoad.Orleans";
    public const string Analyzer = "KeyLoad.Analyzers";
    private const string ProductionAssemblyPrefix = "KeyLoad.";
    private const string TestAssemblyMarker = "Tests";

    public static bool IsProduction(string? assemblyName) =>
        assemblyName?.StartsWith(ProductionAssemblyPrefix, StringComparison.Ordinal) == true &&
        !assemblyName.Contains(TestAssemblyMarker, StringComparison.Ordinal) &&
        !string.Equals(assemblyName, Analyzer, StringComparison.Ordinal);
}
