using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class NativeCommandObservationHandleCodec
{
    internal static NativeCommandObservationHandleV1 Verify(DatabaseEngine database, string opaqueToken)
    {
        var maximum = GrainRequestTokenBounds.MaximumCharacters(database);
        if (string.IsNullOrEmpty(opaqueToken) || opaqueToken.Length > maximum)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var handle = database.Verify<NativeCommandObservationHandleV1>(opaqueToken, maximum);
        if (handle.Version != NativeCommandObservationProtocol.CurrentVersion
            || handle.Purpose != NativeCommandObservationProtocol.Purpose
            || handle.ObservationId == Guid.Empty || handle.RequestId == Guid.Empty || handle.CommandId == Guid.Empty
            || handle.CommandDigest.Length != NativeCommandObservationProtocol.DigestBytes)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        // No new UTC expiration check: the registry's pre-effect original budget and token own elapsed time.
        return handle;
    }
}
