using System.Numerics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using KeyLoad.Core;
using KeyLoad.CrashHost.Features.DocumentStorage;
using KeyLoad.Security;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class NativeChecksumProfileScenario
{
    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length == NativeChecksumProfileProtocol.Success || args[NativeChecksumProfileProtocol.ModeIndex] != NativeChecksumProfileProtocol.Mode)
        { return false; }
        if (args.Length != NativeChecksumProfileProtocol.ArgumentCount)
        { throw new InvalidOperationException(NativeChecksumProfileProtocol.Invalid); }
        var phase = args[NativeChecksumProfileProtocol.PhaseIndex];
        var profile = args[NativeChecksumProfileProtocol.ProfileIndex];
        RequireProfile(profile);
        var root = args[NativeChecksumProfileProtocol.RootIndex];
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RunOwnedPhaseAsync(root, phase, failures), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        Console.WriteLine(NativeChecksumProfileProtocol.CompletionSignalPrefix + phase + NativeChecksumProfileProtocol.SignalSeparator + profile);
        Console.WriteLine($"{NativeChecksumProfileProtocol.CapabilitySignalPrefix}{phase}{NativeChecksumProfileProtocol.SignalSeparator}{profile}"
            + $"{NativeChecksumProfileProtocol.SignalSeparator}{NativeChecksumProfileProtocol.VectorCapability}{Vector.IsHardwareAccelerated}"
            + $"{NativeChecksumProfileProtocol.SignalSeparator}{NativeChecksumProfileProtocol.Sse42Capability}{Sse42.IsSupported}"
            + $"{NativeChecksumProfileProtocol.SignalSeparator}{NativeChecksumProfileProtocol.AdvSimdCapability}{AdvSimd.IsSupported}");
        return true;
    }

    private static async Task RunOwnedPhaseAsync(string root, string phase, List<Exception> failures)
    {
        using var store = new ZoneTreeStore(new(root), CrashExecutionOptions.StorageExecution(),
            CrashExecutionOptions.PointCacheExecution());
        await ServerFailureObserver.ObserveAsync(() => RunPhaseAsync(root, store, phase), failures);
    }

    private static Task RunPhaseAsync(string root, ZoneTreeStore store, string phase) => phase switch
    {
        NativeChecksumProfileProtocol.Seed => SeedAsync(root, store),
        NativeChecksumProfileProtocol.Extend => ContinueAsync(root, store),
        NativeChecksumProfileProtocol.Cold => ColdAsync(root, store),
        _ => throw new InvalidOperationException(NativeChecksumProfileProtocol.Invalid)
    };

    private static void RequireProfile(string profile)
    {
        var settings = NativeChecksumProfileOptionsBinding.Child().Value;
        if (profile is not (NativeChecksumProfileProtocol.Enabled or NativeChecksumProfileProtocol.Disabled)
            || settings.DotnetIntrinsics != profile
            || settings.ComPlusIntrinsics != profile
            || profile == NativeChecksumProfileProtocol.Disabled && (Vector.IsHardwareAccelerated || Sse42.IsSupported || AdvSimd.IsSupported))
        { throw new InvalidOperationException(NativeChecksumProfileProtocol.Invalid); }
    }

    private static async Task SeedAsync(string root, ZoneTreeStore store)
    {
        var database = CrashDatabase.Create(store);
        CommandIdempotencyCrashData.ConfigureResources(database);
        var tail = database.GetOutboxStatus(CrashFixtureValues.Principal, CommandIdempotencyCrashContract.Partition).Head.Tail;
        var operation = CommandIdempotencyCrashData.CreateOperation();
        var receipt = database.Apply(operation).Get<CommitReceipt>();
        CommandIdempotencyCrashAssertions.RequireInitialReceipt(receipt);
        CommandIdempotencyCrashAssertions.AssertCanonicalEffects(database, store, receipt, tail + NativeChecksumProfileProtocol.OriginalEffects, tail);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(root, CommandIdempotencyCrashContract.CommandEvidenceFile, operation);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(root, CommandIdempotencyCrashContract.ReceiptEvidenceFile, receipt);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(root, CommandIdempotencyCrashContract.SeedTailEvidenceFile, tail);
        await NativeChecksumProfileEvidence.SaveAsync(root, store);
    }

    private static async Task ContinueAsync(string root, ZoneTreeStore store)
    {
        await NativeChecksumProfileEvidence.RequireAsync(root, store);
        var database = Open(store);
        var operation = await CommandIdempotencyCrashData.ReadEvidenceAsync<ReplicatedOperation>(root, CommandIdempotencyCrashContract.CommandEvidenceFile);
        var receipt = await CommandIdempotencyCrashData.ReadEvidenceAsync<CommitReceipt>(root, CommandIdempotencyCrashContract.ReceiptEvidenceFile);
        var tail = await CommandIdempotencyCrashData.ReadEvidenceAsync<long>(root, CommandIdempotencyCrashContract.SeedTailEvidenceFile);
        CommandIdempotencyCrashAssertions.RequireSameReceipt(database.ResolveOutcome(operation).Get<CommitReceipt>(), receipt);
        CommandIdempotencyCrashAssertions.AssertCanonicalEffects(database, store, receipt, tail + NativeChecksumProfileProtocol.OriginalEffects, tail);
        CommandIdempotencyCrashAssertions.AssertRetriesAndConflict(database, store, operation, receipt, tail + NativeChecksumProfileProtocol.OriginalEffects, tail);
        var followUpOperation = CreateFollowUp();
        var followUp = database.Apply(followUpOperation).Get<CommitReceipt>();
        CommandIdempotencyCrashAssertions.RequireSameReceipt(database.Apply(followUpOperation).Get<CommitReceipt>(), followUp);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(root, NativeChecksumProfileProtocol.FollowUpCommandFile, followUpOperation);
        CommandIdempotencyCrashAssertions.RequireFollowUp(followUp);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(root, NativeChecksumProfileProtocol.FollowUpReceiptFile, followUp);
        await ColdModelsAsync(root, database, store);
        await NativeChecksumProfileEvidence.SaveAsync(root, store);
    }

    private static async Task ColdAsync(string root, ZoneTreeStore store)
    {
        await NativeChecksumProfileEvidence.RequireAsync(root, store);
        await ColdModelsAsync(root, Open(store), store);
        await NativeChecksumProfileEvidence.RequireAsync(root, store);
    }

    private static async Task ColdModelsAsync(string root, DatabaseEngine database, ZoneTreeStore store)
    {
        var original = await CommandIdempotencyCrashData.ReadEvidenceAsync<ReplicatedOperation>(root, CommandIdempotencyCrashContract.CommandEvidenceFile);
        var receipt = await CommandIdempotencyCrashData.ReadEvidenceAsync<CommitReceipt>(root, CommandIdempotencyCrashContract.ReceiptEvidenceFile);
        var followUp = await CommandIdempotencyCrashData.ReadEvidenceAsync<CommitReceipt>(root, NativeChecksumProfileProtocol.FollowUpReceiptFile);
        var followUpOperation = await CommandIdempotencyCrashData.ReadEvidenceAsync<ReplicatedOperation>(root, NativeChecksumProfileProtocol.FollowUpCommandFile);
        CommandIdempotencyCrashAssertions.RequireSameReceipt(database.ResolveOutcome(followUpOperation).Get<CommitReceipt>(), followUp);
        var tail = await CommandIdempotencyCrashData.ReadEvidenceAsync<long>(root, CommandIdempotencyCrashContract.SeedTailEvidenceFile);
        CommandIdempotencyCrashAssertions.RequireSameReceipt(database.ResolveOutcome(original).Get<CommitReceipt>(), receipt);
        var retained = store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(CommandIdempotencyCrashContract.Partition,
            CrashFixtureValues.Principal, CommandIdempotencyCrashContract.FollowUpCommandId)))?.Result.Get<CommitReceipt>()
            ?? throw new InvalidOperationException(NativeChecksumProfileProtocol.Invalid);
        CommandIdempotencyCrashAssertions.RequireSameReceipt(retained, followUp);
        CommandIdempotencyCrashAssertions.AssertCanonicalEffects(database, store, receipt, tail + NativeChecksumProfileProtocol.HealthyEffects, tail);
        CommandIdempotencyCrashAssertions.RequireDocument(database.GetDocument(CrashFixtureValues.Principal,
            new(CommandIdempotencyCrashContract.Partition, CommandIdempotencyCrashContract.Collection, CommandIdempotencyCrashContract.FollowUpDocumentId)),
            new(CommandIdempotencyCrashContract.Partition, CommandIdempotencyCrashContract.Collection, CommandIdempotencyCrashContract.FollowUpDocumentId),
            NativeChecksumProfileProtocol.InitialRevision, CommandIdempotencyCrashContract.FollowUpJson);
    }

    private static ReplicatedOperation CreateFollowUp()
    {
        var id = CommandIdempotencyCrashContract.FollowUpCommandId;
        var request = new CommandRequest(id, CommandIdempotencyCrashContract.Partition,
            [new PutDocument(CommandIdempotencyCrashContract.Collection, CommandIdempotencyCrashContract.FollowUpDocumentId,
                CommandIdempotencyCrashContract.FollowUpJson, NativeChecksumProfileProtocol.EmptyRevision)]);
        return CrashDatabase.Operation(OperationKind.Batch, request, id);
    }

    private static DatabaseEngine Open(ZoneTreeStore store) => new(store, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(),
        CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(), CrashExecutionOptions.GraphExecution(),
        CrashExecutionOptions.ChangeFeedExecution(), CrashExecutionOptions.BlobExecution(), CrashExecutionOptions.NativeClaimsExecution(),
        CrashExecutionOptions.TimeSeriesExecution(), CrashExecutionOptions.MovementCheckpoints(), UnavailablePartitionMovementCheckpointVerifier.Instance);
}
