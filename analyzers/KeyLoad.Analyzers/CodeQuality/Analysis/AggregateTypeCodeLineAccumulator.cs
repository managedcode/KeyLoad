using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal sealed class AggregateTypeCodeLineAccumulator
{
    private readonly ConcurrentDictionary<INamedTypeSymbol, AggregateTypeCodeMeasurement> measurements =
        new(SymbolEqualityComparer.Default);

    internal void Add(INamedTypeSymbol type, int codeLines, Location location)
    {
        measurements.AddOrUpdate(
            type,
            static (symbol, state) => new AggregateTypeCodeMeasurement(symbol, state.CodeLines, state.Location),
            static (symbol, current, state) => new AggregateTypeCodeMeasurement(
                symbol,
                current.CodeLines + state.CodeLines,
                EarlierLocation(current.Location, state.Location)),
            (CodeLines: codeLines, Location: location));
    }

    internal AggregateTypeCodeMeasurement[] Snapshot() => measurements.Values.ToArray();

    private static Location EarlierLocation(Location current, Location candidate)
    {
        var pathOrder = StringComparer.Ordinal.Compare(
            current.SourceTree?.FilePath, candidate.SourceTree?.FilePath);
        if (pathOrder < 0 || (pathOrder == 0 &&
            current.SourceSpan.Start <= candidate.SourceSpan.Start))
        {
            return current;
        }

        return candidate;
    }
}

internal sealed record AggregateTypeCodeMeasurement(
    INamedTypeSymbol Type,
    int CodeLines,
    Location Location);
