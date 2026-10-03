using System.Globalization;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveHostSettingsFixture
{
    internal const string RouteKey = "Benchmarks:Profile";
    internal const string FamilyPrefix = "Benchmarks:TimeSeries:";
    internal const string TargetKey = FamilyPrefix + "Target";
    internal const string NodesKey = FamilyPrefix + "NodeCount";
    internal const string PhaseKey = FamilyPrefix + "Phase";
    internal const string ScenarioKey = FamilyPrefix + "Scenario";
    internal const string ProfileKey = FamilyPrefix + "EvidenceProfile";
    internal const string CellKey = FamilyPrefix + "CellId";
    internal const string HashKey = FamilyPrefix + "ContractSha256";
    internal const string SourceKey = "Benchmarks:SourceRevision";
    internal const string ServerImageKey = "Benchmarks:Images:KeyLoad";
    internal const string RunnerImageKey = "Benchmarks:LoadGeneratorImage";
    internal const string OutputKey = "Benchmarks:Output";
    internal const string StorageKey = "Benchmarks:Storage";
    internal const string AdminKey = "Benchmarks:AdminKey";
    internal const string ImageKey = "Benchmarks:Native:Image";
    internal const string EndpointsKey = "Benchmarks:Native:Endpoints";
    internal const string VotersKey = "Benchmarks:Native:VoterIds";
    internal const string IncarnationKey = "Benchmarks:Native:Incarnation";
    internal const string ConnectionKey = "Benchmarks:Native:ConnectionString";
    internal const string JobKey = "KEYLOAD_COMPARISON_JOB_ID";
    internal const string RunKey = "GITHUB_RUN_ID";
    internal const string AttemptKey = "GITHUB_RUN_ATTEMPT";
    internal const string RepositoryKey = "GITHUB_REPOSITORY";
    internal const string RefKey = "GITHUB_REF";
    internal const string WorkflowKey = "GITHUB_WORKFLOW";
    internal const string ShaKey = "GITHUB_SHA";
    internal const string OldTargetKey = "Benchmarks:Target";
    internal const string RouteValue = "timeseries-intensive";
    internal const string ProfileValue = "intensive-timeseries-4096-c16";
    internal const string StorageValue = "Fresh TimeSeries cell-owned native directories; no shared database";
    internal const string OutputValue = "/private/tmp/keyload-th009-reports";
    internal const string Revision = "0123456789abcdef0123456789abcdef01234567";
    internal const string OtherRevision = "1123456789abcdef0123456789abcdef01234567";
    internal const string ContractHash = "5d5fca799172272e1495b62c6f7b104785b394a64926082eef1030b0011e1831";
    internal const string ServerImage = "localhost:5000/keyload/server:test@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string RunnerImage = "localhost:5000/keyload/runner:test@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    internal const string TimescaleImage = "docker.io/timescale/timescaledb:2.30.2-pg18@sha256:e72689191e1c977892c53d6f2c344dbc4a9657a867dc8cc1899229f9d3672b2e";
    internal const string Canary = "PrivateTimeSeriesInputCanary";
    internal const string IncarnationValue = "e1ce6380-a182-4f84-a8c0-883a4cfb4ecd";
    internal const string ConnectionValue = "Host=isolated-timescale-1;Port=5432;Username=benchmark;Password=PrivateTimeSeriesInputCanary;Database=postgres";
    internal const string InvalidCode = "TimeSeriesIntensiveHostSettingsInvalid";
    internal const string MissingFailure = "Invalid settings unexpectedly returned a record.";
    internal const string SettingsName = "TimeSeriesIntensiveHostSettings";
    internal const string NativeName = "TimeSeriesIntensiveHostNativeSettings";
    internal const string HttpsOrigin = "https://node1:8443/";
    internal const string MinimumTcpEndpoint = "tcp://isolated-timescale-1:1/";
    internal const string MaximumTcpEndpoint = "tcp://isolated-timescale-1:65535/";
    internal const string MinimumPortConnection = "Host=isolated-timescale-1;Port=1;Username=benchmark;Password='PrivateTimeSeriesInputCanary;quoted'";
    internal const string MaximumPortConnection = "Host=isolated-timescale-1;Port=65535;Username=benchmark;Password='PrivateTimeSeriesInputCanary;quoted'";
    internal const string GuidN = "N";
    internal const string GuidD = "D";
    internal const string RunText = "37070000000";
    internal const string JobText = "111000000001";
    internal const string AttemptText = "2";
    internal const string RepositoryValue = "managedcode/KeyLoad";
    internal const string RefValue = "refs/heads/main";
    internal const string WorkflowValue = "CI";
    internal const string Separator = ":";
    private const string CellPrefix = "ts-";
    private const string NodeSegment = "-n";
    private const string CellSeparator = "-";
    private const string PreflightId = "preflight";
    private const string KeyLoadId = "keyload";
    private const string TimescaleId = "timescaledb";
    private const string HttpPrefix = "http://node";
    private const string HttpSuffix = ":8080";
    private const string TcpPrefix = "tcp://isolated-timescale-";
    private const string TcpSuffix = ":5432";
    internal const int FirstIndex = 0;
    internal const int OneNode = 1;
    internal const int TwoNodes = 2;
    internal const int ThreeNodes = 3;
    internal const int GuidLength = 32;
    internal const int MinimumPort = 1;
    internal const int MaximumPort = 65_535;
    internal const long RunNumber = 37_070_000_000;
    internal const long JobNumber = 111_000_000_001;
    internal const int AttemptNumber = 2;

    internal static Dictionary<string, string?> Values(TimeSeriesIntensiveTargetKind target,
        int count = TwoNodes, TimeSeriesIntensiveScenario? scenario = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [RouteKey] = RouteValue,
            [TargetKey] = target.ToString(),
            [NodesKey] = count.ToString(CultureInfo.InvariantCulture),
            [PhaseKey] = scenario is null ? nameof(TimeSeriesIntensiveCellPhase.Preflight) : nameof(TimeSeriesIntensiveCellPhase.Intensive),
            [ProfileKey] = ProfileValue,
            [CellKey] = CellId(target, count, scenario),
            [HashKey] = ContractHash,
            [SourceKey] = Revision,
            [ShaKey] = Revision,
            [ServerImageKey] = ServerImage,
            [RunnerImageKey] = RunnerImage,
            [OutputKey] = OutputValue,
            [StorageKey] = StorageValue,
            [JobKey] = JobText,
            [RunKey] = RunText,
            [AttemptKey] = AttemptText,
            [RepositoryKey] = RepositoryValue,
            [RefKey] = RefValue,
            [WorkflowKey] = WorkflowValue,
            [ImageKey] = target == TimeSeriesIntensiveTargetKind.KeyLoad ? ServerImage : TimescaleImage
        };
        if (scenario is not null)
        {
            values[ScenarioKey] = scenario.ToString();
        }
        AddNative(values, target, count);
        return values;
    }

    internal static ConfigurationManager Configuration(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(values);
        return configuration;
    }

    internal static string CellId(TimeSeriesIntensiveTargetKind target, int count, TimeSeriesIntensiveScenario? scenario)
        => string.Concat(CellPrefix, target == TimeSeriesIntensiveTargetKind.KeyLoad ? KeyLoadId : TimescaleId,
            NodeSegment, count.ToString(CultureInfo.InvariantCulture), CellSeparator, scenario?.ToString() ?? PreflightId);

    internal static string IndexKey(string key, int index) => key + Separator + index.ToString(CultureInfo.InvariantCulture);
    internal static string Voter(int index) => HttpPrefix + (index + OneNode).ToString(CultureInfo.InvariantCulture) + HttpSuffix;
    internal static string Endpoint(TimeSeriesIntensiveTargetKind target, int index)
        => target == TimeSeriesIntensiveTargetKind.KeyLoad ? Voter(index)
            : TcpPrefix + (index + OneNode).ToString(CultureInfo.InvariantCulture) + TcpSuffix;

    private static void AddNative(Dictionary<string, string?> values, TimeSeriesIntensiveTargetKind target, int count)
    {
        for (var index = FirstIndex; index < count; index++)
        {
            values[IndexKey(EndpointsKey, index)] = Endpoint(target, index);
            if (target == TimeSeriesIntensiveTargetKind.KeyLoad)
            {
                values[IndexKey(VotersKey, index)] = Voter(index);
            }
        }
        if (target == TimeSeriesIntensiveTargetKind.KeyLoad)
        {
            values[IncarnationKey] = IncarnationValue;
            values[AdminKey] = Canary;
        }
        else
        {
            values[ConnectionKey] = ConnectionValue;
        }
    }
}
