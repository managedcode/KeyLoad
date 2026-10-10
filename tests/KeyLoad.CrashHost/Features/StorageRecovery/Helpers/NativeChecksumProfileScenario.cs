using System.Numerics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using KeyLoad.Core;
using KeyLoad.CrashHost.Features.DocumentStorage;
using KeyLoad.Security;
using KeyLoad.Server;
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
            + $"{NativeChecksumProfileProtocol.SignalSeparator}{NativeChecksumProfileProtocol.Sse2Capability}{Sse2.IsSupported}"
            + $"{NativeChecksumProfileProtocol.SignalSeparator}{NativeChecksumProfileProtocol.Avx2Capability}{Avx2.IsSupported}"
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
        NativeChecksumProfileProtocol.FirstAppend => ContinueAsync(root, store, NativeChecksumProfileProtocol.OneAppend),
        NativeChecksumProfileProtocol.SecondAppend => ContinueAsync(root, store, NativeChecksumProfileProtocol.TwoAppends),
        NativeChecksumProfileProtocol.Cold => ColdAsync(root, store),
        _ => throw new InvalidOperationException(NativeChecksumProfileProtocol.Invalid)
    };

    private static void RequireProfile(string profile)
    {
        var settings = NativeChecksumProfileOptionsBinding.Child().Value;
        if (profile is not (NativeChecksumProfileProtocol.Enabled or NativeChecksumProfileProtocol.Disabled)
            || settings.DotnetIntrinsics != profile
            || settings.ComPlusIntrinsics != profile
            || profile == NativeChecksumProfileProtocol.Disabled && (Vector.IsHardwareAccelerated || Sse2.IsSupported || Avx2.IsSupported || Sse42.IsSupported || AdvSimd.IsSupported)
            || profile == NativeChecksumProfileProtocol.Enabled && !(Vector.IsHardwareAccelerated && (Sse2.IsSupported || AdvSimd.IsSupported)))
        { throw new InvalidOperationException(NativeChecksumProfileProtocol.Invalid); }
    }

    private static async Task SeedAsync(string root, ZoneTreeStore store)
    {
        NativeChecksumProfileCorpus.Seed(store);
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

    private static async Task ContinueAsync(string root, ZoneTreeStore store, int append)
    {
        await NativeChecksumProfileEvidence.RequireAsync(root, store);
        var previous = append - NativeChecksumProfileProtocol.OneAppend;
        NativeChecksumProfileCorpus.Require(store, previous);
        var database = Open(store);
        await RequireModelsAsync(root, database, store, previous, replay: true);
        await NativeChecksumProfileOperations.AppendAsync(root, database, store, append);
        NativeChecksumProfileCorpus.Append(store, append);
        await RequireModelsAsync(root, database, store, append, replay: false);
        await NativeChecksumProfileEvidence.SaveAsync(root, store);
    }

    private static async Task ColdAsync(string root, ZoneTreeStore store)
    {
        await NativeChecksumProfileEvidence.RequireAsync(root, store);
        NativeChecksumProfileCorpus.Require(store, NativeChecksumProfileProtocol.TwoAppends);
        await RequireModelsAsync(root, Open(store), store, NativeChecksumProfileProtocol.TwoAppends, replay: false);
        await NativeChecksumProfileEvidence.RequireAsync(root, store);
    }

    private static async Task RequireModelsAsync(string root, DatabaseEngine database, ZoneTreeStore store,
        int appends, bool replay)
    {
        await NativeChecksumProfileOperations.RequireOriginalAsync(root, database, store, appends, replay);
        for (var append = NativeChecksumProfileProtocol.OneAppend; append <= appends; append++)
        { await NativeChecksumProfileOperations.RequireAppendAsync(root, database, store, append); }
    }

    private static DatabaseEngine Open(ZoneTreeStore store) => new(store, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(),
        CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(), CrashExecutionOptions.GraphExecution(),
        CrashExecutionOptions.ChangeFeedExecution(), CrashExecutionOptions.BlobExecution(), CrashExecutionOptions.NativeClaimsExecution(),
        CrashExecutionOptions.TimeSeriesExecution(), CrashExecutionOptions.MovementCheckpoints(), UnavailablePartitionMovementCheckpointVerifier.Instance);
}
