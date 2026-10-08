using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Reads every actual authenticated native page under its borrowed original capture owner.</summary>
internal static class ControlledPartitionMovementCapturedPageReader
{
    internal static async Task<PartitionMovementPageResult[]> ReadAsync(ControlledPartitionMovementNode source,
        PartitionMovementSourceOwner owner, ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        PartitionMovementCaptureHandle handle, PartitionMovementTransportRequest request,
        CancellationToken cancellationToken)
    {
        var pages = new PartitionMovementPageResult[handle.PageCount];
        for (var ordinal = 0; ordinal < pages.Length; ordinal++)
        {
            var pageRequest = request with
            {
                Action = PartitionMovementTransportAction.Page,
                HandleId = handle.HandleId,
                Ordinal = ordinal,
                Envelope = request.Envelope with { Nonce = Guid.NewGuid() }
            };
            var page = await ControlledPartitionMovementPeerVerification.VerifyAsync(source, runtime,
                admission, pageRequest, cancellationToken);
            pages[ordinal] = await owner.ReadPageAsync(ControlledPartitionMovementCaptureFlow.Principal(source, page),
                page.Envelope, new(handle.HandleId, ordinal), cancellationToken);
            await Assert.That(pages[ordinal].HandleId).IsEqualTo(handle.HandleId);
            await Assert.That(pages[ordinal].Ordinal).IsEqualTo(ordinal);
        }
        return pages;
    }
}
