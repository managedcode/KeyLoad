using KeyLoad.Core.Search;

namespace KeyLoad.Server.Search;

internal static class AnnOriginalOperations
{
    private const long NewRetiredOriginalCount = 1;
    internal static (AnnRootReceipt Receipt, AnnMaintenanceResult? Result) Admit(
        AnnRootReceipt receipt, PrincipalRecord principal, AnnMaintenanceRequest request,
        ProjectionConsumerInfo configured, AnnSeed current, AnnExecutionOptions options)
    {
        var key = new AnnOwnedKey(request.Consumer, request.IndexGeneration);
        AnnOriginalResults.RequireRequest(key, request);
        if (configured.Consumer != request.Consumer || configured.Definition.IndexGeneration != request.IndexGeneration
            || configured.Released || configured.Definition.AnnMaintenance != request.MaintenanceProfile)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProtocol.InvalidSource);
        }
        var entry = receipt.OriginalOperations.SingleOrDefault(value => value.Key == key);
        AnnReservedOperationAdmission.Require(configured, key, entry, request);
        if (entry is null && (receipt.Published.Any(value => value.Key == key)
            || receipt.Pending.Any(value => value.Key == key)))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, AnnProtocol.Interrupted);
        }
        if (entry is not null)
        {
            if (entry.Released)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProtocol.InvalidSource);
            }
            AnnOriginalResults.RequireCurrent(entry, principal, request, current);
            if (entry.LastRequest is { } last && last.CommandId == request.CommandId)
            {
                RequireSame(last, request);
                return (receipt, entry.LastResult ?? throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt));
            }
            RequireNotRetired(entry, request);
            if (entry.PendingRequest is { } pending)
            {
                if (pending.CommandId != request.CommandId)
                {
                    throw Errors.Fail(ErrorCode.RecoveryRequired, AnnProtocol.Interrupted);
                }
                RequireSame(pending, request);
                if (entry.PendingStarted)
                {
                    throw Errors.Fail(ErrorCode.RecoveryRequired, AnnProtocol.Interrupted);
                }
                return (receipt, null);
            }
        }
        if (configured.Checkpoint < current.ProjectionCheckpoint)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
        }
        if (entry is null && receipt.OriginalOperations.Length >= options.MaximumOwnedGenerations)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnProtocol.Bound);
        }
        var next = new AnnOriginalOperation(key, principal.Id, current.Scope.PolicyEpoch,
            request, false, entry?.LastRequest, entry?.LastResult, false,
            entry?.InitialRequest ?? request, entry?.InitialSettled ?? false, entry?.InitialSettlementIntent, entry?.RetiredOriginals ?? []);
        return (Replace(receipt, next), null);
    }

    internal static AnnRootReceipt Start(AnnRootReceipt receipt, AnnMaintenanceRequest request)
    {
        var key = new AnnOwnedKey(request.Consumer, request.IndexGeneration);
        var entry = receipt.OriginalOperations.SingleOrDefault(value => value.Key == key);
        if (entry is null)
        {
            if (request.MaintenanceProfile is not null)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, AnnProtocol.Interrupted);
            }
            return receipt;
        }
        RequireSame(entry.PendingRequest ?? throw Errors.Fail(ErrorCode.HistoryUnavailable,
            AnnProtocol.MissingDependencyHistory), request);
        if (entry.PendingStarted)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, AnnProtocol.Interrupted);
        }
        return Replace(receipt, entry with
        {
            PendingStarted = true
        });
    }

    internal static AnnRootReceipt Settle(AnnRootReceipt receipt, PrincipalRecord principal,
        AnnMaintenanceRequest request, AnnMaintenanceResult result, AnnSeed current,
        CommitProjectionBatchRequest? settlementIntent, AnnExecutionOptions options)
    {
        var key = new AnnOwnedKey(request.Consumer, request.IndexGeneration);
        var entry = receipt.OriginalOperations.SingleOrDefault(value => value.Key == key)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, AnnProtocol.Interrupted);
        AnnOriginalResults.RequireCurrent(entry, principal, request, current);
        AnnOriginalResults.RequireResult(request, result);
        if (entry.LastRequest is { } last && last.CommandId == request.CommandId)
        {
            RequireSame(last, request);
            var settledResult = entry.LastResult ?? throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
            if (!global::KeyLoad.OrleansSerialization.Serialize(settledResult).AsSpan()
                .SequenceEqual(global::KeyLoad.OrleansSerialization.Serialize(result)))
            {
                throw Errors.Fail(ErrorCode.Conflict, AnnProtocol.InvalidSource);
            }
            return receipt;
        }
        RequireSame(entry.PendingRequest ?? throw Errors.Fail(ErrorCode.RecoveryRequired,
            AnnProtocol.Interrupted), request);
        if (!entry.PendingStarted || result.Source is not { } source || current.ProjectionCheckpoint != source.ThroughSequence)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, AnnProtocol.Interrupted);
        }
        return Replace(receipt, entry with
        {
            PendingRequest = null,
            PendingStarted = false,
            LastRequest = request,
            LastResult = result,
            InitialSettled = entry.InitialSettled || entry.InitialRequest.CommandId == request.CommandId,
            InitialSettlementIntent = entry.InitialSettlementIntent ?? settlementIntent,
            RetiredOriginals = RetirePrevious(entry, options)
        });
    }

    private static AnnRetiredOriginalIdentity[] RetirePrevious(AnnOriginalOperation entry,
        AnnExecutionOptions options)
    {
        if (entry.LastRequest is not { } previous)
        {
            return entry.RetiredOriginals;
        }
        if (checked(((long)entry.RetiredOriginals.Length + NewRetiredOriginalCount) * System.Security.Cryptography.SHA256.HashSizeInBytes)
            > options.MaximumManifestBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnProtocol.Bound);
        }
        var retired = new AnnRetiredOriginalIdentity(previous.CommandId,
            System.Security.Cryptography.SHA256.HashData(global::KeyLoad.OrleansSerialization.Serialize(previous)));
        return [.. entry.RetiredOriginals, retired];
    }

    private static void RequireNotRetired(AnnOriginalOperation entry, AnnMaintenanceRequest request)
    {
        var retired = entry.RetiredOriginals.SingleOrDefault(value => value.CommandId == request.CommandId);
        if (retired is null)
        {
            return;
        }
        var digest = System.Security.Cryptography.SHA256.HashData(global::KeyLoad.OrleansSerialization.Serialize(request));
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(retired.RequestSha256, digest))
        {
            throw Errors.Fail(ErrorCode.Conflict, AnnProtocol.InvalidSource);
        }
        throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
    }

    internal static AnnOriginalOperation[] Release(AnnRootReceipt receipt, AnnOwnedKey key)
        => receipt.OriginalOperations.Select(entry => entry.Key == key ? entry with { Released = true } : entry).ToArray();

    internal static AnnRootReceipt CompleteRelease(AnnRootReceipt receipt, AnnOwnedKey key)
    {
        if (receipt.Published.Any(item => item.Key == key) || receipt.Pending.Any(item => item.Key == key)
            || receipt.OriginalOperations.Any(entry => entry.Key == key && !entry.Released))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, AnnProtocol.Interrupted);
        }
        return receipt with
        {
            OriginalOperations = receipt.OriginalOperations.Where(entry => entry.Key != key).ToArray()
        };
    }

    private static AnnRootReceipt Replace(AnnRootReceipt receipt, AnnOriginalOperation next)
        => receipt with
        {
            OriginalOperations = receipt.OriginalOperations.Where(value => value.Key != next.Key).Append(next).ToArray()
        };

    private static void RequireSame(AnnMaintenanceRequest original, AnnMaintenanceRequest request)
    {
        if (!AnnOriginalResults.SameRequest(original, request))
        {
            throw Errors.Fail(ErrorCode.Conflict, AnnProtocol.InvalidSource);
        }
    }
}
