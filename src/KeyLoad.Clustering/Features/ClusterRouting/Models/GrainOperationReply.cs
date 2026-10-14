namespace KeyLoad.Clustering;

/// <summary>Bounded exact native response or typed safe rejection across the Orleans boundary.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(GrainRoutingProtocol.ReplyAlias)]
public sealed record GrainOperationReply
{
    /// <summary>Native GrainValue for a successful operation; empty for a failure.</summary>
    [Orleans.Id(0)]
    public ReadOnlyMemory<byte> Payload
    {
        get; init;
    }
    /// <summary>Public failure classification, without a serialized exception object.</summary>
    [Orleans.Id(1)]
    public ErrorCode? Error
    {
        get; init;
    }
    /// <summary>Safe caller-facing failure detail which contains no keys or private payloads.</summary>
    [Orleans.Id(2)]
    public string? SafeDetail
    {
        get; init;
    }
}
