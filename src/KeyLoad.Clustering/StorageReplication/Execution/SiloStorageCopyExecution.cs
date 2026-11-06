using System.Collections.Immutable;
using ManagedCode.Communication.Commands;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Copies prepared commands through execution storage services under the owning grain's ordering.
/// The owner set and its canonical position are cached per membership version; cuts are re-read only after
/// a membership change or a failed copy, not on every write.</summary>
internal sealed class SiloStorageCopyExecution(SiloDatabaseClient storage, IClusterMembershipService membership,
    IOptions<ReplicaCopyOptions> options, Guid incarnation, ILogger diagnostics, PartitionRef? partition = null)
{

    private readonly StorageCopyDiscovery discovery = new(storage, membership, incarnation, diagnostics, partition, options.Value.StorageMode);
    private readonly StorageCopyGapCompletion gaps = new(storage, diagnostics, partition);
    private StorageCopyOwner[]? cachedOwners;
    private MembershipVersion cachedVersion;
    private long ownersEpoch;
    private readonly StorageCopyProgress copyProgress = new();
    internal bool NeedsSynchronization { get; private set; }

    internal async Task WaitForCopiedAsync(long position, CancellationToken token)
    {
        NeedsSynchronization = true;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var changed = copyProgress.Changed;
            var owners = await OwnersAsync(token);
            if (owners.All(owner => owner.Cut.Position >= position))
            {
                return;
            }
            NeedsSynchronization = true;
            await changed.WaitAsync(token);
        }
    }

    internal void StopWaiting() => copyProgress.Stop();

    /// <summary>Prepares and applies consecutive commands as one commit on every owner.</summary>
    /// <returns>Each command's outcome; an undecided command was applied by no copy and belongs in a later batch.</returns>
    internal async Task<StorageCopyOutcome[]> ApplyBatchAsync(ImmutableArray<Command<DatabaseCommand>> operations,
        CancellationToken cancellationToken)
    {
        var outcomes = new StorageCopyOutcome[operations.Length];
        var sent = operations.Length;
        var started = false;
        var applying = false;
        var persisted = false;
        var completed = false;
        try
        {
            var owners = await OwnersAsync(cancellationToken);
            var maximum = await gaps.CompleteAsync(owners, cancellationToken);
            Advance(owners, maximum);
            var authorized = partition is null || options.Value.SiloCount == StorageCopyContract.MinimumCount
                ? owners[StorageCopyContract.FirstOwner].Address : await storage.WriteOwnerAsync(incarnation, cancellationToken);
            owners = StorageCopyPrimarySelection.ForWrite(owners, authorized);
            started = true;
            applying = true;
            var primary = await storage.CommitAsync(owners[StorageCopyContract.FirstOwner].Address, operations, maximum + StorageCopyContract.NextSequence,
                    retainReplication: owners.Length > StorageCopyContract.MinimumCount, cancellationToken);
            persisted = true;
            sent = primary.Applied.Length;
            var applies = new Task<StorageCopyBatchAcknowledgement>[owners.Length];
            applies[StorageCopyContract.FirstOwner] = Task.FromResult(primary);
            for (var index = StorageCopyContract.FirstOwner + 1; index < owners.Length; index++)
            {
                applies[index] = storage.CopyAsync(owners[index].Address, primary, maximum, cancellationToken);
            }
            var results = await Task.WhenAll(applies);
            completed = StorageCopyBatchOutcomes.Accept(results, outcomes);
            if (completed)
            {
                Advance(owners, results[StorageCopyContract.FirstOwner].Position);
            }
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null)
        {
            StorageCopyBatchOutcomes.Fail(outcomes, sent, error, persisted, started, applying);
        }
        finally
        {
            if (!completed)
            {
                NeedsSynchronization = true;
                InvalidateOwners();
            }
        }
        return outcomes;
    }

    internal async Task<SiloStorageCut> ReadCutAsync(CancellationToken cancellationToken)
        => (await ReadAvailableAsync(cancellationToken)).Cut;

    internal async Task<SiloAddress> GetPrimaryOwnerAsync(CancellationToken token)
        => (await ReadAvailableAsync(token)).Address;

    internal async Task<SiloAddress> GetReadOwnerAsync(SiloAddress preferredSilo, CancellationToken cancellationToken)
    {
        if (options.Value.StorageMode == StorageAccessMode.SharedVolume)
        {
            return await storage.MetadataOwnerAsync(incarnation, preferredSilo, cancellationToken);
        }
        var selected = await ReadAvailableAsync(cancellationToken, preferredSilo);
        var owners = await OwnersAsync(cancellationToken, requireComplete: false);
        return await SelectAuthorizedOwnerAsync(owners, selected.Address, cancellationToken);
    }

    private async Task<SiloAddress> SelectAuthorizedOwnerAsync(StorageCopyOwner[] owners, SiloAddress preferred,
        CancellationToken token)
    {
        var selected = partition is null || options.Value.StorageMode == StorageAccessMode.SharedVolume
            || options.Value.SiloCount == StorageCopyContract.MinimumCount
            ? preferred : await storage.MetadataOwnerAsync(incarnation, preferred, token);
        StorageCopyPrimarySelection.Require(owners, selected);
        return selected;
    }

    internal async Task<SiloStorageCut> ReadDatabaseCutAsync(CancellationToken token)
        => StorageCopyDatabaseReadSelection.Select(await discovery.ReadAsync(allScopes: true, token), null).Cut;

    internal async Task<SiloAddress> GetDatabaseReadOwnerAsync(SiloAddress preferred, CancellationToken token)
        => StorageCopyDatabaseReadSelection.Select(await discovery.ReadAsync(allScopes: true, token), preferred).Address;

    /// <summary>Selects up to ReplicaCount of the most advanced reachable copies. A batch requires the complete owner
    /// set; a read takes the most advanced reachable copy, so surviving copies keep serving every acknowledged write
    /// after a silo loss. Only a complete owner set is cached.</summary>
    private async Task<StorageCopyOwner[]> OwnersAsync(CancellationToken cancellationToken, bool requireComplete = true)
    {
        var settings = options.Value;
        settings.Validate();
        if (settings.StorageMode == StorageAccessMode.SharedVolume)
        {
            var sharedOwners = await discovery.ReadAsync(allScopes: false, cancellationToken);
            return sharedOwners.Length == StorageCopyContract.MinimumCount ? sharedOwners
                : throw Errors.Fail(ErrorCode.OwnershipLost, StorageCopyContract.OwnersUnavailable);
        }
        var version = membership.CurrentSnapshot.Version;
        if (cachedOwners is { } cached && cachedVersion.Equals(version))
        {
            return cached;
        }
        // Reads interleave with batches; a cut read that a batch overtook must not replace the batch's owners.
        var epoch = ownersEpoch;
        var available = await discovery.ReadAsync(allScopes: false, cancellationToken);
        // A concurrent read can publish the owner set while this discovery awaits its RPCs. The writer must
        // advance that same set, otherwise its committed cut is lost from the cache.
        if (cachedOwners is { } published && cachedVersion.Equals(version))
        {
            return published;
        }
        var owners = available.OrderByDescending(copy => copy.Cut.Position)
            .ThenBy(copy => copy.Name, StringComparer.Ordinal).Take(settings.ReplicaCount).ToArray();
        if (requireComplete && owners.Length != settings.ReplicaCount)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, StorageCopyContract.OwnersUnavailable);
        }
        NeedsSynchronization = owners.Length > 1 && owners.Any(owner => owner.Cut.Position != owners[0].Cut.Position);
        if (owners.Length == settings.ReplicaCount && epoch == ownersEpoch && (cachedOwners is null || !cachedVersion.Equals(version)))
        {
            cachedOwners = owners;
            cachedVersion = version;
        }
        return owners;
    }

    /// <summary>Completes retained canonical work without requiring another foreground write.</summary>
    internal async Task SynchronizeAsync(CancellationToken token)
    {
        try
        {
            var owners = await OwnersAsync(token);
            var cut = await gaps.CompleteAsync(owners, token);
            Advance(owners, cut);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            InvalidateOwners();
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            InvalidateOwners();
            StorageCopyDiagnostics.SynchronizationDeferred(diagnostics,
                failure is KeyLoadException error ? error.Code : ErrorCode.OwnershipLost);
        }
    }

    private void InvalidateOwners()
    {
        cachedOwners = null;
        ownersEpoch++;
    }

    /// <summary>Records that every owner persisted <paramref name="sequence"/>, so the next write needs no cut reads.</summary>
    private void Advance(StorageCopyOwner[] owners, long sequence)
    {
        NeedsSynchronization = false;
        for (var index = StorageCopyContract.FirstOwner; index < owners.Length; index++)
        {
            if (owners[index].Cut.Position < sequence)
            {
                owners[index] = owners[index] with { Cut = owners[index].Cut with { Position = sequence } };
            }
        }
        ownersEpoch++;
        copyProgress.Advance();
        if (cachedOwners is { } published && !(ReferenceEquals(published, owners) /* KLD0041: Invalidate only a different published storage owner roster instance through CLR reference identity. */))
        {
            cachedOwners = null;
        }
    }

    private async Task<StorageCopyOwner> ReadAvailableAsync(CancellationToken cancellationToken, SiloAddress? preferredSilo = null)
    {
        StorageCopyOwner? selected = null;
        foreach (var copy in await OwnersAsync(cancellationToken, requireComplete: false))
        {
            if (selected is not { } current || copy.Cut.Position > current.Cut.Position
                || copy.Cut.Position == current.Cut.Position && copy.Address.Equals(preferredSilo))
            {
                selected = copy;
            }
        }
        return selected ?? throw Errors.Fail(ErrorCode.OwnershipLost, StorageCopyContract.OwnersUnavailable);
    }

}
