using System.Globalization;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedKeyLoadFaultRegressionProtocol
{
    internal const string Admin = "admin-key";
    internal const string DataMount = "/data";
    internal const string Running = "running";
    internal const string Exited = "exited";
    internal const string Failure = "IsolatedKeyLoadFaultRegressionFailed";
    internal const string EvidenceFile = "keyload-fault.json";

    internal static string Resource(int node) => "node" + node.ToString(CultureInfo.InvariantCulture);
    internal static string Voter(int node) => "http://" + Resource(node) + ":8080";

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
