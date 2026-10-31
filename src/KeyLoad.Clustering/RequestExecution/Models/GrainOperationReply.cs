namespace KeyLoad.Clustering;

/// <summary>Bounded exact execution response or typed safe rejection across the Orleans boundary.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(GrainRoutingProtocol.ReplyAlias)]
public sealed record GrainOperationReply
{
    /// <summary>Generated Orleans value for a successful operation; absent for a failure.</summary>
    [Orleans.Id(4)]
    public GrainValue? Result
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
    /// <summary>Authenticated execution command dependencies; canonical results and public JSON exclude this proof.</summary>
    [Orleans.Id(3), System.Text.Json.Serialization.JsonIgnore]
    internal CommandWorkProof? WorkProof
    {
        get; init;
    }
}
