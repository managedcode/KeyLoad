using System.Diagnostics;
using KeyLoad.Client;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Owns the TimeSeries target and HTTP client lifetimes while the comparison runs.</summary>
internal sealed class TimeSeriesComparisonTargetOwner(IOptions<KeyLoadClientExecutionOptions> clientOptions,
    IOptions<ComparisonLifecycleOptions> lifecycleOptions) : IAsyncDisposable
{
    private readonly List<ITimeSeriesPersistentTarget> targets = [];
    private readonly List<HttpClient> unownedClients = [];
    private ITimeSeriesPersistentTarget? pendingTarget;

    internal ITimeSeriesPersistentTarget[] CreateTargets(Uri endpoint, string adminKey, string connectionString,
        string timescaleImage, string keyLoadBuildIdentity)
    {
        pendingTarget = new TimescaleTimeSeriesTarget(connectionString, lifecycleOptions, timescaleImage);
        PublishPendingTarget();
        var client = CreateClient(endpoint);
        pendingTarget = new KeyLoadTimeSeriesTarget(client, adminKey, clientOptions, keyLoadBuildIdentity);
        PublishPendingTarget(client);
        return [.. targets];
    }

    public async ValueTask DisposeAsync()
    {
        const int FirstOwnerIndex = 0;

        try
        {
            await DisposeTargetsAsync(FirstOwnerIndex);
        }
        finally
        {
            try
            {
                await DisposePendingTargetAsync();
            }
            finally
            {
                await DisposeUnownedClientsAsync(FirstOwnerIndex);
                targets.Clear();
                unownedClients.Clear();
                pendingTarget = null;
            }
        }
    }

    private async Task DisposeTargetsAsync(int index)
    {
        const int NextOwnerOffset = 1;

        if (index >= targets.Count)
        {
            return;
        }

        try
        {
            await targets[index].DisposeAsync();
        }
        finally
        {
            await DisposeTargetsAsync(index + NextOwnerOffset);
        }
    }

    private async Task DisposePendingTargetAsync()
    {
        if (pendingTarget is not null)
        {
            await pendingTarget.DisposeAsync();
        }
    }

    private async Task DisposeUnownedClientsAsync(int index)
    {
        const int NextOwnerOffset = 1;

        if (index >= unownedClients.Count)
        {
            return;
        }

        try
        {
            unownedClients[index].Dispose();
        }
        finally
        {
            await DisposeUnownedClientsAsync(index + NextOwnerOffset);
        }
    }

    private HttpClient CreateClient(Uri endpoint)
    {
        var client = new HttpClient { BaseAddress = endpoint, Timeout = Timeout.InfiniteTimeSpan };
        unownedClients.Add(client);
        return client;
    }

    private void PublishPendingTarget(HttpClient? transferredClient = null)
    {
        var target = pendingTarget ?? throw new UnreachableException();
        if (transferredClient is not null)
        {
            unownedClients.Remove(transferredClient);
        }

        targets.Add(target);
        pendingTarget = null;
    }
}
