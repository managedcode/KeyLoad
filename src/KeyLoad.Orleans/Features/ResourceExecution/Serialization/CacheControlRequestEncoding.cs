namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlRequestEncoding
{
    internal static void Prepare(ref CacheControlWriter writer, CachePrepareRequest value, bool includeMac)
    {
        const int HeaderFieldId = 0;
        const int ExpectedTargetSiloAddressFieldId = 1;
        const int MacFieldId = 2;

        writer.String(CacheControlNames.PrepareRequest);
        CacheControlFieldEncoding.Header(ref writer, HeaderFieldId, value.Header);
        writer.StringField(ExpectedTargetSiloAddressFieldId, value.ExpectedTargetSiloAddress);
        if (includeMac)
        {
            writer.DigestField(MacFieldId, value.Mac);
        }
    }

    internal static void Grant(ref CacheControlWriter writer, CacheGrantRequest value, bool includeMac)
    {
        const int HeaderFieldId = 0;
        const int GrantIdFieldId = 1;
        const int TargetBindingFieldId = 2;
        const int Slot0ProofFieldId = 3;
        const int Slot1ProofFieldId = 4;
        const int Slot2ProofFieldId = 5;
        const int MacFieldId = 6;

        writer.String(CacheControlNames.GrantRequest);
        CacheControlFieldEncoding.Header(ref writer, HeaderFieldId, value.Header);
        writer.GuidField(GrantIdFieldId, value.GrantId);
        CacheControlFieldEncoding.Binding(ref writer, TargetBindingFieldId, value.TargetBinding);
        CacheControlFieldEncoding.Proof(ref writer, Slot0ProofFieldId, value.Slot0Proof);
        CacheControlFieldEncoding.Proof(ref writer, Slot1ProofFieldId, value.Slot1Proof);
        CacheControlFieldEncoding.Proof(ref writer, Slot2ProofFieldId, value.Slot2Proof);
        if (includeMac)
        {
            writer.DigestField(MacFieldId, value.Mac);
        }
    }

    internal static void Revoke(ref CacheControlWriter writer, CacheRevokeRequest value, bool includeMac)
    {
        const int HeaderFieldId = 0;
        const int GrantIdFieldId = 1;
        const int TargetBindingFieldId = 2;
        const int MacFieldId = 3;

        writer.String(CacheControlNames.RevokeRequest);
        CacheControlFieldEncoding.Header(ref writer, HeaderFieldId, value.Header);
        writer.GuidField(GrantIdFieldId, value.GrantId);
        CacheControlFieldEncoding.Binding(ref writer, TargetBindingFieldId, value.TargetBinding);
        if (includeMac)
        {
            writer.DigestField(MacFieldId, value.Mac);
        }
    }

    internal static void Refresh(ref CacheControlWriter writer, CacheRefreshHint value, bool includeMac)
    {
        const int HeaderFieldId = 0;
        const int SenderBindingFieldId = 1;
        const int MacFieldId = 2;

        writer.String(CacheControlNames.RefreshRequest);
        CacheControlFieldEncoding.Header(ref writer, HeaderFieldId, value.Header);
        CacheControlFieldEncoding.Binding(ref writer, SenderBindingFieldId, value.SenderBinding);
        if (includeMac)
        {
            writer.DigestField(MacFieldId, value.Mac);
        }
    }
}
