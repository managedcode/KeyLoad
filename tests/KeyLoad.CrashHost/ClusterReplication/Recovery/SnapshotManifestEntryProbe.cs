using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage.IO;

namespace KeyLoad.CrashHost.Features.ClusterReplication;

internal static class SnapshotManifestEntryProbe
{
    internal const string Mode = "snapshot-manifest-entry";
    internal const string Refused = "SNAPSHOT_MANIFEST_NATIVE_REFUSED";
    internal const string Manifest = "manifest";
    internal const string Image = "image";
    private const int Arguments = 4;
    private const int Variant = 3;
    private const int First = 0;
    private const int Root = 1;
    private const int Incarnation = 2;
    private const int OriginalCut = 3;
    private const int TailCut = 5;
    private const int FirstSnapshotOffset = 0;
    private const string RefusalSeparator = ":";
    private const string InvalidRequest = "The snapshot manifest probe requires its original root and incarnation.";
    private const string MissingDescriptor = "The original published source descriptor is absent.";
    private const string InvalidRefusal = "The original snapshot operation did not retain its exact native entry refusal.";

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length == First || args[First] != Mode)
        {
            return false;
        }
        if (args.Length != Arguments || !Path.IsPathFullyQualified(args[Root])
            || args[Variant] is not (Manifest or Image)
            || !Guid.TryParse(args[Incarnation], out var incarnation) || incarnation == Guid.Empty)
        {
            throw new ArgumentException(InvalidRequest, nameof(args));
        }
        var failures = new List<Exception>();
        ReplicaCrashNode? source = null;
        ReplicaCrashNode? target = null;
        await ServerFailureObserver.ObserveAsync(() =>
        {
            source = ReplicaCrashNode.OpenSource(args[Root], incarnation);
            target = ReplicaCrashNode.OpenTarget(args[Root], incarnation);
            if (args[Variant] == Manifest)
            {
                source.Snapshots.Recover();
            }
            var snapshot = source.Snapshots.Current ?? throw new InvalidOperationException(MissingDescriptor);
            KeyLoadException? observed = null;
            try
            {
                if (args[Variant] == Manifest)
                {
                    target.Snapshots.Begin(snapshot);
                }
                else
                {
                    source.Snapshots.ReadChunk(snapshot.TransferId, FirstSnapshotOffset, source.Configuration.SnapshotChunkBytes);
                }
            }
            catch (KeyLoadException failure)
            {
                observed = failure;
            }
            if (observed is not { Code: ErrorCode.Corruption } || observed.Message != ReplicaProtocol.InvalidSnapshot
                || observed.InnerException is not KeyLoadException native || !OfflineNativeErrors.IsInvalidEntry(native)
                || target.Database.LastApplied != OriginalCut || target.Log.State.CommittedIndex != OriginalCut
                || target.Log.State.LastIndex != TailCut || target.Snapshots.Current is not null)
            {
                throw new InvalidOperationException(InvalidRefusal, observed);
            }
            return Task.CompletedTask;
        }, failures);
        if (target is { } originalTarget)
        {
            ServerFailureObserver.Observe(originalTarget.Dispose, failures);
        }
        if (source is { } originalSource)
        {
            ServerFailureObserver.Observe(originalSource.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Console.Out.WriteLineAsync(Refused + RefusalSeparator + args[Variant]);
        await Console.Out.FlushAsync();
        return true;
    }
}
