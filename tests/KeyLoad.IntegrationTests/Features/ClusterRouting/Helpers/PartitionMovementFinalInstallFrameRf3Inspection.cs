using KeyLoad.Client;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ClusterRouting.Processes;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Original live node identities bind the stopped checksum-verified native frame child.</summary>
internal static class PartitionMovementFinalInstallFrameRf3Inspection
{
    private const string DatabaseDirectory = "database";
    private const string NodeLock = "node.owner.lock";
    private const int SuccessfulChildExit = 0;

    internal static async Task<Dictionary<string, NodeStatus>> CaptureOwnersAsync(TwoRf3MembershipWave wave,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, NodeStatus>(StringComparer.Ordinal);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            using var http = McpCallerHttp.Create(wave.Application, node);
            var sdk = new KeyLoadClient(http, wave.Profile.AdminKey, IntegrationClientOptions.Execution());
            var status = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(cancellationToken).ConfigureAwait(false));
            await Assert.That(status.RoutingReady).IsTrue();
            result.Add(node, status);
        }
        return result;
    }

    internal static async Task<NativeInstallFrameInspectionReceipt> ReadAsync(TwoRf3MembershipWave wave,
        string node, NodeStatus original, PartitionRef partition, string actualPrincipalId,
        Guid actualCommandId, int actualCap, CancellationToken cancellationToken)
    {
        var root = Path.Combine(wave.OwnedDataRoot, node);
        var request = new NativeInstallFrameInspectionRequest(C1OutcomeInspectionProtocol.Version,
            Path.Combine(root, DatabaseDirectory), Guid.Parse(original.NodeId), original.Incarnation,
            actualPrincipalId, actualCommandId, partition, actualCap);
        var process = await C1OutcomeInspectionProcess.RunFrameAsync(NativeInstallFrameInspectionJson.SerializeRequest(request),
            Path.Combine(root, NodeLock), cancellationToken).ConfigureAwait(false);
        await RequireJoinedAsync(process).ConfigureAwait(false);
        var receipt = NativeInstallFrameInspectionJson.ReadReceipt(process.StandardOutput);
        await Assert.That(receipt.NodeId).IsEqualTo(request.ExpectedNodeId);
        await Assert.That(receipt.Incarnation).IsEqualTo(original.Incarnation);
        await Assert.That(receipt.CommandId).IsEqualTo(actualCommandId);
        return receipt;
    }

    private static async Task RequireJoinedAsync(C1OutcomeInspectionProcessResult process)
    {
        await Assert.That(process.ProcessId).IsGreaterThan(SuccessfulChildExit);
        await Assert.That(process.StandardOutputExceeded).IsFalse();
        await Assert.That(process.StandardErrorExceeded).IsFalse();
        await Assert.That(process.ProcessReaped).IsTrue();
        await Assert.That(process.InputWriterSettled).IsTrue();
        await Assert.That(process.StandardOutputReaderSettled).IsTrue();
        await Assert.That(process.StandardErrorReaderSettled).IsTrue();
        await Assert.That(process.ProcessHandleClosed).IsTrue();
        await Assert.That(process.OuterOwnerReleased).IsTrue();
        if (process.ExitCode != SuccessfulChildExit)
        {
            var failure = C1OutcomeInspectionFailureEvidence.Read(process.StandardError);
            throw new InvalidOperationException(C1OutcomeInspectionFailureEvidence.Describe(failure));
        }
        await Assert.That(process.ExitCode).IsEqualTo(SuccessfulChildExit);
        await Assert.That(process.StandardError).IsEmpty();
    }
}
