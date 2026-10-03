using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record TimeSeriesIntensivePinnedImageContext(string SourceRevision, long RunId, int Attempt,
    string Repository, string Ref, string Event, string Workflow, string Job, string RunnerOs,
    string HostOs, string HostArchitecture, DateTimeOffset ObservedAt)
{
    public string RunUrl => TimeSeriesIntensivePinnedImageProtocol.RunUrlPrefix + RunId.ToString(CultureInfo.InvariantCulture);

    internal static TimeSeriesIntensivePinnedImageContext Read()
    {
        var source = Required(TimeSeriesIntensivePinnedImageProtocol.ShaEnvironment);
        var repository = Required(TimeSeriesIntensivePinnedImageProtocol.RepositoryEnvironment);
        var reference = Required(TimeSeriesIntensivePinnedImageProtocol.RefEnvironment);
        var eventName = Required(TimeSeriesIntensivePinnedImageProtocol.EventEnvironment);
        var workflow = Required(TimeSeriesIntensivePinnedImageProtocol.WorkflowEnvironment);
        var job = Required(TimeSeriesIntensivePinnedImageProtocol.JobEnvironment);
        var runner = Required(TimeSeriesIntensivePinnedImageProtocol.RunnerOsEnvironment);
        Require(OperatingSystem.IsLinux() && Required(TimeSeriesIntensivePinnedImageProtocol.ActionsEnvironment)
            == TimeSeriesIntensivePinnedImageProtocol.True && repository == TimeSeriesIntensivePinnedImageProtocol.Repository
            && reference == TimeSeriesIntensivePinnedImageProtocol.MainRef && runner == TimeSeriesIntensivePinnedImageProtocol.LinuxRunner
            && workflow == TimeSeriesIntensivePinnedImageProtocol.Workflow && job == TimeSeriesIntensivePinnedImageProtocol.Job
            && (eventName == TimeSeriesIntensivePinnedImageProtocol.Push || eventName == TimeSeriesIntensivePinnedImageProtocol.Dispatch)
            && Matches(source, TimeSeriesIntensivePinnedImageProtocol.ShaPattern));
        var run = Required(TimeSeriesIntensivePinnedImageProtocol.RunEnvironment);
        var attempt = Required(TimeSeriesIntensivePinnedImageProtocol.AttemptEnvironment);
        Require(Matches(run, TimeSeriesIntensivePinnedImageProtocol.DecimalPattern)
            && Matches(attempt, TimeSeriesIntensivePinnedImageProtocol.DecimalPattern));
        return new(source, long.Parse(run, CultureInfo.InvariantCulture), int.Parse(attempt, CultureInfo.InvariantCulture),
            repository, reference, eventName, workflow, job, runner, RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(), TimeProvider.System.GetUtcNow());
    }

    internal static string Required(string key) => Environment.GetEnvironmentVariable(key)
        ?? throw new InvalidOperationException(TimeSeriesIntensivePinnedImageProtocol.GitHubMissing);

    internal static bool Matches(string value, string pattern) => Regex.IsMatch(value, pattern,
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(TimeSeriesIntensivePinnedImageProtocol.RegexSeconds));

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException(TimeSeriesIntensivePinnedImageProtocol.GitHubMissing);
        }
    }
}
