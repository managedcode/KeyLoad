using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.Search;

internal static class AnnSeedCollector
{
    private const string InvalidAppliedPosition = "The applied storage position is invalid.";
    private const string InvalidAuthority = "Persisted ANN seed authority is inconsistent.";

    internal static AnnSeed Capture(DatabaseEngine database, string principalId,
        PartitionRef partition, string collection, string field, VectorSpace space,
        IOptions<AnnSeedOptions> configuredOptions, ReadExecutionBudget readBudget)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(readBudget);
        ArgumentNullException.ThrowIfNull(configuredOptions);
        var options = configuredOptions.Value;
        options.Validate();
        readBudget.Check();
        var work = new AnnSeedWork(readBudget, options.MaxWorkUnits);
        AnnSeedValidation.ValidateRequest(principalId, partition, collection, field, space, work);
        var startingReadBytes = readBudget.ReadBytes;
        var buffer = database.Store.Read(view => CaptureCut(database, view, principalId,
            partition, collection, field, space, options, readBudget, work));
        readBudget.Check();
        var captured = buffer.Finish();
        var digest = AnnSeedFingerprint.Compute(captured.Scope, captured.Records,
            captured.Work, captured.HashScratch);
        captured.Work.Check();
        return new(captured.Scope, captured.Cut, captured.Records, digest,
            captured.OwnedBytes, captured.PeakBytes,
            checked(readBudget.ReadBytes - startingReadBytes), captured.Work.Units);
    }

    private static AnnSeedBuffer CaptureCut(DatabaseEngine database, IKeyValueView view,
        string principalId, PartitionRef partition, string collection, string field,
        VectorSpace space, AnnSeedOptions options, ReadExecutionBudget budget, AnnSeedWork work)
    {
        const int CaptureCutAbsentCount = 0;
        const int AppliedValidationBoundary = 0;

        var buffer = new AnnSeedBuffer(options.MaxRecords, options.MaxOwnedBytes, options.MaxPeakBytes, options.InitialRecordCapacity,
            options.HashScratchBytes, budget, work);
        var now = database.EvaluationClock.GetUtcNow();
        var metadata = budget.CreateView(view);
        var principal = database.Principal(metadata, principalId, now);
        if (!AnnSeedSourceValidator.IdentityEquals(principal.Id, principalId, work))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidAuthority);
        }
        database.Authorization.Require(principal, partition, collection, Capability.VectorSearch);
        var resource = database.Resource(metadata, partition, collection, ResourceKind.Collection);
        if (!AnnSeedSourceValidator.IdentityEquals(resource.Name, collection, work))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidAuthority);
        }
        database.Authorization.RequireFieldUse(principal, resource, field);
        var head = database.ReadOutboxHead(metadata, partition);
        var appliedBytes = metadata.ReadOwnedValue(KeySpace.AppliedBytes);
        var applied = appliedBytes is null ? CaptureCutAbsentCount : NativeSerialization.Deserialize<long>(appliedBytes);
        if (applied < AppliedValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidAppliedPosition);
        }
        var identity = database.Store.Identity;
        var position = database.Store.Position;
        AnnSeedValidation.ValidateCut(identity, position, applied, head, principal, resource);
        buffer.InitializeScope(principal, partition, collection, field, space, resource.SchemaVersion, now);
        var cut = new AnnSeedCut(identity.NodeId, identity.Incarnation, identity.FormatVersion,
            identity.KeyCodecVersion, identity.ReadGeneration, position, applied, head.Tail, head.FirstAvailable);
        VisibleVectorReads.Visit(database, view, principal, partition, collection, field, budget,
            (document, vector) => buffer.Observe(document, vector, partition, collection, field, space));
        buffer.SetCut(cut);
        budget.Check();
        return buffer;
    }
}
