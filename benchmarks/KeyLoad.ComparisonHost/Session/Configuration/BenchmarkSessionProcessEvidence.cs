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
    private const string NativeServersRequired = "The KeyLoad workload requires observed native server processes.";
    private const string NativeServerTopologyMismatch = "The observed native server process set does not match the selected topology.";
    private const int MinimumProcessId = 1;
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
            throw new InvalidDataException(NativeServersRequired);
        }
        var servers = JsonSerializer.Deserialize<ImmutableArray<NativeProcessEvidence>>(text, Json);
        if (servers.IsDefault || servers.Length != selection.NodeCount
            || servers.Select(server => server.Name).Distinct(StringComparer.Ordinal).Count() != servers.Length
            || servers.Select(server => server.ProcessId).Distinct().Count() != servers.Length
            || servers.Select(server => server.AspireProcessId).Distinct().Count() != servers.Length
            || servers.Any(server => server.AspireStartedAtUtc is null || server.AspireProcessId is not >= MinimumProcessId
                || server.AspireProcessStartedAtUtc is null
                || server.AspireProcessStartedAtUtc > server.StartedAtUtc))
        {
            throw new InvalidDataException(NativeServerTopologyMismatch);
        }
        foreach (var server in servers)
        {
            ProjectProcessEvidence.Verify(server);
        }
        return new(client, servers);
    }
}
