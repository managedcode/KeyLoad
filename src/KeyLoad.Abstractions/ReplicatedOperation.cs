using System.Text.Json.Serialization;

namespace KeyLoad;

/// <summary>Carries a trusted operation and its evaluated principal and time.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Kind">Identifies the mutation or trusted operation kind.</param>
/// <param name="PrincipalId">Identifies the principal receiving the delivery.</param>
/// <param name="EvaluatedAt">Records when authorization evaluated the operation.</param>
/// <param name="PayloadJson">Contains the optional message payload.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReplicatedOperation)]
public sealed record ReplicatedOperation([property: Orleans.Id(0)] Guid Id, [property: Orleans.Id(1)] OperationKind Kind, [property: Orleans.Id(2)] string PrincipalId, [property: Orleans.Id(3)] DateTimeOffset EvaluatedAt, [property: Orleans.Id(4)] string PayloadJson)
{
    /// <summary>Gets the native internal operation body after authorized public-ingress normalization.</summary>
    [Orleans.Id(5)]
    [JsonIgnore]
    public ReadOnlyMemory<byte> NativePayload
    {
        get; init;
    }
}
