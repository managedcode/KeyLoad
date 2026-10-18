using System.Globalization;
using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedKeyLoadFaultChecksProtocol
{
    internal const string Admin = "admin-key";
    internal const string Running = KnownResourceStates.Running;
    internal const string Failure = "IsolatedKeyLoadFaultChecksFailed";
    internal const string EvidenceFile = "keyload-fault.json";

    internal static string Resource(int node) => "node" + node.ToString(CultureInfo.InvariantCulture);

    internal static IsolatedKeyLoadFaultDeadline Deadline(TimeSpan budget, CancellationToken token)
        => new(budget, token);

    internal static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException(Failure);
        }
    }
}
