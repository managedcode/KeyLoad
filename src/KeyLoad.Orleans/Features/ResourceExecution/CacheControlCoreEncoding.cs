namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlCoreEncoding
{
    internal static void Binding(ref CacheControlWriter writer, CachePhysicalBinding value)
    {
        writer.String(CacheControlNames.PhysicalBinding);
        writer.ByteField(0, (byte)value.Slot);
        writer.GuidField(1, value.NodeId);
        writer.GuidField(2, value.Incarnation);
        writer.StringField(3, value.SiloAddress);
        writer.GuidField(4, value.RuntimeId);
        writer.ByteField(5, (byte)value.Role);
    }

    internal static void Header(ref CacheControlWriter writer, CacheControlHeader value)
    {
        writer.String(CacheControlNames.Header);
        writer.ByteField(0, value.Version);
        writer.ByteField(1, (byte)value.Operation);
        writer.DigestField(2, value.ScopeHash);
        writer.DigestField(3, value.PolicyHash);
        writer.Int64Field(4, value.PolicyRevision);
        writer.ByteField(5, (byte)value.OriginSlot);
        writer.NullableSlotField(6, value.TargetSlot);
        writer.GuidField(7, value.CoordinatorSessionId);
        writer.GuidField(8, value.RoundNonce);
        writer.GuidField(9, value.RequestNonce);
        writer.Int64Field(10, value.SentUnixMilliseconds);
    }

    internal static void Proof(ref CacheControlWriter writer, CacheReadyProof value, bool includeMac = true)
    {
        writer.String(CacheControlNames.ReadyProof);
        writer.ByteField(0, value.Version);
        writer.DigestField(1, value.ScopeHash);
        writer.DigestField(2, value.PolicyHash);
        writer.Int64Field(3, value.PolicyRevision);
        writer.ByteField(4, (byte)value.OriginSlot);
        writer.GuidField(5, value.CoordinatorSessionId);
        writer.GuidField(6, value.RoundNonce);
        writer.GuidField(7, value.ChallengeId);
        writer.Int64Field(8, value.ChallengeSequence);
        CacheControlFieldEncoding.Binding(ref writer, 9, value.Binding);
        writer.ByteField(10, (byte)value.Status);
        if (includeMac)
        {
            writer.DigestField(11, value.Mac);
        }
    }

    internal static void Correlation(ref CacheControlWriter writer, CacheReplyCorrelation value)
    {
        writer.String(CacheControlNames.ReplyCorrelation);
        CacheControlFieldEncoding.Header(ref writer, 0, value.Header);
        writer.DigestField(1, value.SignedRequestDigest);
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
