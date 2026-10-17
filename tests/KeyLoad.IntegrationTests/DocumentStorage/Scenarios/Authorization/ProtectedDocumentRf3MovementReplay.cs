using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using P = KeyLoad.IntegrationTests.Features.DocumentStorage.ProtectedDocumentRf3MovementProtocol;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal static class ProtectedDocumentRf3MovementReplay
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, KeyLoadClient source,
        KeyLoadClient target, McpOfficialClient official, ProtectedDocumentRf3MovementState movement,
        CommandRequest command, CommitReceipt receipt, string deniedKey, CancellationToken token)
    {
        var before = await ProtectedDocumentRf3MovementImage.CaptureAsync(source, target, movement, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await PartitionMovementPublicCallerReplay.RequireAsync(source, official, movement.Request, movement.Receipt, token);
            await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, deniedKey, async denied =>
            {
                await PartitionMovementPublicCallerReplay.RequireDeniedSubjectAsync(denied.Sdk, denied.Mcp, movement.Request, token);
                return true;
            }, token);
            var expected = new CommitReceipt(command.CommandId, receipt.Token,
                [new(P.Put, ProtectedDocumentRf3Seed.Collection, ProtectedDocumentRf3Seed.Entity, P.DocumentRevision)],
                DurabilityProfile.QuorumProcessDurable);
            await NativeActivationOriginalSessionConservation.RequireReceiptAsync(expected,
                await McpCallerAssertions.SdkSuccessAsync(await source.CommitAsync(command, token)));
            await NativeActivationOriginalSessionConservation.RequireReceiptAsync(expected,
                (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await official.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(StringComparer.Ordinal.Equals(before,
                await ProtectedDocumentRf3MovementImage.CaptureAsync(source, target, movement, token))).IsTrue();
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
