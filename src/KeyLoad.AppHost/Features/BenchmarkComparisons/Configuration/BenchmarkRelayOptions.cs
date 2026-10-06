using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

/// <summary>Optional benchmark cohort metadata forwarded from validated native host options.</summary>
[ConfigurationOptions]
internal sealed record BenchmarkRelayOptions
{
    private const string EvidenceProfileKey = "Benchmarks:EvidenceProfile";
    private const string SourceRevisionKey = "Benchmarks:SourceRevision";
    private const string CellKey = "Benchmarks:TimeSeries:CellId";
    private const string ContractKey = "Benchmarks:TimeSeries:ContractSha256";
    [ConfigurationKeyName(EvidenceProfileKey)]
    public string? EvidenceProfile { get; init; }
    [ConfigurationKeyName(SourceRevisionKey)]
    public string? SourceRevision { get; init; }
    [ConfigurationKeyName(CellKey)]
    public string? TimeSeriesCellId { get; init; }
    [ConfigurationKeyName(ContractKey)]
    public string? TimeSeriesContractSha256 { get; init; }
}
