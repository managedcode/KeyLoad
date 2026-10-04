using System.Collections.Immutable;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

internal static class TimeSeriesIntensiveHostConstants
{
    private const string FamilyPrefix = "Benchmarks:TimeSeries:";
    private const string NativePrefix = "Benchmarks:Native:";
    internal const string NativeSection = "Benchmarks:Native";
    internal const string ImageField = "Image";
    internal const string EndpointsField = "Endpoints";
    internal const string VoterIdsField = "VoterIds";
    internal const string IncarnationField = "Incarnation";
    internal const string ConnectionStringField = "ConnectionString";
    internal const string CellId = FamilyPrefix + "CellId";
    internal const string ContractSha256 = FamilyPrefix + "ContractSha256";
    internal const string Image = NativePrefix + ImageField;
    internal const string Endpoints = NativePrefix + EndpointsField;
    internal const string VoterIds = NativePrefix + VoterIdsField;
    internal const string Incarnation = NativePrefix + IncarnationField;
    internal const string ConnectionString = NativePrefix + ConnectionStringField;
    internal const string JobId = "KEYLOAD_COMPARISON_JOB_ID";
    internal const string InvalidCode = "TimeSeriesIntensiveHostSettingsInvalid";
    internal const string StorageDescription = "Fresh TimeSeries cell-owned native directories; no shared database";
    internal const string GuidD = "D";
    internal const string RootPath = "/";
    internal const string TcpScheme = "tcp";
    internal const char CarriageReturn = '\r';
    internal const char LineFeed = '\n';
    internal const char HostSeparator = ',';
    internal const int FirstIndex = 0;
    internal const int OverflowLookahead = 1;
    internal const int MinimumPort = 1;
    internal const int MaximumPort = 65_535;
    internal static readonly ImmutableArray<string> KeyLoadNativeFields = [ImageField, EndpointsField, VoterIdsField, IncarnationField];
    internal static readonly ImmutableArray<string> TimescaleNativeFields = [ImageField, EndpointsField, ConnectionStringField];
}
