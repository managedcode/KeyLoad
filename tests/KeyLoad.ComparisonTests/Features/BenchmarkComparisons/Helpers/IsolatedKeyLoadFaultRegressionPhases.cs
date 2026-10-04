using Aspire.Hosting;
using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-005: actual RF1 loss, each RF2 voter loss, and RF3 follower loss use the same public oracle.</summary>
internal static class IsolatedKeyLoadFaultRegressionPhases
{
    internal static async Task RunAsync(DistributedApplication app, int nodes, string admin,
        IsolatedKeyLoadFaultRegressionSeed seed, IsolatedKeyLoadFaultRegressionNative native,
        IsolatedKeyLoadFaultRegressionEvidence evidence, CancellationToken token)
    {
        var targets = nodes == 3
            ? new[] { evidence.Baseline.First(item => item.LocalVoter != item.Leader).Index }
            : Enumerable.Range(1, nodes).ToArray();
        for (var phase = 0; phase < targets.Length; phase++)
        {
            await PhaseAsync(app, nodes, admin, seed, native, evidence, phase + 1, targets[phase], token);
        }
    }

    private static async Task PhaseAsync(DistributedApplication app, int nodes, string admin,
        IsolatedKeyLoadFaultRegressionSeed seed, IsolatedKeyLoadFaultRegressionNative native,
        IsolatedKeyLoadFaultRegressionEvidence evidence, int phase, int killed, CancellationToken token)
    {
        var before = await IsolatedKeyLoadFaultRegressionMembership.WaitAsync(app, nodes, admin, seed.AckReceipt.Token.Position, token);
        await IsolatedKeyLoadFaultRegressionAssertions.MembershipAsync(evidence.Baseline, before, 0, seed.AckReceipt.Token.Position);
        killed = nodes == 3 ? before.First(item => item.LocalVoter != item.Leader).Index : killed;
        var reference = seed.PhaseDocument(phase);
        var command = seed.Create(reference);
        var receipt = new IsolatedKeyLoadFaultRegressionPhaseReceipt(phase, killed, command.CommandId,
            nodes == 3 ? "majorityAvailable" : "majorityLost");
        evidence.Phases.Add(receipt);
        evidence.Stage = "nativeKill";
        var stop = await native.KillAsync(killed, token);
        var live = nodes == 1 ? 1 : killed == 1 ? 2 : 1;
        using var http = IsolatedKeyLoadPublicRegressionProtocol.CreateHttp(app, live);
        var sdk = new KeyLoadClient(http, admin);
        evidence.Stage = "quorumAssertions";
        var committed = await WhileStoppedAsync(app, nodes, live, admin, sdk, seed, command, reference, receipt, token);
        evidence.Stage = "nativeRestart";
        await native.RestartAsync(stop, token);
        evidence.Stage = "publicRecovery";
        await IsolatedKeyLoadFaultRegressionMembership.WaitAsync(app, nodes, admin, seed.AckReceipt.Token.Position, token);
        using var recoveredHttp = IsolatedKeyLoadPublicRegressionProtocol.CreateHttp(app, live);
        var recoveredSdk = new KeyLoadClient(recoveredHttp, admin);
        committed = await ResolveAsync(recoveredSdk, nodes, command, reference, committed, token);
        receipt.ResolvedPosition = committed.Token.Position;
        await RecoveredAsync(app, nodes, killed, admin, seed, command, committed, reference, before, receipt, token);
        receipt.Complete = true;
    }

    private static async Task<CommitReceipt?> WhileStoppedAsync(DistributedApplication app, int nodes, int live, string admin,
        KeyLoadClient sdk, IsolatedKeyLoadFaultRegressionSeed seed, CommandRequest command, EntityRef reference,
        IsolatedKeyLoadFaultRegressionPhaseReceipt receipt, CancellationToken token)
    {
        if (nodes < 3)
        {
            var rejected = await IsolatedKeyLoadFaultRegressionQuorum.RejectAsync(
                app, live, admin, seed, command, nodes == 2, token);
            receipt.NativeMcpAuthentication503 = rejected.NativeMcpAuthentication503;
            receipt.WriteRejection = rejected.WriteCode;
            return null;
        }
        var committed = await IsolatedKeyLoadFaultRegressionQuorum.ResolveAsync(sdk, command, token);
        await IsolatedKeyLoadFaultRegressionAssertions.CommandAsync(sdk, command, committed, reference, token);
        return committed;
    }

    private static async Task<CommitReceipt> ResolveAsync(KeyLoadClient sdk, int nodes, CommandRequest command,
        EntityRef reference, CommitReceipt? committed, CancellationToken token)
    {
        if (nodes == 1)
        {
            await Assert.That(await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
                attempt => sdk.GetAsync(reference, attempt), token))).IsNull();
        }
        var resolved = await IsolatedKeyLoadFaultRegressionQuorum.ResolveAsync(sdk, command, token);
        if (committed is not null)
        {
            await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(committed, resolved);
        }
        await IsolatedKeyLoadFaultRegressionAssertions.CommandAsync(sdk, command, resolved, reference, token);
        return resolved;
    }

    private static async Task RecoveredAsync(DistributedApplication app, int nodes, int killed, string admin,
        IsolatedKeyLoadFaultRegressionSeed seed, CommandRequest command, CommitReceipt committed, EntityRef reference,
        IsolatedKeyLoadFaultRegressionMembership[] before, IsolatedKeyLoadFaultRegressionPhaseReceipt receipt, CancellationToken token)
    {
        var restored = await IsolatedKeyLoadFaultRegressionMembership.WaitAsync(app, nodes, admin, committed.Token.Position, token);
        await IsolatedKeyLoadFaultRegressionAssertions.MembershipAsync(before, restored, killed, committed.Token.Position);
        receipt.Restored = restored;
        for (var index = 1; index <= nodes; index++)
        {
            using var http = IsolatedKeyLoadPublicRegressionProtocol.CreateHttp(app, index);
            var sdk = new KeyLoadClient(http, admin);
            await IsolatedKeyLoadPublicRegressionAssertions.DocumentAsync(
                (await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
                    attempt => sdk.GetAsync(reference, attempt), token)))!,
                reference, IsolatedKeyLoadFaultRegressionSeed.Payload, 1);
        }
        await IsolatedKeyLoadFaultRegressionAuthority.VerifyAsync(app, killed, admin, seed, token);
        await using var mcp = await IsolatedKeyLoadFaultRegressionCalls.ConnectAsync(app, killed, admin, token);
        await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(committed,
            await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
                attempt => mcp.SuccessAsync<CommitReceipt>(IsolatedKeyLoadPublicRegressionProtocol.Commit, command, attempt), token));
    }
}
