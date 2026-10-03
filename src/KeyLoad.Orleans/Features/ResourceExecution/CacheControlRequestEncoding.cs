namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlRequestEncoding
{
    internal static void Prepare(ref CacheControlWriter writer, CachePrepareRequest value, bool includeMac)
    {
        writer.String(CacheControlNames.PrepareRequest);
        CacheControlFieldEncoding.Header(ref writer, 0, value.Header);
        writer.StringField(1, value.ExpectedTargetSiloAddress);
        if (includeMac)
        {
            writer.DigestField(2, value.Mac);
        }
    }

    internal static void Grant(ref CacheControlWriter writer, CacheGrantRequest value, bool includeMac)
    {
        writer.String(CacheControlNames.GrantRequest);
        CacheControlFieldEncoding.Header(ref writer, 0, value.Header);
        writer.GuidField(1, value.GrantId);
        CacheControlFieldEncoding.Binding(ref writer, 2, value.TargetBinding);
        CacheControlFieldEncoding.Proof(ref writer, 3, value.Slot0Proof);
        CacheControlFieldEncoding.Proof(ref writer, 4, value.Slot1Proof);
        CacheControlFieldEncoding.Proof(ref writer, 5, value.Slot2Proof);
        if (includeMac)
        {
            writer.DigestField(6, value.Mac);
        }
    }

    internal static void Revoke(ref CacheControlWriter writer, CacheRevokeRequest value, bool includeMac)
    {
        writer.String(CacheControlNames.RevokeRequest);
        CacheControlFieldEncoding.Header(ref writer, 0, value.Header);
        writer.GuidField(1, value.GrantId);
        CacheControlFieldEncoding.Binding(ref writer, 2, value.TargetBinding);
        if (includeMac)
        {
            writer.DigestField(3, value.Mac);
        }
    }

    internal static void Refresh(ref CacheControlWriter writer, CacheRefreshHint value, bool includeMac)
    {
        writer.String(CacheControlNames.RefreshRequest);
        CacheControlFieldEncoding.Header(ref writer, 0, value.Header);
        CacheControlFieldEncoding.Binding(ref writer, 1, value.SenderBinding);
        if (includeMac)
        {
            writer.DigestField(2, value.Mac);
        }
    }
}
