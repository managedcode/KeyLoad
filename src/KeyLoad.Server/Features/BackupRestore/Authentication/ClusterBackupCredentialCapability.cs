using KeyLoad.Core;

namespace KeyLoad.Server;

internal static class ClusterBackupCredentialCapability
{
    internal static ReadOnlyMemory<byte> Wrap(HttpContext context, ReadOnlyMemory<byte> publicPayload)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith(ServerProtocol.BearerPrefix, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential); }
        var request = NativeSerialization.Deserialize<ClusterBackupOwnerRequest>(publicPayload.Span);
        ClusterBackupRequestValidation.Require(request);
        var witness = DatabaseEngine.IssueCredentialWitness(header[ServerProtocol.BearerPrefix.Length..]);
        return NativeSerialization.Serialize(new ClusterBackupOwnerCapability(request, witness));
    }
}
