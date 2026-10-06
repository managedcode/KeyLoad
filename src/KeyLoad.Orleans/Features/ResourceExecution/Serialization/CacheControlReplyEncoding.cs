namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlReplyEncoding
{
    internal static void Prepare(ref CacheControlWriter writer, CachePrepareReply value, bool includeMac)
    {
        const int CorrelationFieldId = 0;
        const int StatusFieldId = 1;
        const int ProofFieldId = 2;
        const int MacFieldId = 3;

        writer.String(CacheControlNames.PrepareReply);
        CacheControlFieldEncoding.NullableCorrelation(ref writer, CorrelationFieldId, value.Correlation);
        writer.ByteField(StatusFieldId, (byte)value.Status);
        CacheControlFieldEncoding.NullableProof(ref writer, ProofFieldId, value.Proof);
        if (includeMac)
        {
            writer.DigestField(MacFieldId, value.Mac);
        }
    }

    internal static void Grant(ref CacheControlWriter writer, CacheGrantReply value, bool includeMac)
    {
        const int CorrelationFieldId = 0;
        const int StatusFieldId = 1;
        const int GrantIdFieldId = 2;
        const int AcceptedBindingFieldId = 3;
        const int AcceptedSequenceFieldId = 4;
        const int MacFieldId = 5;

        writer.String(CacheControlNames.GrantReply);
        CacheControlFieldEncoding.NullableCorrelation(ref writer, CorrelationFieldId, value.Correlation);
        writer.ByteField(StatusFieldId, (byte)value.Status);
        writer.GuidField(GrantIdFieldId, value.GrantId);
        CacheControlFieldEncoding.NullableBinding(ref writer, AcceptedBindingFieldId, value.AcceptedBinding);
        writer.Int64Field(AcceptedSequenceFieldId, value.AcceptedSequence);
        if (includeMac)
        {
            writer.DigestField(MacFieldId, value.Mac);
        }
    }

    internal static void Revoke(ref CacheControlWriter writer, CacheRevokeReply value, bool includeMac)
    {
        const int CorrelationFieldId = 0;
        const int StatusFieldId = 1;
        const int GrantIdFieldId = 2;
        const int EffectFieldId = 3;
        const int MacFieldId = 4;

        writer.String(CacheControlNames.RevokeReply);
        CacheControlFieldEncoding.NullableCorrelation(ref writer, CorrelationFieldId, value.Correlation);
        writer.ByteField(StatusFieldId, (byte)value.Status);
        writer.GuidField(GrantIdFieldId, value.GrantId);
        writer.ByteField(EffectFieldId, (byte)value.Effect);
        if (includeMac)
        {
            writer.DigestField(MacFieldId, value.Mac);
        }
    }

    internal static void Refresh(ref CacheControlWriter writer, CacheRefreshReceipt value, bool includeMac)
    {
        const int CorrelationFieldId = 0;
        const int StatusFieldId = 1;
        const int ActualCoordinatorSessionIdFieldId = 2;
        const int CurrentRoundNonceFieldId = 3;
        const int ActualCoordinatorSiloAddressFieldId = 4;
        const int MacFieldId = 5;

        writer.String(CacheControlNames.RefreshReply);
        CacheControlFieldEncoding.NullableCorrelation(ref writer, CorrelationFieldId, value.Correlation);
        writer.ByteField(StatusFieldId, (byte)value.Status);
        writer.GuidField(ActualCoordinatorSessionIdFieldId, value.ActualCoordinatorSessionId);
        writer.GuidField(CurrentRoundNonceFieldId, value.CurrentRoundNonce);
        writer.NullableStringField(ActualCoordinatorSiloAddressFieldId, value.ActualCoordinatorSiloAddress);
        if (includeMac)
        {
            writer.DigestField(MacFieldId, value.Mac);
        }
    }
}
