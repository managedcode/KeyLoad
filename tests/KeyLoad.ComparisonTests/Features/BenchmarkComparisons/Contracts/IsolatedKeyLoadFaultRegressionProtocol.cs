using System.Globalization;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedKeyLoadFaultRegressionProtocol
{
    internal const int OverallMinutes = 10;
    internal const int CliSeconds = 30;
    internal const int ExitedSeconds = 20;
    internal const int RestartSeconds = 120;
    internal const int AttemptSeconds = 30;
    internal const int ReadinessSeconds = 90;
    internal const int PollMilliseconds = 250;
    internal const int DrainSeconds = 5;
    internal const int OutputCharacters = 16_384;
    internal const int ErrorCharacters = 2_048;
    internal const string Admin = "admin-key";
    internal const string DataMount = "/data";
    internal const string Running = "running";
    internal const string Exited = "exited";
    internal const string Failure = "IsolatedKeyLoadFaultRegressionFailed";
    internal const string EvidenceFile = "keyload-fault.json";

    internal static string Resource(int node) => "node" + node.ToString(CultureInfo.InvariantCulture);
    internal static string Voter(int node) => "http://" + Resource(node) + ":8080";

    internal static CancellationTokenSource Deadline(int seconds, CancellationToken token)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        return deadline;
    }

    internal static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException(Failure);
        }
    }
}
