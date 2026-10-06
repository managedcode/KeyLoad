namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlMessageEncoding
{
    private const int ReadyProofOpcode = 5;
    private const string OpcodeFailureMessage = "Unknown cache control shape.";

    internal static void Object(ref CacheControlWriter writer, ICacheControlMessage message, bool includeMac)
    {
        const string ObjectFailureMessage = "Unknown cache control shape.";

        switch (message)
        {
            case CacheReadyProof value:
                CacheControlCoreEncoding.Proof(ref writer, value, includeMac);
                break;
            case CachePrepareRequest value:
                CacheControlRequestEncoding.Prepare(ref writer, value, includeMac);
                break;
            case CacheGrantRequest value:
                CacheControlRequestEncoding.Grant(ref writer, value, includeMac);
                break;
            case CacheRevokeRequest value:
                CacheControlRequestEncoding.Revoke(ref writer, value, includeMac);
                break;
            case CacheRefreshHint value:
                CacheControlRequestEncoding.Refresh(ref writer, value, includeMac);
                break;
            case CachePrepareReply value:
                CacheControlReplyEncoding.Prepare(ref writer, value, includeMac);
                break;
            case CacheGrantReply value:
                CacheControlReplyEncoding.Grant(ref writer, value, includeMac);
                break;
            case CacheRevokeReply value:
                CacheControlReplyEncoding.Revoke(ref writer, value, includeMac);
                break;
            case CacheRefreshReceipt value:
                CacheControlReplyEncoding.Refresh(ref writer, value, includeMac);
                break;
            default:
                throw new ArgumentException(ObjectFailureMessage, nameof(message));
        }
    }

    internal static void SigningPrefix(ref CacheControlWriter writer, ICacheControlMessage message)
    {
        const string SigningPrefixFailureMessage = "Unknown cache control shape.";

        var purpose = message switch
        {
            CacheReadyProof => CacheControlNames.ProofPurpose,
            ICacheControlRequest => CacheControlNames.RequestPurpose,
            ICacheControlReply => CacheControlNames.ReplyPurpose,
            _ => throw new ArgumentException(SigningPrefixFailureMessage, nameof(message))
        };
        writer.String(purpose);
        writer.Byte(Opcode(message));
    }

    internal static byte Opcode(ICacheControlMessage message)
        => message switch
        {
            CachePrepareRequest or CachePrepareReply => (byte)CacheControlOperation.Prepare,
            CacheGrantRequest or CacheGrantReply => (byte)CacheControlOperation.Grant,
            CacheRevokeRequest or CacheRevokeReply => (byte)CacheControlOperation.Revoke,
            CacheRefreshHint or CacheRefreshReceipt => (byte)CacheControlOperation.Refresh,
            CacheReadyProof => ReadyProofOpcode,
            _ => throw new ArgumentException(OpcodeFailureMessage, nameof(message))
        };
}
