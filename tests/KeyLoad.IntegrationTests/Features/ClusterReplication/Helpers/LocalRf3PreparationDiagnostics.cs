using Aspire.Hosting;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Passively observes only the original preparation application.</summary>
internal sealed class LocalRf3PreparationDiagnostics : IAsyncDisposable
{
    private const int MaximumRecords = 128;
    private readonly Lock gate = new();
    private readonly Queue<string> records = new();
    private readonly CancellationTokenSource lifetime = new();
    private Task? observation;
    private DistributedApplication? app;
    private LocalImageVerifierPhase verifierPhase;

    internal void RecordVerifierPhase(LocalImageVerifierPhase phase)
    {
        lock (gate)
        { verifierPhase = phase; }
    }

    internal void Record(string record)
    {
        lock (gate)
        {
            records.Enqueue(record);
            while (records.Count > MaximumRecords)
            { records.Dequeue(); }
        }
    }

    internal void Attach(DistributedApplication owned)
    {
        app = owned;
        observation = ObserveAsync(owned);
    }

    internal void SaveFailure(Exception primary, CancellationToken caller)
    {
        var owned = app;
        var stopping = owned?.Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping.IsCancellationRequested;
        var lines = new List<string>
        {
            $"Preparation failure kind={LocalRf3PreparationLogger.FailureKind(primary)}",
            $"CallerCancelled={caller.IsCancellationRequested}; HostStopping={stopping}",
            $"VerifierStage={verifierPhase}",
            "Stopping cause requires an original host event or resource exit; otherwise unobserved.",
            "Original prerequisite logs use the joined TestSuiteOutput stream; missing output is unobserved.",
            "Original producer build-output sidecar remains retained by its owning producer."
        };
        if (owned is not null && owned.ResourceNotifications.TryGetCurrentState(
            LocalRf3ImagePrerequisite.ResourceName, out var current))
        {
            lines.Add(State(current.Snapshot.State?.Text, current.Snapshot.ExitCode));
        }
        else
        { lines.Add("Preparation resource state unobserved."); }
        lock (gate)
        { lines.AddRange(records); }
        foreach (var line in BoundedDiagnosticLog.Bound(lines))
        { Console.Error.WriteLine(line); }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            if (observation is not null)
            { await observation.ConfigureAwait(false); }
        }
        finally { lifetime.Dispose(); }
    }

    private async Task ObserveAsync(DistributedApplication owned)
    {
        try
        {
            await foreach (var update in owned.ResourceNotifications.WatchAsync(lifetime.Token)
                .ConfigureAwait(false))
            {
                if (update.Resource.Name == LocalRf3ImagePrerequisite.ResourceName)
                { Record(State(update.Snapshot.State?.Text, update.Snapshot.ExitCode)); }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private static string State(string? state, int? exit)
    {
        var category = state is "Starting" or "Running" or "Stopping" or "Exited"
            or "Finished" or "FailedToStart" or "RuntimeUnhealthy" or "NotStarted" ? state : "OtherOrUnavailable";
        return $"Preparation resource state={category}; exit={exit}";
    }
}
