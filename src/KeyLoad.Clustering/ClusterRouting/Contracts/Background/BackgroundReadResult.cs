using ManagedCode.Communication;

namespace KeyLoad.Clustering;

/// <summary>Returns actual settled work and bounded native lifetime bytes with the original domain result.</summary>
[GenerateSerializer, Alias(BackgroundReadResult.SerializerAlias)]
public sealed record BackgroundReadResult([property: Id(0)] Result<object> Result,
    [property: Id(1)] long ReadBytes, [property: Id(2)] int ExaminedRecords,
    [property: Id(3)] ReadOnlyMemory<byte> Lifetimes)
{
    internal const string SerializerAlias = "keyload.orleans.background-read-result.v1";
}
