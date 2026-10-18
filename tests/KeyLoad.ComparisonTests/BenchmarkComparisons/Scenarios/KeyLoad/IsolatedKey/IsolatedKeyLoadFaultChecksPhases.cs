using Aspire.Hosting;
using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-005: actual storage-silo loss must never acknowledge fewer than the required copies.</summary>
internal static class IsolatedKeyLoadFaultChecksPhases
{
    internal static async Task RunAsync(DistributedApplication app, int nodes, string admin,
        IsolatedKeyLoadFaultChecksSeed seed, IsolatedKeyLoadFaultChecksNative native,
        IsolatedKeyLoadFaultChecksEvidence evidence, CancellationToken token)
    {
        var targets = Enumerable.Range(1, nodes).ToArray();
        for (var phase = 0; phase < targets.Length; phase++)
        {
            await PhaseAsync(app, nodes, admin, seed, native, evidence, phase + 1, targets[phase], token);
        }
    }

    private static async Task PhaseAsync(DistributedApplication app, int nodes, string admin,
        IsolatedKeyLoadFaultChecksSeed seed, IsolatedKeyLoadFaultChecksNative native,
        IsolatedKeyLoadFaultChecksEvidence evidence, int phase, int killed, CancellationToken token)
    {
        var before = await IsolatedKeyLoadFaultChecksMembership.WaitAsync(app, nodes, admin, token);
        await IsolatedKeyLoadFaultChecksAssertions.MembershipAsync(evidence.Baseline, before, 0);
        var reference = seed.PhaseDocument(phase);
        var command = seed.Create(reference);
        var receipt = new IsolatedKeyLoadFaultChecksPhaseReceipt(phase, killed, command.CommandId,
            "requiredCopiesUnavailable");
        evidence.Phases.Add(receipt);
        evidence.Stage = "nativeKill";
        var stop = await native.KillAsync(killed, token);
        var live = nodes == 1 ? 1 : killed == 1 ? 2 : 1;
        evidence.Stage = "copyAcknowledgementAssertions";
        await WhileStoppedAsync(app, nodes, live, admin, seed, command, receipt, token);
        evidence.Stage = "nativeRestart";
        await native.RestartAsync(stop, token);
        evidence.Stage = "publicRecovery";
        await IsolatedKeyLoadFaultChecksMembership.WaitAsync(app, nodes, admin, token);
        using var recoveredHttp = IsolatedKeyLoadPublicChecksProtocol.CreateHttp(app, live);
        var recoveredSdk = new KeyLoadClient(recoveredHttp, admin, ComparisonClientOptions.Execution());
        var committed = await ResolveAsync(recoveredSdk, nodes, command, reference, token);
        receipt.ResolvedPosition = committed.Token.Position;
        await RecoveredAsync(app, nodes, killed, admin, seed, command, committed, reference, before, receipt, token);
        receipt.Complete = true;
    }

    private static async Task WhileStoppedAsync(DistributedApplication app, int nodes, int live, string admin,
        IsolatedKeyLoadFaultChecksSeed seed, CommandRequest command,
        IsolatedKeyLoadFaultChecksPhaseReceipt receipt, CancellationToken token)
    {
        var rejected = await IsolatedKeyLoadFaultChecksCopyAvailability.RejectAsync(
            app, live, admin, seed, command, nodes > 1, token);
        receipt.WriteRejection = rejected;
    }

    private static async Task<CommitReceipt> ResolveAsync(KeyLoadClient sdk, int nodes, CommandRequest command,
        EntityRef reference, CancellationToken token)
    {
        if (nodes == 1)
        {
            await Assert.That(await IsolatedKeyLoadPublicChecksAssertions.DocumentReadAsync(await IsolatedKeyLoadFaultChecksCalls.RunAsync(
                attempt => sdk.GetAsync(reference, attempt), token))).IsNull();
        }
        var resolved = await IsolatedKeyLoadFaultChecksCopyAvailability.ResolveAsync(sdk, command, token);
        await IsolatedKeyLoadFaultChecksAssertions.CommandAsync(sdk, command, resolved, reference, nodes, token);
        return resolved;
    }

    private static async Task RecoveredAsync(DistributedApplication app, int nodes, int killed, string admin,
        IsolatedKeyLoadFaultChecksSeed seed, CommandRequest command, CommitReceipt committed, EntityRef reference,
        IsolatedKeyLoadFaultChecksMembership[] before, IsolatedKeyLoadFaultChecksPhaseReceipt receipt, CancellationToken token)
    {
        var restored = await IsolatedKeyLoadFaultChecksMembership.WaitAsync(app, nodes, admin, token);
        await IsolatedKeyLoadFaultChecksAssertions.MembershipAsync(before, restored, killed);
        receipt.Restored = restored;
        for (var index = 1; index <= nodes; index++)
        {
            using var http = IsolatedKeyLoadPublicChecksProtocol.CreateHttp(app, index);
            var sdk = new KeyLoadClient(http, admin, ComparisonClientOptions.Execution());
            await IsolatedKeyLoadPublicChecksAssertions.DocumentAsync(
                (await IsolatedKeyLoadPublicChecksAssertions.DocumentReadAsync(await IsolatedKeyLoadFaultChecksCalls.RunAsync(
                    attempt => sdk.GetAsync(reference, attempt), token)))!,
                reference, IsolatedKeyLoadFaultChecksSeed.Payload, 1);
        }
        await IsolatedKeyLoadFaultChecksAuthority.VerifyAsync(app, killed, admin, seed, token);
        await using var mcp = await IsolatedKeyLoadFaultChecksCalls.ConnectAsync(app, killed, admin, token);
        await IsolatedKeyLoadPublicChecksAssertions.EqualAsync(committed,
            await IsolatedKeyLoadFaultChecksCalls.RunAsync(
                attempt => mcp.SuccessAsync<CommitReceipt>(IsolatedKeyLoadPublicChecksProtocol.Commit, command, attempt), token));
    }
}
