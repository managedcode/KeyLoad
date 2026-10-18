using System.Globalization;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedKeyLoadFaultChecksProtocol
{
    internal const string Admin = "admin-key";
    internal const string DataMount = "/data";
    internal const string Running = "running";
    internal const string Exited = "exited";
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
