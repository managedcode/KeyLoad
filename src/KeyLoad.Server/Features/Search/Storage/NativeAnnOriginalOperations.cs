using KeyLoad.Core.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnOriginalOperations
{
    private const long NewRetiredOriginalCount = 1;
    internal static (NativeAnnRootReceipt Receipt, AnnMaintenanceResult? Result) Admit(
        NativeAnnRootReceipt receipt, PrincipalRecord principal, AnnMaintenanceRequest request,
        ProjectionConsumerInfo configured, AnnSeed current, NativeAnnExecutionOptions options)
    {
        var key = new NativeAnnOwnedKey(request.Consumer, request.IndexGeneration);
        NativeAnnOriginalResults.RequireRequest(key, request);
        if (configured.Consumer != request.Consumer || configured.Definition.IndexGeneration != request.IndexGeneration
            || configured.Released || configured.Definition.AnnMaintenance != request.MaintenanceProfile)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, NativeAnnProtocol.InvalidSource); }
        var entry = receipt.OriginalOperations.SingleOrDefault(value => value.Key == key);
        if (configured.InitialAnnMaintenance is { } reserved)
        {
            NativeAnnOriginalResults.RequireRequest(key, reserved);
            if (request.CommandId == reserved.CommandId)
            {
                RequireSame(reserved, request);
                if (entry is null && configured.InitialAnnMaintenanceSettled)
                { throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory); }
            }
            if (!configured.InitialAnnMaintenanceSettled && !NativeAnnOriginalResults.SameRequest(reserved, request))
            { throw Errors.Fail(ErrorCode.RecoveryRequired, NativeAnnProtocol.Interrupted); }
            if (entry is not null && !configured.InitialAnnMaintenanceSettled
                && !NativeAnnOriginalResults.SameRequest(reserved, entry.InitialRequest))
            { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
        }
        if (entry is null && (receipt.Published.Any(value => value.Key == key)
            || receipt.Pending.Any(value => value.Key == key)))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, NativeAnnProtocol.Interrupted); }
        if (entry is not null)
        {
            if (entry.Released) { throw Errors.Fail(ErrorCode.TokenInvalidated, NativeAnnProtocol.InvalidSource); }
            NativeAnnOriginalResults.RequireCurrent(entry, principal, request, current);
            if (entry.LastRequest is { } last && last.CommandId == request.CommandId)
            {
                RequireSame(last, request);
                return (receipt, entry.LastResult ?? throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt));
            }
            RequireNotRetired(entry, request);
            if (entry.PendingRequest is { } pending)
            {
                if (pending.CommandId != request.CommandId)
                { throw Errors.Fail(ErrorCode.RecoveryRequired, NativeAnnProtocol.Interrupted); }
                RequireSame(pending, request);
                if (entry.PendingStarted)
                { throw Errors.Fail(ErrorCode.RecoveryRequired, NativeAnnProtocol.Interrupted); }
                return (receipt, null);
            }
        }
        if (configured.Checkpoint < current.ProjectionCheckpoint)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory); }
        if (entry is null && receipt.OriginalOperations.Length >= options.MaximumOwnedGenerations)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound); }
        var next = new NativeAnnOriginalOperation(key, principal.Id, current.Scope.PolicyEpoch,
            request, false, entry?.LastRequest, entry?.LastResult, false,
            entry?.InitialRequest ?? request, entry?.InitialSettled ?? false, entry?.InitialSettlementIntent, entry?.RetiredOriginals ?? []);
        return (Replace(receipt, next), null);
    }

    internal static NativeAnnRootReceipt Start(NativeAnnRootReceipt receipt, AnnMaintenanceRequest request)
    {
        var key = new NativeAnnOwnedKey(request.Consumer, request.IndexGeneration);
        var entry = receipt.OriginalOperations.SingleOrDefault(value => value.Key == key);
        if (entry is null)
        {
            if (request.MaintenanceProfile is not null)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, NativeAnnProtocol.Interrupted); }
            return receipt;
        }
        RequireSame(entry.PendingRequest ?? throw Errors.Fail(ErrorCode.HistoryUnavailable,
            NativeAnnProtocol.MissingDependencyHistory), request);
        if (entry.PendingStarted)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, NativeAnnProtocol.Interrupted); }
        return Replace(receipt, entry with { PendingStarted = true });
    }

    internal static NativeAnnRootReceipt Settle(NativeAnnRootReceipt receipt, PrincipalRecord principal,
        AnnMaintenanceRequest request, AnnMaintenanceResult result, AnnSeed current,
        CommitProjectionBatchRequest? settlementIntent, NativeAnnExecutionOptions options)
    {
        var key = new NativeAnnOwnedKey(request.Consumer, request.IndexGeneration);
        var entry = receipt.OriginalOperations.SingleOrDefault(value => value.Key == key)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, NativeAnnProtocol.Interrupted);
        NativeAnnOriginalResults.RequireCurrent(entry, principal, request, current);
        NativeAnnOriginalResults.RequireResult(request, result);
        if (entry.LastRequest is { } last && last.CommandId == request.CommandId)
        {
            RequireSame(last, request);
            var settledResult = entry.LastResult ?? throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt);
            if (!global::KeyLoad.NativeSerialization.Serialize(settledResult).AsSpan()
                .SequenceEqual(global::KeyLoad.NativeSerialization.Serialize(result)))
            { throw Errors.Fail(ErrorCode.Conflict, NativeAnnProtocol.InvalidSource); }
            return receipt;
        }
        RequireSame(entry.PendingRequest ?? throw Errors.Fail(ErrorCode.RecoveryRequired,
            NativeAnnProtocol.Interrupted), request);
        if (!entry.PendingStarted || result.Source is not { } source || current.ProjectionCheckpoint != source.ThroughSequence)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, NativeAnnProtocol.Interrupted); }
        return Replace(receipt, entry with { PendingRequest = null, PendingStarted = false,
            LastRequest = request, LastResult = result,
            InitialSettled = entry.InitialSettled || entry.InitialRequest.CommandId == request.CommandId,
            InitialSettlementIntent = entry.InitialSettlementIntent ?? settlementIntent,
            RetiredOriginals = RetirePrevious(entry, options) });
    }

    private static NativeAnnRetiredOriginalIdentity[] RetirePrevious(NativeAnnOriginalOperation entry,
        NativeAnnExecutionOptions options)
    {
        if (entry.LastRequest is not { } previous) { return entry.RetiredOriginals; }
        if (checked(((long)entry.RetiredOriginals.Length + NewRetiredOriginalCount) * System.Security.Cryptography.SHA256.HashSizeInBytes)
            > options.MaximumManifestBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound); }
        var retired = new NativeAnnRetiredOriginalIdentity(previous.CommandId,
            System.Security.Cryptography.SHA256.HashData(global::KeyLoad.NativeSerialization.Serialize(previous)));
        return [.. entry.RetiredOriginals, retired];
    }

    private static void RequireNotRetired(NativeAnnOriginalOperation entry, AnnMaintenanceRequest request)
    {
        var retired = entry.RetiredOriginals.SingleOrDefault(value => value.CommandId == request.CommandId);
        if (retired is null) { return; }
        var digest = System.Security.Cryptography.SHA256.HashData(global::KeyLoad.NativeSerialization.Serialize(request));
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(retired.RequestSha256, digest))
        { throw Errors.Fail(ErrorCode.Conflict, NativeAnnProtocol.InvalidSource); }
        throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory);
    }

    internal static NativeAnnOriginalOperation[] Release(NativeAnnRootReceipt receipt, NativeAnnOwnedKey key)
        => receipt.OriginalOperations.Select(entry => entry.Key == key ? entry with { Released = true } : entry).ToArray();

    internal static NativeAnnRootReceipt CompleteRelease(NativeAnnRootReceipt receipt, NativeAnnOwnedKey key)
    {
        if (receipt.Published.Any(item => item.Key == key) || receipt.Pending.Any(item => item.Key == key)
            || receipt.OriginalOperations.Any(entry => entry.Key == key && !entry.Released))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, NativeAnnProtocol.Interrupted); }
        return receipt with { OriginalOperations = receipt.OriginalOperations.Where(entry => entry.Key != key).ToArray() };
    }

    private static NativeAnnRootReceipt Replace(NativeAnnRootReceipt receipt, NativeAnnOriginalOperation next)
        => receipt with { OriginalOperations = receipt.OriginalOperations.Where(value => value.Key != next.Key).Append(next).ToArray() };

    private static void RequireSame(AnnMaintenanceRequest original, AnnMaintenanceRequest request)
    {
        if (!NativeAnnOriginalResults.SameRequest(original, request))
        { throw Errors.Fail(ErrorCode.Conflict, NativeAnnProtocol.InvalidSource); }
    }
}
