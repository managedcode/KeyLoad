using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.Comparisons;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Requires real native process identities before opening a KeyLoad workload client.</summary>
[ConfigurationBinding]
internal static class BenchmarkSessionProcessEvidence
{
    internal const string ServerSetting = "Benchmarks:Native:Processes";
    private const string ClientName = "comparison-client";
    private const string KeyLoad = "KeyLoad";
    private const int MaximumCharacters = 131_072;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static BenchmarkSessionProcesses Read(IConfiguration configuration, BenchmarkSessionSelection selection)
    {
        var client = ProjectProcessEvidence.ReadCurrent(ClientName);
        if (selection.Target != KeyLoad)
        {
            return new(client, []);
        }
        var text = configuration[ServerSetting];
        if (text is null || text.Length > MaximumCharacters)
        {
            throw new InvalidDataException("The KeyLoad workload requires observed native server processes.");
        }
        var servers = JsonSerializer.Deserialize<ImmutableArray<NativeProcessEvidence>>(text, Json);
        if (servers.IsDefault || servers.Length != selection.NodeCount
            || servers.Select(server => server.Name).Distinct(StringComparer.Ordinal).Count() != servers.Length
            || servers.Select(server => server.ProcessId).Distinct().Count() != servers.Length
            || servers.Any(server => server.AspireStartedAtUtc is null))
        {
            throw new InvalidDataException("The observed native server process set does not match the selected topology.");
        }
        foreach (var server in servers)
        {
            ProjectProcessEvidence.Verify(server);
        }
        return new(client, servers);
    }
}
