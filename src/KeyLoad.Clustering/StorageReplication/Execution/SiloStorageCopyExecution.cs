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
    IOptions<ReplicaCopyOptions> options, Guid incarnation, ILogger diagnostics)
{

    private readonly StorageCopyGapCompletion gaps = new(storage, diagnostics);
    private StorageCopyOwner[]? cachedOwners;
    private MembershipVersion cachedVersion;
    private long ownersEpoch;

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
            var owners = await OwnersAsync(cancellationToken).ConfigureAwait(true);
            var maximum = await gaps.CompleteAsync(owners, cancellationToken).ConfigureAwait(true);
            Advance(owners, maximum);
            started = true;
            var prepared = await storage.On(owners[StorageCopyContract.FirstOwner].Address)
                .PrepareBatchAsync(operations, cancellationToken).ConfigureAwait(true);
            sent = prepared.Length;
            cancellationToken.ThrowIfCancellationRequested();
            // Every owner applies the same prepared commands from the same cut in one commit, so the copies persist
            // concurrently and the next batch starts only after all of them.
            var applies = new Task<StorageBatchResult>[owners.Length];
            applying = true;
            for (var index = StorageCopyContract.FirstOwner; index < owners.Length; index++)
            {
                applies[index] = storage.On(owners[index].Address)
                    .ApplyBatchAsync(prepared, maximum + StorageCopyContract.NextSequence, cancellationToken);
            }
            StorageBatchResult[] results;
            try
            {
                results = await Task.WhenAll(applies).ConfigureAwait(true);
            }
            finally
            {
                persisted = applies.Any(apply => apply.IsCompletedSuccessfully);
            }
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
                InvalidateOwners();
            }
        }
        return outcomes;
    }

    internal async Task<SiloStorageCut> ReadCutAsync(CancellationToken cancellationToken)
        => (await ReadAvailableAsync(cancellationToken).ConfigureAwait(true)).Cut;

    internal async Task<SiloAddress> GetReadOwnerAsync(SiloAddress preferredSilo, CancellationToken cancellationToken)
        => (await ReadAvailableAsync(cancellationToken, preferredSilo).ConfigureAwait(true)).Address;

    /// <summary>Selects up to ReplicaCount of the most advanced reachable copies. A batch requires the complete owner
    /// set; a read takes the most advanced reachable copy, so surviving copies keep serving every acknowledged write
    /// after a silo loss. Only a complete owner set is cached.</summary>
    private async Task<StorageCopyOwner[]> OwnersAsync(CancellationToken cancellationToken, bool requireComplete = true)
    {
        var settings = options.Value;
        settings.Validate();
        var version = membership.CurrentSnapshot.Version;
        if (cachedOwners is { } cached && cachedVersion.Equals(version))
        {
            return cached;
        }
        // Reads interleave with batches; a cut read that a batch overtook must not replace the batch's owners.
        var epoch = ownersEpoch;
        var available = await ReadAvailableCopiesAsync(cancellationToken).ConfigureAwait(true);
        var owners = available.OrderByDescending(copy => copy.Cut.Position)
            .ThenBy(copy => copy.Name, StringComparer.Ordinal).Take(settings.ReplicaCount).ToArray();
        if (requireComplete && owners.Length != settings.ReplicaCount)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, StorageCopyContract.OwnersUnavailable);
        }
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
            var owners = await OwnersAsync(token).ConfigureAwait(true);
            var cut = await gaps.CompleteAsync(owners, token).ConfigureAwait(true);
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
    private static void Advance(StorageCopyOwner[] owners, long sequence)
    {
        for (var index = StorageCopyContract.FirstOwner; index < owners.Length; index++)
        {
            if (owners[index].Cut.Position < sequence)
            {
                owners[index] = owners[index] with { Cut = owners[index].Cut with { Position = sequence } };
            }
        }
    }

    private async Task<StorageCopyOwner[]> ReadAvailableCopiesAsync(CancellationToken cancellationToken)
    {
        var members = membership.CurrentSnapshot.Members.Values.Where(member => member.Status == SiloStatus.Active)
            .OrderBy(member => member.Name, StringComparer.Ordinal).ToArray();
        var reads = new Task<(bool HasValue, SiloStorageCut? Value)>[members.Length];
        for (var index = StorageCopyContract.FirstOwner; index < members.Length; index++)
        {
            reads[index] = TryReadCutAsync(members[index].SiloAddress, cancellationToken);
        }
        var cuts = await Task.WhenAll(reads).ConfigureAwait(true);
        var available = new List<StorageCopyOwner>(members.Length);
        for (var index = StorageCopyContract.FirstOwner; index < members.Length; index++)
        {
            if (cuts[index].Value is { } cut)
            {
                available.Add(new(members[index].SiloAddress, members[index].Name, cut));
            }
        }
        return [.. available];
    }

    private async Task<(bool HasValue, SiloStorageCut? Value)> TryReadCutAsync(SiloAddress silo, CancellationToken cancellationToken)
    {
        try
        {
            var cut = await storage.On(silo).ReadCutAsync(cancellationToken).ConfigureAwait(true);
            if (cut.Incarnation == incarnation)
            {
                return (true, ((SiloStorageCut?)(cut)));
            }
            StorageCopyDiagnostics.CopyExcluded(diagnostics, silo, ErrorCode.RecoveryRequired);
            return (false, default(SiloStorageCut?));
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && CqrsBoundaryErrors.IsNonFatal(error))
        {
            StorageCopyDiagnostics.CopyExcluded(diagnostics, silo,
                error is KeyLoadException failure ? failure.Code : ErrorCode.OwnershipLost);
            return (false, default(SiloStorageCut?));
        }
    }

    private async Task<StorageCopyOwner> ReadAvailableAsync(CancellationToken cancellationToken, SiloAddress? preferredSilo = null)
    {
        StorageCopyOwner? selected = null;
        foreach (var copy in await OwnersAsync(cancellationToken, requireComplete: false).ConfigureAwait(true))
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
