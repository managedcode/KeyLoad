using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Drop-only profile over the existing real two-RF3-group application owners.</summary>
internal sealed class NativeDropRuntimeProfile : IAsyncDisposable
{
    private readonly List<Task> originals = [];
    private readonly List<(ReadExecutionBudget Work, ReadExecutionBudgetReadGrant Metadata)> metadataOwners = [];
    private readonly Lock lifecycle = new();
    private Task? disposal;
    private bool retain;
    internal ControlledPartitionMovementNativeHosts Hosts
    {
        get;
    }
    internal NativeDropCommandPhaseObserver Observer { get; }
    internal ControlledPartitionMovementNativeHost Source => Hosts.Source;
    internal ResourceOwnerInventoryNativeClient Client => Source.Silo.ResourceInventoryRuntime!.Client;

    private NativeDropRuntimeProfile(ControlledPartitionMovementNativeHosts hosts, NativeDropCommandPhaseObserver observer)
    {
        Hosts = hosts;
        Observer = observer;
    }

    internal static async Task<NativeDropRuntimeProfile> CreateAsync(CancellationToken token)
    {
        var observer = new NativeDropCommandPhaseObserver();
        var hosts = await ControlledPartitionMovementNativeHosts.CreateWithPhaseObserverAsync(_ => observer, token).ConfigureAwait(false);
        var profile = new NativeDropRuntimeProfile(hosts, observer);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() =>
        {
            profile.Attach();
            _ = profile.Source.Application.Services.GetRequiredService<INativeDropTableExecution>();
            return Task.CompletedTask;
        }, failures).ConfigureAwait(false);
        if (failures.Count != NativeDropRuntimeProtocol.NoFailures)
        {
            profile.RetainRoots();
            await ServerFailureObserver.ObserveAsync(() => profile.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return profile;
    }

    private void Attach()
    {
        foreach (var node in Hosts.Nodes)
        {
            var codec = node.Silo.CatalogRequestCodec();
            if (!ReferenceEquals(codec.PhaseObserver, Observer))
            {
                throw new InvalidOperationException(NativeDropRuntimeProtocol.ObserverAlreadyOwned);
            }
        }
    }

    internal T Track<T>(T original) where T : Task
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposal is not null, this);
            originals.Add(original);
        }
        return original;
    }

    internal ReadExecutionBudgetReadGrant ReserveMetadata(ReadExecutionBudget work)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposal is not null, this);
            var metadata = work.CreateReadGrant(work.RemainingReadGrantBytes / NativeDropRuntimeProtocol.MetadataHeadroomDivisor,
                work.RemainingReadGrantRecords / NativeDropRuntimeProtocol.MetadataHeadroomDivisor);
            metadataOwners.Add((work, metadata));
            return metadata;
        }
    }

    internal void RetainRoots()
    {
        retain = true;
        Hosts.RetainRoots();
    }

    internal async Task RestartAsync(CancellationToken token)
    {
        await Hosts.RestartAsync(token).ConfigureAwait(false);
        Attach();
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            return new(disposal ??= CloseAsync());
        }
    }

    private async Task CloseAsync()
    {
        Observer.ReleaseAll();
        var failures = new List<Exception>();
        foreach (var original in originals)
        {
            await ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false);
        }

        foreach (var owner in metadataOwners)
        {
            ServerFailureObserver.Observe(() => owner.Work.CompleteSettledReadGrant(owner.Metadata), failures);
        }

        if (failures.Count != NativeDropRuntimeProtocol.NoFailures || retain)
        {
            Hosts.RetainRoots();
        }

        await ServerFailureObserver.ObserveAsync(() => Hosts.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
