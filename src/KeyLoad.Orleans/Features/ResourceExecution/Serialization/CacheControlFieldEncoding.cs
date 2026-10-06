namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlFieldEncoding
{
    internal static void Header(ref CacheControlWriter writer, ushort id, CacheControlHeader value)
    {
        writer.Id(id);
        writer.UInt32(checked((uint)CacheControlCoreEncoding.HeaderSize(value)));
        CacheControlCoreEncoding.Header(ref writer, value);
    }

    internal static void Binding(ref CacheControlWriter writer, ushort id, CachePhysicalBinding value)
    {
        writer.Id(id);
        writer.UInt32(checked((uint)CacheControlCoreEncoding.BindingSize(value)));
        CacheControlCoreEncoding.Binding(ref writer, value);
    }

    internal static void Proof(ref CacheControlWriter writer, ushort id, CacheReadyProof value)
    {
        writer.Id(id);
        writer.UInt32(checked((uint)CacheControlCoreEncoding.ProofSize(value)));
        CacheControlCoreEncoding.Proof(ref writer, value);
    }

    internal static void NullableBinding(ref CacheControlWriter writer, ushort id, CachePhysicalBinding? value)
    {
        const int NullableBindingEmptyCount = 0;
        const int NullableBindingSingleItemCount = 1;

        writer.Id(id);
        writer.Byte(value is null ? (byte)NullableBindingEmptyCount : (byte)NullableBindingSingleItemCount);
        if (value is not null)
        {
            writer.UInt32(checked((uint)CacheControlCoreEncoding.BindingSize(value)));
            CacheControlCoreEncoding.Binding(ref writer, value);
        }
    }

    internal static void NullableProof(ref CacheControlWriter writer, ushort id, CacheReadyProof? value)
    {
        const int NullableProofEmptyCount = 0;
        const int NullableProofSingleItemCount = 1;

        writer.Id(id);
        writer.Byte(value is null ? (byte)NullableProofEmptyCount : (byte)NullableProofSingleItemCount);
        if (value is not null)
        {
            writer.UInt32(checked((uint)CacheControlCoreEncoding.ProofSize(value)));
            CacheControlCoreEncoding.Proof(ref writer, value);
        }
    }

    internal static void NullableCorrelation(ref CacheControlWriter writer, ushort id, CacheReplyCorrelation? value)
    {
        const int NullableCorrelationEmptyCount = 0;
        const int NullableCorrelationSingleItemCount = 1;

        writer.Id(id);
        writer.Byte(value is null ? (byte)NullableCorrelationEmptyCount : (byte)NullableCorrelationSingleItemCount);
        if (value is not null)
        {
            writer.UInt32(checked((uint)CacheControlCoreEncoding.CorrelationSize(value)));
            CacheControlCoreEncoding.Correlation(ref writer, value);
        }
    }
}
