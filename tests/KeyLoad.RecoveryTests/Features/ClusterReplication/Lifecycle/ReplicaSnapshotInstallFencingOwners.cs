using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Server;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal sealed class ReplicaSnapshotInstallFencingOwners : IAsyncDisposable
{
    private ReplicaProcessTrial? trial;
    private ReplicaCrashNode? source;
    private ReplicaCrashNode? target;
    private ReplicaMaterializer? materializer;
    private bool uncertain;
    private bool targetBorrowUncertain;
    private bool materializerAttempted;
    private bool trialAttempted;
    private bool preserveEvidence;
    private readonly HashSet<ReplicaCrashNode> attempted = [];
    internal void RetainEvidence() => preserveEvidence = true;
    internal ReplicaProcessTrial Trial => trial ?? throw new InvalidOperationException(ReplicaProtocol.SnapshotUnavailable);
    internal ReplicaProcessTrial Start()
    {
        try
        {
            return trial = ReplicaProcessTrial.Start(ReplicaCrashBoundary.SnapshotChunkAcknowledged);
        }
        catch (Exception)
        {
            uncertain = true;
            throw;
        }
    }
    internal ReplicaCrashNode OpenSource()
    {
        if (uncertain || source is not null)
        {
            throw new InvalidOperationException(ReplicaProtocol.SnapshotUnavailable);
        }
        try
        {
            return source = ReplicaCrashNode.OpenSource(Trial.DirectoryPath, Trial.Incarnation);
        }
        catch (Exception)
        {
            uncertain = true;
            throw;
        }
    }
    internal async Task<T> TargetAsync<T>(Func<ReplicaCrashNode, Task<T>> operation)
    {
        if (uncertain || target is not null || materializer is not null)
        {
            throw new InvalidOperationException(ReplicaProtocol.SnapshotUnavailable);
        }
        var failures = new List<Exception>();
        T result = default!;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                target = ReplicaCrashNode.OpenTarget(Trial.DirectoryPath, Trial.Incarnation);
            }
            catch (Exception)
            {
                uncertain = true;
                throw;
            }
            result = await operation(target).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        await CloseMaterializerAsync(failures).ConfigureAwait(false);
        CloseTarget(failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }
    internal Task TargetAsync(Func<ReplicaCrashNode, Task> operation)
        => TargetAsync(async node =>
        {
            await operation(node).ConfigureAwait(false);
            return true;
        });
    internal async Task MaterializerAsync(ReplicaCrashNode node, Func<ReplicaMaterializer, Task> operation)
    {
        if (uncertain || !ReferenceEquals(node, target) || materializer is not null)
        {
            throw new InvalidOperationException(ReplicaProtocol.SnapshotUnavailable);
        }
        var failures = new List<Exception>();
        materializerAttempted = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                materializer = new(node.Database, node.Log, node.Snapshots, RecoveryExecutionOptions.Replica());
            }
            catch (Exception)
            {
                uncertain = true;
                targetBorrowUncertain = true;
                throw;
            }
            await operation(materializer).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        await CloseMaterializerAsync(failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private async Task CloseMaterializerAsync(List<Exception> failures)
    {
        if (materializer is null || materializerAttempted)
        {
            return;
        }
        materializerAttempted = true;
        var closed = false;
        try
        {
            await materializer.DisposeAsync().ConfigureAwait(false);
            closed = true;
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            failures.Add(failure);
        }
        if (closed)
        {
            materializer = null;
        }
        uncertain |= !closed;
    }
    private void CloseTarget(List<Exception> failures)
    {
        if (materializer is not null || targetBorrowUncertain || target is null || !attempted.Add(target))
        {
            return;
        }
        var closed = false;
        ServerFailureObserver.Observe(() =>
        {
            target.Dispose();
            closed = true;
        }, failures);
        if (closed)
        {
            target = null;
        }
        uncertain |= !closed;
    }
    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await CloseMaterializerAsync(failures).ConfigureAwait(false);
        CloseTarget(failures);
        if (source is not null && attempted.Add(source))
        {
            try
            {
                source.Dispose();
                source = null;
            }
            catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
            {
                uncertain = true;
                failures.Add(failure);
            }
            catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
            {
                uncertain = true;
                failures.Add(failure);
            }
        }
        if (trial is not null && !trialAttempted)
        {
            trialAttempted = true;
            try
            {
                if (preserveEvidence || uncertain || failures.Count != 0)
                {
                    await trial.DisposeAsync(retainDirectory: true).ConfigureAwait(false);
                }
                else
                {
                    await trial.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
            {
                failures.Add(failure);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
