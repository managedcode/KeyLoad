namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlMessageEncoding
{
    internal static void Object(ref CacheControlWriter writer, ICacheControlMessage message, bool includeMac)
    {
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
                throw new ArgumentException("Unknown cache control shape.", nameof(message));
        }
    }

    internal static void SigningPrefix(ref CacheControlWriter writer, ICacheControlMessage message)
    {
        var purpose = message switch
        {
            CacheReadyProof => CacheControlNames.ProofPurpose,
            ICacheControlRequest => CacheControlNames.RequestPurpose,
            ICacheControlReply => CacheControlNames.ReplyPurpose,
            _ => throw new ArgumentException("Unknown cache control shape.", nameof(message))
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
            CacheReadyProof => 5,
            _ => throw new ArgumentException("Unknown cache control shape.", nameof(message))
        };
}
