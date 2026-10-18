using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

internal sealed record ComparisonSessionCloseResult(bool ThresholdExpired, ImmutableArray<Exception> Failures);
