using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeBlobWholeParentAssertions
{
    internal static async Task RequireAsync(RemoteTransferNativeAdministrator administrator,
        RemoteTransferNativeParentSeed seed, PartitionMoveResult terminal, CancellationToken token)
    {
        await Assert.That(terminal.Phase).IsEqualTo(PartitionMovePhase.Retired);
        await administrator.RequireAsync(seed.Partition, McpToolNames.AdminPartitionMove, seed.Request,
            ct => administrator.Source.MovePartitionAsync(seed.Request, ct), terminal, token).ConfigureAwait(false);
        await seed.RequireAsync(token).ConfigureAwait(false);
    }

    internal static Task RequireEvidenceAsync(TwoRf3MembershipWave wave,
        RemoteTransferNativeMembership captured, CancellationToken token)
        => NativeBlobWireEvidenceReader.RequireAsync(wave.nativeBlobWireControls
            ?? throw new InvalidOperationException("The actual ten-arm Blob owner is absent."),
            captured.Source, captured.Destination, captured.SourceSecret, captured.DestinationSecret, token);
}
