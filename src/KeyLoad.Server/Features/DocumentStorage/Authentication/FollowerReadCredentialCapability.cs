using KeyLoad.Core;

namespace KeyLoad.Server;

/// <summary>Creates the private native credential witness only at the already authenticated server boundary.</summary>
internal static class FollowerReadCredentialCapability
{
    internal static ReadOnlyMemory<byte> Wrap(HttpContext context, ReadOnlyMemory<byte> publicPayload)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith(ServerProtocol.BearerPrefix, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential); }
        var request = NativeSerialization.Deserialize<ReadFollowerDocumentRequestV1>(publicPayload.Span);
        DatabaseEngine.ValidateFollowerReadRequest(request);
        var witness = DatabaseEngine.IssueCredentialWitness(header[ServerProtocol.BearerPrefix.Length..]);
        return NativeSerialization.Serialize(new FollowerDocumentReadCapability(request, witness));
    }
}
