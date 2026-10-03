namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlReplyEncoding
{
    internal static void Prepare(ref CacheControlWriter writer, CachePrepareReply value, bool includeMac)
    {
        writer.String(CacheControlNames.PrepareReply);
        CacheControlFieldEncoding.NullableCorrelation(ref writer, 0, value.Correlation);
        writer.ByteField(1, (byte)value.Status);
        CacheControlFieldEncoding.NullableProof(ref writer, 2, value.Proof);
        if (includeMac)
        {
            writer.DigestField(3, value.Mac);
        }
    }

    internal static void Grant(ref CacheControlWriter writer, CacheGrantReply value, bool includeMac)
    {
        writer.String(CacheControlNames.GrantReply);
        CacheControlFieldEncoding.NullableCorrelation(ref writer, 0, value.Correlation);
        writer.ByteField(1, (byte)value.Status);
        writer.GuidField(2, value.GrantId);
        CacheControlFieldEncoding.NullableBinding(ref writer, 3, value.AcceptedBinding);
        writer.Int64Field(4, value.AcceptedSequence);
        if (includeMac)
        {
            writer.DigestField(5, value.Mac);
        }
    }

    internal static void Revoke(ref CacheControlWriter writer, CacheRevokeReply value, bool includeMac)
    {
        writer.String(CacheControlNames.RevokeReply);
        CacheControlFieldEncoding.NullableCorrelation(ref writer, 0, value.Correlation);
        writer.ByteField(1, (byte)value.Status);
        writer.GuidField(2, value.GrantId);
        writer.ByteField(3, (byte)value.Effect);
        if (includeMac)
        {
            writer.DigestField(4, value.Mac);
        }
    }

    internal static void Refresh(ref CacheControlWriter writer, CacheRefreshReceipt value, bool includeMac)
    {
        writer.String(CacheControlNames.RefreshReply);
        CacheControlFieldEncoding.NullableCorrelation(ref writer, 0, value.Correlation);
        writer.ByteField(1, (byte)value.Status);
        writer.GuidField(2, value.ActualCoordinatorSessionId);
        writer.GuidField(3, value.CurrentRoundNonce);
        writer.NullableStringField(4, value.ActualCoordinatorSiloAddress);
        if (includeMac)
        {
            writer.DigestField(5, value.Mac);
        }
    }
}
