namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlCoreEncoding
{
    internal static void Binding(ref CacheControlWriter writer, CachePhysicalBinding value)
    {
        const int SlotFieldId = 0;
        const int NodeIdFieldId = 1;
        const int IncarnationFieldId = 2;
        const int SiloAddressFieldId = 3;
        const int RuntimeIdFieldId = 4;
        const int RoleFieldId = 5;

        writer.String(CacheControlNames.PhysicalBinding);
        writer.ByteField(SlotFieldId, (byte)value.Slot);
        writer.GuidField(NodeIdFieldId, value.NodeId);
        writer.GuidField(IncarnationFieldId, value.Incarnation);
        writer.StringField(SiloAddressFieldId, value.SiloAddress);
        writer.GuidField(RuntimeIdFieldId, value.RuntimeId);
        writer.ByteField(RoleFieldId, (byte)value.Role);
    }

    internal static void Header(ref CacheControlWriter writer, CacheControlHeader value)
    {
        const int VersionFieldId = 0;
        const int OperationFieldId = 1;
        const int ScopeHashFieldId = 2;
        const int PolicyHashFieldId = 3;
        const int PolicyRevisionFieldId = 4;
        const int OriginSlotFieldId = 5;
        const int TargetSlotFieldId = 6;
        const int CoordinatorSessionIdFieldId = 7;
        const int RoundNonceFieldId = 8;
        const int RequestNonceFieldId = 9;
        const int SentUnixMillisecondsFieldId = 10;

        writer.String(CacheControlNames.Header);
        writer.ByteField(VersionFieldId, value.Version);
        writer.ByteField(OperationFieldId, (byte)value.Operation);
        writer.DigestField(ScopeHashFieldId, value.ScopeHash);
        writer.DigestField(PolicyHashFieldId, value.PolicyHash);
        writer.Int64Field(PolicyRevisionFieldId, value.PolicyRevision);
        writer.ByteField(OriginSlotFieldId, (byte)value.OriginSlot);
        writer.NullableSlotField(TargetSlotFieldId, value.TargetSlot);
        writer.GuidField(CoordinatorSessionIdFieldId, value.CoordinatorSessionId);
        writer.GuidField(RoundNonceFieldId, value.RoundNonce);
        writer.GuidField(RequestNonceFieldId, value.RequestNonce);
        writer.Int64Field(SentUnixMillisecondsFieldId, value.SentUnixMilliseconds);
    }

    internal static void Proof(ref CacheControlWriter writer, CacheReadyProof value, bool includeMac = true)
    {
        const int VersionFieldId = 0;
        const int ScopeHashFieldId = 1;
        const int PolicyHashFieldId = 2;
        const int PolicyRevisionFieldId = 3;
        const int OriginSlotFieldId = 4;
        const int CoordinatorSessionIdFieldId = 5;
        const int RoundNonceFieldId = 6;
        const int ChallengeIdFieldId = 7;
        const int ChallengeSequenceFieldId = 8;
        const int BindingFieldId = 9;
        const int StatusFieldId = 10;
        const int MacFieldId = 11;

        writer.String(CacheControlNames.ReadyProof);
        writer.ByteField(VersionFieldId, value.Version);
        writer.DigestField(ScopeHashFieldId, value.ScopeHash);
        writer.DigestField(PolicyHashFieldId, value.PolicyHash);
        writer.Int64Field(PolicyRevisionFieldId, value.PolicyRevision);
        writer.ByteField(OriginSlotFieldId, (byte)value.OriginSlot);
        writer.GuidField(CoordinatorSessionIdFieldId, value.CoordinatorSessionId);
        writer.GuidField(RoundNonceFieldId, value.RoundNonce);
        writer.GuidField(ChallengeIdFieldId, value.ChallengeId);
        writer.Int64Field(ChallengeSequenceFieldId, value.ChallengeSequence);
        CacheControlFieldEncoding.Binding(ref writer, BindingFieldId, value.Binding);
        writer.ByteField(StatusFieldId, (byte)value.Status);
        if (includeMac)
        {
            writer.DigestField(MacFieldId, value.Mac);
        }
    }

    internal static void Correlation(ref CacheControlWriter writer, CacheReplyCorrelation value)
    {
        const int HeaderFieldId = 0;
        const int SignedRequestDigestFieldId = 1;

        writer.String(CacheControlNames.ReplyCorrelation);
        CacheControlFieldEncoding.Header(ref writer, HeaderFieldId, value.Header);
        writer.DigestField(SignedRequestDigestFieldId, value.SignedRequestDigest);
    }

    internal static int BindingSize(CachePhysicalBinding value)
    {
        var writer = new CacheControlWriter(default, measureOnly: true);
        Binding(ref writer, value);
        return writer.Position;
    }

    internal static int HeaderSize(CacheControlHeader value)
    {
        var writer = new CacheControlWriter(default, measureOnly: true);
        Header(ref writer, value);
        return writer.Position;
    }

    internal static int ProofSize(CacheReadyProof value)
    {
        var writer = new CacheControlWriter(default, measureOnly: true);
        Proof(ref writer, value);
        return writer.Position;
    }

    internal static int CorrelationSize(CacheReplyCorrelation value)
    {
        var writer = new CacheControlWriter(default, measureOnly: true);
        Correlation(ref writer, value);
        return writer.Position;
    }
}
