using KeyLoad.CrashHost.Features.ClusterReplication;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.IO;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeSubmissionJournalProcessFixture(C1OutcomeInspectionFixture canonical) : IDisposable
{
    private ZoneTreeStore? replica;
    private DurableReplicaLog? log;
    private FileStream? retentionLock;
    internal C1OutcomeInspectionFixture Canonical { get; } = canonical;
    internal string ReplicaDirectory => Path.Combine(Canonical.Root, ReplicaProtocol.ReplicaDirectory);
    internal StoreIdentity ReplicaIdentity { get; private set; } = null!;
    internal NativeSubmissionJournalProcessSeed Seed { get; private set; } = null!;
    internal C1SubmissionJournalInspectionRequest Request { get; private set; } = null!;

    internal static async Task RunOwnedAsync(Func<NativeSubmissionJournalProcessFixture, Task> body)
    {
        var failures = new List<Exception>();
        NativeSubmissionJournalProcessFixture? owner = null;
        await ServerFailureObserver.ObserveAsync(() => C1OutcomeInspectionFixture.RunOwnedAsync(async canonical =>
        {
            owner = new(canonical);
            var ownedFailures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(owner.InitializeAsync, ownedFailures).ConfigureAwait(false);
            if (ownedFailures.Count == 0)
            { await ServerFailureObserver.ObserveAsync(() => body(owner), ownedFailures).ConfigureAwait(false); }
            ServerFailureObserver.Observe(owner.CloseAndVerify, ownedFailures);
            ServerFailureObserver.ThrowIfAny(ownedFailures);
        }), failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => owner?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task InitializeAsync()
    {
        var principal = await NativeSubmissionOutcomeProcessSeed.CreateAsync(Canonical).ConfigureAwait(false);
        var configuration = new ReplicaConfiguration("journal-process-a",
            ["journal-process-a", "journal-process-b", "journal-process-c"], ReplicaDirectory, Canonical.Incarnation);
        replica = new(new(ReplicaDirectory)
        { Incarnation = Canonical.Incarnation, SigningKey = Canonical.Identity.SigningKey },
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        ReplicaIdentity = replica.Identity;
        log = new(replica, UnitExecutionOptions.ReplicaConfiguration(configuration), canonicalDatabase: Canonical.Database);
        log.SaveTermAndVote(1, configuration.LocalId);
        Seed = await NativeSubmissionJournalProcessSeed.CreateAsync(Canonical, replica, log, principal.Request.PrincipalId, principal.PolicyEpoch)
            .ConfigureAwait(false);
        Request = new(C1SubmissionJournalInspectionProtocol.Version, Canonical.Root, Canonical.Identity.NodeId,
            Canonical.Incarnation, configuration.LocalId, [.. configuration.VoterIds], Seed.Entry.Index, Seed.Entry.Term);
        CloseAndVerify();
    }

    private void CloseAndVerify()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => { log?.Dispose(); log = null; }, failures);
        ServerFailureObserver.Observe(() => { replica?.Dispose(); replica = null; }, failures);
        ServerFailureObserver.Observe(Canonical.CloseStore, failures);
        if (Directory.Exists(ReplicaDirectory))
        {
            try { RequireLocksReleased(); }
            catch (Exception original) when (global::ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(original) is null)
            {
                failures.Add(original);
                // Keep the original C1 owner from deleting an unjoined sibling tree.
                ServerFailureObserver.Observe(() => retentionLock ??= OfflineRegularFile.Open(
                    Canonical.OuterOwnerLockPath, FileAccess.ReadWrite, FileShare.None, 1024), failures);
            }
            catch (Exception original) when (global::ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(original) is not null)
            {
                failures.Add(original);
                ServerFailureObserver.Observe(() => retentionLock ??= OfflineRegularFile.Open(
                    Canonical.OuterOwnerLockPath, FileAccess.ReadWrite, FileShare.None, 1024), failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        try { log?.Dispose(); }
        catch (Exception error) when (global::ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        { failures.Add(error); }
        catch (Exception error) when (global::ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        { failures.Add(error); }
        try { replica?.Dispose(); }
        catch (Exception error) when (global::ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        { failures.Add(error); }
        catch (Exception error) when (global::ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        { failures.Add(error); }
        try { retentionLock?.Dispose(); }
        catch (Exception error) when (global::ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        { failures.Add(error); }
        catch (Exception error) when (global::ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal void RequireLocksReleased()
    {
        FileStream? outer = null;
        FileStream? database = null;
        FileStream? journal = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            outer = OfflineRegularFile.Open(Canonical.OuterOwnerLockPath, FileAccess.ReadWrite, FileShare.None, 1024);
            database = OfflineRegularFile.Open(Path.Combine(Canonical.DirectoryPath, "owner.lock"),
                FileAccess.ReadWrite, FileShare.None, 1024);
            journal = OfflineRegularFile.Open(Path.Combine(ReplicaDirectory, "owner.lock"),
                FileAccess.ReadWrite, FileShare.None, 1024);
            if (outer.Length != 0 || database.Length != 0 || journal.Length != 0)
            { throw new InvalidDataException("The original journal process owner lock is not empty."); }
        }, failures);
        ServerFailureObserver.Observe(() => journal?.Dispose(), failures);
        ServerFailureObserver.Observe(() => database?.Dispose(), failures);
        ServerFailureObserver.Observe(() => outer?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
