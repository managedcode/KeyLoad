using System.Collections.Immutable;
using KeyLoad.Client;
using ManagedCode.Communication;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Deployment;

/// <summary>
/// One SDK client per KeyLoad node and the job's own partition. Every KeyLoad driver uses this so all types
/// address the database the same way; calls rotate over the nodes.
/// </summary>
internal sealed class KeyLoadDatabase : IAsyncDisposable
{
    private const string TenantPrefix = "benchmark-";
    private const string DatabaseName = "benchmark";
    private const string TransactionDomain = "workload";
    private const string PartitionKey = "shared";
    private const string FailedPrefix = "KeyLoad ";
    private readonly HttpClient[] http;
    private readonly KeyLoadClient[] clients;
    private int next = -1;

    internal KeyLoadDatabase(BenchmarkConnection connection)
    {
        var options = Options.Create(new KeyLoadClientExecutionOptions());
        var key = connection.RequireAdminKey();
        http = [.. Enumerable.Range(0, connection.Endpoints.Count).Select(node => connection.CreateHttpClient(node))];
        clients = [.. http.Select(client => new KeyLoadClient(client, key, options))];
        Partition = new(TenantPrefix + Guid.NewGuid().ToString("N"), DatabaseName, TransactionDomain, PartitionKey);
    }

    internal PartitionRef Partition { get; }

    internal string TransactionDomainId => Partition.TransactionDomainId;

    /// <summary>The next node's client; calls spread evenly over the cluster.</summary>
    internal KeyLoadClient Client => clients[(int)((uint)Interlocked.Increment(ref next) % (uint)clients.Length)];

    internal EntityRef Entity(string collection, string id) => new(Partition, collection, id);

    /// <summary>Creates a resource in the job's database before any data is written.</summary>
    internal async Task ConfigureAsync(ResourceDefinition definition, CancellationToken token)
        => Require(await Client.ConfigureResourceAsync(Guid.NewGuid(),
            new(Partition.TenantId, Partition.DatabaseId, definition), token), definition.Name);

    internal ResourceDefinition Resource(string name, ResourceKind kind) => new(name, kind, TransactionDomainId);

    /// <summary>Commits the mutations as one atomic command in the job's partition.</summary>
    internal async Task<CommitReceipt> CommitAsync(ImmutableArray<Mutation> mutations, CancellationToken token)
        => Require(await Client.CommitAsync(new(Guid.NewGuid(), Partition, mutations), token), nameof(CommitAsync));

    internal static T Require<T>(Result<T> result, string operation)
        => result.IsFailed ? throw new InvalidOperationException(FailedPrefix + operation + ": " + Describe(result.Problem))
            : result.Value;

    /// <summary>The server's own problem: status, error code, title and detail.</summary>
    internal static string Describe(Problem? problem) => problem is null ? string.Empty
        : string.Join(" ", new object?[] { problem.StatusCode, problem.ErrorCode, problem.Title, problem.Detail }
            .Select(part => part?.ToString()).Where(part => !string.IsNullOrEmpty(part)));

    public ValueTask DisposeAsync()
    {
        foreach (var client in http)
        {
            client.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}
