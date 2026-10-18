using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal sealed record AggregateTypeCodeMeasurement(
    INamedTypeSymbol Type,
    int CodeLines,
    Location Location);
