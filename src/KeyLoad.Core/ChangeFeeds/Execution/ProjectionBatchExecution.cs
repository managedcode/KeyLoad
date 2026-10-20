using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Owns the ProjectionBatchExecution operation contract.</summary>
public static class ProjectionBatchExecution
{
    internal static void RequireProjectionAdministrator(PrincipalRecord principal)
    {
        const string RequireProjectionAdministratorDetailText = "System projection administration is required.";
        if (!principal.ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, RequireProjectionAdministratorDetailText);
        }
    }

    /// <summary>Reads a bounded authorized projection batch.</summary>
    /// <param name = "principalId">Persisted principal identifier.</param>
    /// <param name = "request">Consumer and byte/count budget.</param>
    /// <returns>The ordered batch and signed continuation token.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static ProjectionBatch ReadProjectionBatch(this DatabaseEngine engine, string principalId, ReadProjectionBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.Store.Read(view => engine.ReadProjectionBatchRead(view, principalId, request));
    }

    internal static long ProjectionUpperSequence(ReadProjectionBatchRequest request, ProjectionConsumerInfo state, OutboxHead head)
    {
        var upper = request.ThroughSequence ?? head.Tail;
        if (upper < state.Checkpoint || upper > head.Tail)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ChangeFeedsExecutionProjectionOutbox.InvalidProjectionUpperSequence);
        }

        return upper;
    }

    internal static byte[] ProjectionReceiptKey(ProjectionBatchClaims claims) => KeySpace.Partition(ChangeFeedsExecutionProjectionOutbox.ProjectionReceiptSpace, claims.Consumer.Partition, claims.Consumer.Name, claims.IndexGeneration, claims.After, claims.Through);
    internal static ProjectionBatchClaims ProjectionClaims(this DatabaseEngine engine, IKeyValueView view, CommitProjectionBatchRequest request, DateTimeOffset now, bool allowExpiredReceipt = false)
    {
        const string ProjectionBatchScopeMismatchDetail = "The projection batch belongs to a different generation or scope.";
        const string ExpiredProjectionBatchTokenDetail = "The projection batch token expired.";
        var claims = engine.Verify<ProjectionBatchClaims>(request.Token);
        var state = ChangeFeedsValidationProjectionConsumerIdentity.ProjectionConsumer(view, request.Consumer);
        if (claims.Purpose != ChangeFeedsExecutionProjectionOutbox.ProjectionBatchTokenPurpose || claims.Incarnation != engine.Store.Identity.Incarnation || claims.Consumer != request.Consumer || claims.IndexGeneration != state.Definition.IndexGeneration || state.Released || claims.Through < claims.After)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ProjectionBatchScopeMismatchDetail);
        }

        if (claims.ExpiresAt <= now && !(allowExpiredReceipt && view.ReadOwnedValue(ChangeFeedsExecution.ProjectionReceiptKey(claims, request)) is not null))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ExpiredProjectionBatchTokenDetail);
        }

        return claims;
    }

    internal static ProjectionBatchResult CommitProjectionBatch(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, CommitProjectionBatchRequest request, DateTimeOffset now, long position)
    {
        const string ConflictingProjectionEffectsDetail = "The projection batch was completed with different effects.";
        const string ChangedProjectionCheckpointDetail = "The projection checkpoint changed; reread the next batch.";
        const int EmptyEffectsLength = 0;
        const int EffectsLengthValidationBoundary = 0;
        var claims = engine.ProjectionClaims(tx, request, now, allowExpiredReceipt: true);
        engine.AuthorizeBatch(tx, principal, new(request.CommandId, request.Consumer.Partition, request.Effects), allowEmpty: true);
        engine.ReauthorizeEffects(tx, principal, request.Consumer.Partition, request.Effects);
        var key = ChangeFeedsExecution.ProjectionReceiptKey(claims, request);
        var fingerprint = JsonData.Fingerprint(request.Effects);
        var completed = tx.GetRecord<ProjectionReceipt>(key);
        var enrollment = engine.RequireAnnInitialSettlement(tx, principal, request, claims, now, completed is not null);
        if (completed is not null)
        {
            if (fingerprint != completed.Fingerprint)
            {
                throw Errors.Fail(ErrorCode.Conflict, ConflictingProjectionEffectsDetail);
            }

            return new(completed.Receipt, true, completed.Checkpoint);
        }

        var state = ChangeFeedsValidationProjectionConsumerIdentity.ProjectionConsumer(tx, request.Consumer);
        var head = engine.ReadOutboxHead(tx, request.Consumer.Partition);
        ChangeFeedsExecutionProjectionOutbox.CheckOutboxPosition(head, claims.After);
        if (state.Checkpoint != claims.After || claims.Through > head.Tail)
        {
            throw Errors.Fail(ErrorCode.Conflict, ChangedProjectionCheckpointDetail);
        }

        if (claims.Through == claims.After)
        {
            if (request.Effects.Length != EmptyEffectsLength)
            {
                throw Errors.Fail(ErrorCode.Validation, ChangeFeedsExecutionProjectionOutbox.EmptyProjectionBatchEffectsDetail);
            }

            var emptyReceipt = new CommitReceipt(request.CommandId, engine.Token(tx, request.Consumer.Partition, position), [], engine.Durability, engine.AcknowledgedPersistedCopies);
            if (enrollment is not null)
            {
                var settled = enrollment with
                {
                    InitialAnnMaintenanceSettled = true
                };
                if (OrleansSerialization.Measure(settled) > engine.Limits.MaxBatchBytes)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, ChangeFeedsExecution.InvalidAnnSettlement);
                }

                tx.PutRecord(ChangeFeedsExecutionProjectionOutbox.ConsumerKey(request.Consumer), settled);
                tx.PutRecord(key, new ProjectionReceipt(fingerprint, emptyReceipt, state.Checkpoint));
            }

            return new(emptyReceipt, false, state.Checkpoint);
        }

        var effects = engine.ApplyMutations(tx, principal, request.Consumer.Partition, request.Effects, now, position, allowOutboxProgressReserve: state.LastProgressReservationCut != head.FirstAvailable);
        var receipt = new CommitReceipt(request.CommandId, engine.Token(tx, request.Consumer.Partition, position), effects, engine.Durability, engine.AcknowledgedPersistedCopies);
        var after = engine.ReadOutboxHead(tx, request.Consumer.Partition);
        var usedReserve = effects.Length > EffectsLengthValidationBoundary && (after.StoredRecords > engine.Limits.MaxOutboxRecords || after.StoredBytes > engine.Limits.MaxOutboxBytes);
        tx.PutRecord(ChangeFeedsExecutionProjectionOutbox.ConsumerKey(request.Consumer), state with { Checkpoint = claims.Through, LastProgressReservationCut = usedReserve ? head.FirstAvailable : state.LastProgressReservationCut });
        tx.PutRecord(key, new ProjectionReceipt(fingerprint, receipt, claims.Through));
        return new(receipt, false, claims.Through);
    }
}
