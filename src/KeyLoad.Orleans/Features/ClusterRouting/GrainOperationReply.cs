namespace KeyLoad.Orleans;

/// <summary>Bounded exact JSON response or typed safe rejection across the Orleans boundary.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(GrainRoutingProtocol.ReplyAlias)]
public sealed record GrainOperationReply
{
    /// <summary>UTF8 JSON for a successful operation; empty for a failure.</summary>
    [global::Orleans.Id(0)] public ReadOnlyMemory<byte> Payload { get; init; }
    /// <summary>Public failure classification, without a serialized exception object.</summary>
    [global::Orleans.Id(1)] public ErrorCode? Error { get; init; }
    /// <summary>Safe caller-facing failure detail which contains no keys or private payloads.</summary>
    [global::Orleans.Id(2)] public string? SafeDetail { get; init; }
}
