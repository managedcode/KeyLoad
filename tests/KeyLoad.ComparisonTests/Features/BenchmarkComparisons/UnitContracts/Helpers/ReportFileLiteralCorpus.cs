using System.Collections.Immutable;
using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class ReportFileLiteralCorpus
{
    internal static ComparisonReport Create()
    {
        var queue = new QueueTimings(1.25, 2.5, 3.75);
        var cases = new[]
        {
            new ComparisonCase("node,\"β\"", Scenario.QueueCycle, 1, "failed", "one timeout", null,
            [
                new OperationSample(0, 2, 1, 2, true, null, 12, "id-α", new(1, 2, 3)),
                new OperationSample(1, 2, 2, 4.5, false, "timeout\n\"Δ\"", 7, "msg,\"β\"", queue)
            ]),
            new ComparisonCase("other", Scenario.PointRead, 0, "failed", "unavailable", null,
            [new OperationSample(2, 0, 4, 5, false, "unavailable", 0, null, null)])
        };
        return new ComparisonReport(3, Guid.Parse("be464719-904b-4ca8-8c14-fb48886f116b"),
            new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), new ComparisonOptions(), "sha", "closed", "linux",
            "x64", 4, ".NET 10", "disk", "revision", ImmutableArray<TargetProfile>.Empty, ImmutableArray.CreateRange(cases));
    }
}
