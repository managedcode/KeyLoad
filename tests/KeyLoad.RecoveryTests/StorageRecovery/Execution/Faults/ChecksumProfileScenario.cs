using System.Numerics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal static class ChecksumProfileScenario
{
    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length == ChecksumProfileProtocol.Success || args[ChecksumProfileProtocol.ModeIndex] != ChecksumProfileProtocol.Mode)
        {
            return false;
        }
        if (args.Length != ChecksumProfileProtocol.ArgumentCount)
        {
            throw new InvalidOperationException(ChecksumProfileProtocol.Invalid);
        }
        var phase = args[ChecksumProfileProtocol.PhaseIndex];
        RequireHardwareEligibility();
        var root = args[ChecksumProfileProtocol.RootIndex];
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RunOwnedPhaseAsync(root, phase, failures), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        Console.WriteLine(ChecksumProfileProtocol.CompletionSignalPrefix + phase);
        Console.WriteLine($"{ChecksumProfileProtocol.CapabilitySignalPrefix}{phase}"
            + $"{ChecksumProfileProtocol.SignalSeparator}{ChecksumProfileProtocol.VectorCapability}{Vector.IsHardwareAccelerated}"
            + $"{ChecksumProfileProtocol.SignalSeparator}{ChecksumProfileProtocol.Sse2Capability}{Sse2.IsSupported}"
            + $"{ChecksumProfileProtocol.SignalSeparator}{ChecksumProfileProtocol.Avx2Capability}{Avx2.IsSupported}"
            + $"{ChecksumProfileProtocol.SignalSeparator}{ChecksumProfileProtocol.Sse42Capability}{Sse42.IsSupported}"
            + $"{ChecksumProfileProtocol.SignalSeparator}{ChecksumProfileProtocol.AdvSimdCapability}{AdvSimd.IsSupported}");
        return true;
    }

    private static async Task RunOwnedPhaseAsync(string root, string phase, List<Exception> failures)
    {
        if (phase == ChecksumProfileProtocol.CorruptionRefusal)
        {
            ChecksumCorruptionRefusal.Require(root);
            return;
        }
        using var store = new ZoneTreeStore(new(root), CrashExecutionOptions.StorageExecution(),
            CrashExecutionOptions.PointCacheExecution());
        await ServerFailureObserver.ObserveAsync(() => RunPhaseAsync(root, store, phase), failures);
    }

    private static Task RunPhaseAsync(string root, ZoneTreeStore store, string phase) => phase switch
    {
        ChecksumProfileProtocol.Seed => SeedAsync(root, store),
        ChecksumProfileProtocol.FirstAppend => ContinueAsync(root, store, ChecksumProfileProtocol.OneAppend),
        ChecksumProfileProtocol.SecondAppend => ContinueAsync(root, store, ChecksumProfileProtocol.TwoAppends),
        ChecksumProfileProtocol.Cold => ColdAsync(root, store),
        _ => throw new InvalidOperationException(ChecksumProfileProtocol.Invalid)
    };

    private static void RequireHardwareEligibility()
    {
        if (!(Vector.IsHardwareAccelerated && (Sse2.IsSupported || AdvSimd.IsSupported)))
        {
            throw new InvalidOperationException(ChecksumProfileProtocol.Invalid);
        }
    }

    private static async Task SeedAsync(string root, ZoneTreeStore store)
    {
        ChecksumProfileCorpus.Seed(store);
        var database = CrashDatabase.Create(store);
        CommandIdempotencyCrashData.ConfigureResources(database);
        var tail = database.GetOutboxStatus(CrashFixtureValues.Principal, CommandIdempotencyCrashContract.Partition).Head.Tail;
        var operation = CommandIdempotencyCrashData.CreateOperation(database);
        var receipt = database.Apply(operation).Get<CommitReceipt>();
        CommandIdempotencyCrashAssertions.RequireInitialReceipt(receipt);
        CommandIdempotencyCrashAssertions.AssertCanonicalEffects(database, store, receipt, tail + ChecksumProfileProtocol.OriginalEffects, tail);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(root, CommandIdempotencyCrashContract.CommandEvidenceFile, operation);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(root, CommandIdempotencyCrashContract.ReceiptEvidenceFile, receipt);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(root, CommandIdempotencyCrashContract.SeedTailEvidenceFile, tail);
        await ChecksumProfileEvidence.SaveAsync(root, store);
    }

    private static async Task ContinueAsync(string root, ZoneTreeStore store, int append)
    {
        await ChecksumProfileEvidence.RequireAsync(root, store);
        var previous = append - ChecksumProfileProtocol.OneAppend;
        ChecksumProfileCorpus.Require(store, previous);
        var database = Open(store);
        await RequireModelsAsync(root, database, store, previous, replay: true);
        await ChecksumProfileOperations.AppendAsync(root, database, store, append);
        ChecksumProfileCorpus.Append(store, append);
        await RequireModelsAsync(root, database, store, append, replay: false);
        await ChecksumProfileEvidence.SaveAsync(root, store);
    }

    private static async Task ColdAsync(string root, ZoneTreeStore store)
    {
        await ChecksumProfileEvidence.RequireAsync(root, store);
        ChecksumProfileCorpus.Require(store, ChecksumProfileProtocol.TwoAppends);
        await RequireModelsAsync(root, Open(store), store, ChecksumProfileProtocol.TwoAppends, replay: false);
        await ChecksumProfileEvidence.RequireAsync(root, store);
    }

    private static async Task RequireModelsAsync(string root, DatabaseEngine database, ZoneTreeStore store,
        int appends, bool replay)
    {
        await ChecksumProfileOperations.RequireOriginalAsync(root, database, store, appends, replay);
        for (var append = ChecksumProfileProtocol.OneAppend; append <= appends; append++)
        {
            await ChecksumProfileOperations.RequireAppendAsync(root, database, store, append);
        }
    }

    private static DatabaseEngine Open(ZoneTreeStore store) => new(store, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(),
        CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(), CrashExecutionOptions.GraphExecution(),
        CrashExecutionOptions.ChangeFeedExecution(), CrashExecutionOptions.RuntimeClaimsExecution(),
        CrashExecutionOptions.TimeSeriesExecution());
}
