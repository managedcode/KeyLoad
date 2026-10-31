using System.Collections.Immutable;
using ManagedCode.Communication;

namespace KeyLoad.Clustering;

/// <summary>Returns the observed resource lifetimes with the original domain result.</summary>
[GenerateSerializer, Alias(BackgroundReadResult.SerializerAlias)]
public sealed record BackgroundReadResult
{
    internal const string SerializerAlias = "keyload.orleans.background-read-result";

    /// <summary>The original domain result.</summary>
    [Id(0)]
    public required Result<object> Result
    {
        get; init;
    }

    [Id(3)]
    internal ImmutableArray<ResourceLifetime> Lifetimes
    {
        get; init;
    }
}
