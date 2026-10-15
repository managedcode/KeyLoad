using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.ClusterReplication.SnapshotManifestEntryProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class SnapshotManifestEntryOwners
{
    internal string Root { get; } = ReplicaFixturePaths.NewDirectory(P.Prefix);
    internal Guid Incarnation { get; } = Guid.NewGuid();
    internal ReplicaCrashNode? Source
    {
        get; private set;
    }
    internal ReplicaCrashNode? Target
    {
        get; private set;
    }
    internal ReplicaMaterializer? Materializer
    {
        get; private set;
    }
    internal bool ChildSettled { get; set; } = true;
    private bool uncertain;
    private bool materializerAttempted;
    private bool targetBorrowUncertain;
    private readonly HashSet<ReplicaCrashNode> attempted = [];
    internal bool CanOpen => !uncertain && ChildSettled && Materializer is null && Target is null && Source is null;
    internal void RetainRoot() => uncertain = true;
    internal ReplicaCrashNode Open(bool source)
    {
        if (uncertain || !ChildSettled || (source ? Source : Target) is not null)
        {
            throw new InvalidOperationException("The prior native snapshot owner is not confirmed closed.");
        }
        try
        {
            var node = source ? ReplicaCrashNode.OpenSource(Root, Incarnation) : ReplicaCrashNode.OpenTarget(Root, Incarnation);
            if (source)
            {
                Source = node;
            }
            else
            {
                Target = node;
            }
            return node;
        }
        catch (Exception)
        {
            uncertain = true;
            throw;
        }
    }
    internal ReplicaMaterializer StartMaterializer()
    {
        if (Target is null || Materializer is not null || uncertain)
        {
            throw new InvalidOperationException("The actual target apply owner is unavailable.");
        }
        materializerAttempted = false;
        try
        {
            return Materializer = new(Target.Database, Target.Log, Target.Snapshots, UnitExecutionOptions.ReplicaExecution());
        }
        catch (Exception)
        {
            uncertain = true;
            targetBorrowUncertain = true;
            throw;
        }
    }
    internal async Task CloseAsync(List<Exception> failures)
    {
        if (Materializer is not null && !materializerAttempted)
        {
            materializerAttempted = true;
            var closed = false;
            var original = Materializer;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await original.DisposeAsync();
                closed = true;
            }, failures);
            if (closed)
            {
                Materializer = null;
            }
            uncertain |= !closed;
        }
        CloseNode(Source, failures, source: true);
        if (Materializer is null && !targetBorrowUncertain)
        {
            CloseNode(Target, failures, source: false);
        }
    }
    private void CloseNode(ReplicaCrashNode? node, List<Exception> failures, bool source)
    {
        if (node is null || !attempted.Add(node))
        {
            return;
        }
        var closed = false;
        ServerFailureObserver.Observe(() =>
        {
            node.Dispose();
            closed = true;
        }, failures);
        if (closed && source)
        {
            Source = null;
        }
        if (closed && !source)
        {
            Target = null;
        }
        uncertain |= !closed;
    }
    internal async Task CleanupAsync(List<Exception> failures)
    {
        await CloseAsync(failures);
        if (CanOpen)
        {
            ServerFailureObserver.Observe(() => Directory.Delete(Root, recursive: true), failures);
        }
    }
}
