using KeyLoad.Clustering;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeBlobWireEvidenceReader
{
    private const int NativeCarrierMaximumBytes = 8_192;
    internal static async Task RequireAsync(NativeBlobWireProbeFixture fixture,
        IReadOnlyList<ReplicaSiloDiscovery> actualSourceAtCapture, IReadOnlyList<ReplicaSiloDiscovery> actualDestinationAtCapture,
        string sourceSecret, string destinationSecret, CancellationToken token)
    {
        foreach (var arm in fixture.Arms.Arms)
        {
            token.ThrowIfCancellationRequested();
            var claimBytes = fixture.Read(NativeBlobWireNames.Claim(arm.Identity));
            var claim = fixture.Codec.Read<NativeBlobWireClaim>(claimBytes);
            var digest = NativeBlobWireCodec.Digest(claimBytes);
            var complete = fixture.Codec.Read<NativeBlobWireCompletion>(fixture.Read(NativeBlobWireNames.Completion(arm.Identity)));
            var expected = (int)arm.Identity < (int)NativeBlobWireIdentity.Metadata
                ? NativeBlobWireProbeProtocol.MaximumFaultCount : NativeBlobWireProbeProtocol.CommonFaultCount;
            if (claim.Version != NativeBlobWireProbeProtocol.Version || claim.OwnerDigest != fixture.Arms.OwnerDigest
                || claim.ArmsDigest != NativeBlobWireCodec.Digest(fixture.Read(NativeBlobWireProbeProtocol.ArmsFile))
                || claim.Identity != arm.Identity || claim.ArmId != arm.ArmId || claim.SessionId != fixture.SessionId
                || complete.Version != NativeBlobWireProbeProtocol.Version || complete.SessionId != fixture.SessionId
                || complete.ClaimDigest != digest || complete.Identity != arm.Identity
                || complete.Count != expected || complete.Results.Length != expected)
            {
                throw Invalid();
            }
            for (var extra = expected + 1; extra <= NativeBlobWireProbeProtocol.MaximumFaultCount; extra++)
            {
                if (fixture.Exists(NativeBlobWireNames.Result(arm.Identity, extra)))
                {
                    throw Invalid();
                }
            }
            await RequireResultsAsync(fixture, actualSourceAtCapture, actualDestinationAtCapture, sourceSecret, destinationSecret, arm, claim, digest, complete, expected, token).ConfigureAwait(false);
        }
    }

    private static async Task RequireResultsAsync(NativeBlobWireProbeFixture fixture, IReadOnlyList<ReplicaSiloDiscovery> actualSourceAtCapture, IReadOnlyList<ReplicaSiloDiscovery> actualDestinationAtCapture, string sourceSecret, string destinationSecret, NativeBlobWireArm arm, NativeBlobWireClaim claim, string digest, NativeBlobWireCompletion complete, int expected, CancellationToken token)
    {
        byte[]? originalBytes = null;
        for (var ordinal = 1; ordinal <= expected; ordinal++)
        {
            var bytes = fixture.Read(NativeBlobWireNames.Result(arm.Identity, ordinal));
            var result = fixture.Codec.Read<NativeBlobWireResult>(bytes);
            var entry = complete.Results[ordinal - 1];
            if (result.Version != NativeBlobWireProbeProtocol.Version || result.SessionId != fixture.SessionId
                || result.ClaimDigest != digest || result.Identity != arm.Identity || result.Ordinal != ordinal
                || (int)result.Variation != ordinal - 1 || entry.Ordinal != ordinal
                || entry.Variation != result.Variation || entry.RecordDigest != NativeBlobWireCodec.Digest(bytes))
            {
                throw Invalid();
            }
            originalBytes ??= result.OriginalNativeRequest;
            if (!originalBytes.AsSpan().SequenceEqual(result.OriginalNativeRequest))
            {
                throw Invalid();
            }
            var original = NativeSerialization.Deserialize<RemoteModelNativeRequest>(result.OriginalNativeRequest);
            var request = NativeSerialization.Deserialize<RemoteModelNativeRequest>(result.NativeRequest);
            var reply = NativeSerialization.Deserialize<RemoteModelNativeReply>(result.NativeReply);
            RequireCall(fixture, arm, claim, original);
            await Assert.That(result.OriginalNativeRequest.Length).IsLessThanOrEqualTo(NativeCarrierMaximumBytes);
            await Assert.That(result.NativeRequest.Length).IsLessThanOrEqualTo(NativeCarrierMaximumBytes);
            await Assert.That(result.NativeReply.Length).IsLessThanOrEqualTo(NativeCarrierMaximumBytes);
            await Console.Out.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new
            {
                Kind = "ActualNativeBlobSignedCarrierBytes",
                Identity = arm.Identity,
                Variation = result.Variation,
                CarrierAssertionMaximumBytes = NativeCarrierMaximumBytes,
                ProductionTransportMaximumBytes = RemoteDocumentProtocol.MaximumBodyBytes,
                OriginalRequestBytes = result.OriginalNativeRequest.Length,
                FaultRequestBytes = result.NativeRequest.Length,
                ReplyBytes = result.NativeReply.Length
            }));
            await PartitionMovementNativeBlobFaultAssertions.RequireAsync(original,
                new(result.Variation, request.Payload, original.Signature), request, reply,
                actualSourceAtCapture, actualDestinationAtCapture, sourceSecret, destinationSecret, token).ConfigureAwait(false);
        }
    }

    private static void RequireCall(NativeBlobWireProbeFixture fixture, NativeBlobWireArm arm,
        NativeBlobWireClaim claim, RemoteModelNativeRequest original)
    {
        if ((int)arm.Identity >= (int)NativeBlobWireIdentity.Metadata)
        {
            if (original.Channel != RemoteModelNativeChannel.ResourceLifetimeRead)
            {
                throw Invalid();
            }
            var call = NativeSerialization.Deserialize<RemoteResourceLifetimeReadCallV2>(original.Payload.Span);
            if (NativeBlobWireScope.Match(fixture.Owner, fixture.Arms, call)?.ArmId != arm.ArmId)
            {
                throw Invalid();
            }
            NativeBlobWireClaims.RequireOwn(claim, call);
            return;
        }
        if (original.Channel != RemoteModelNativeChannel.ResourceLifetimeBlobOutcome)
        {
            throw Invalid();
        }
        var outcome = NativeSerialization.Deserialize<RemoteControlledBlobOutcomeCallV2>(original.Payload.Span);
        if (NativeBlobWireScope.Match(fixture.Owner, fixture.Arms, outcome)?.ArmId != arm.ArmId)
        {
            throw Invalid();
        }
        NativeBlobWireClaims.RequireOwn(claim, outcome);
    }

    private static InvalidOperationException Invalid() => new(NativeBlobWireProbeProtocol.InvalidEvidence);
}
