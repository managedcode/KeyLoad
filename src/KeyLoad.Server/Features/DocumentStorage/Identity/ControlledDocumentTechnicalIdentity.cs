using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class ControlledDocumentTechnicalIdentity
{
    private const int GuidBytes = 16;
    internal const string Admit = "keyload-controlled-document-admit-v1";
    internal const string Effect = "keyload-controlled-document-effect-v1";
    internal const string Authorize = "keyload-controlled-document-authorize-v1";
    internal const string GrantAcknowledgement = "keyload-controlled-document-grant-ack-v1";
    internal const string CommandAcknowledgement = "keyload-controlled-document-command-ack-v1";
    internal const string Finalize = "keyload-controlled-document-finalize-v1";

    internal static Guid Derive(ReplicatedOperation original, string purpose)
    {
        var fingerprint = NativeOperationFingerprint.Compute(original);
        var bytes = Encoding.UTF8.GetBytes(purpose + fingerprint);
        var digest = SHA256.HashData(bytes);
        var selected = new Guid(digest.AsSpan(default, GuidBytes));
        if (selected == Guid.Empty || selected == original.Id)
        { throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.Unavailable); }
        return selected;
    }
}
