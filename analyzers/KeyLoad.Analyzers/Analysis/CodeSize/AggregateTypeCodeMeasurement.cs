using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.CodeQuality;

internal sealed class AggregateTypeCodeMeasurement(INamedTypeSymbol type, int codeLines, Location location)
{
    internal INamedTypeSymbol Type { get; } = type;
    internal int CodeLines { get; } = codeLines;
    internal Location Location { get; } = location;
}
