using System.Text.Json.Serialization;
using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.StoredOutcome)]
internal sealed record StoredOutcome(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.Fingerprint)] string Fingerprint,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.PolicyEpoch)] long PolicyEpoch,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.Result)] OperationResult Result)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.BlobAuthority)]
    public BlobOutcomeAuthority? BlobAuthority
    {
        get; init;
    }

    [JsonIgnore]
    [global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.CompositionAuthority)]
    public global::KeyLoad.Core.Features.DatabaseComposition.CompositionOutcomeAuthority? CompositionAuthority
    {
        get; init;
    }

    [JsonIgnore]
    [global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.ScopeKind)]
    public global::KeyLoad.Core.Features.ClusterRouting.Contracts.CommandOutcomeScopeKind ScopeKind
    {
        get; init;
    }

    [JsonIgnore]
    [global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.Partition)]
    public PartitionRef? Partition
    {
        get; init;
    }

    [JsonIgnore]
    [global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.OnlineTextAuthority)]
    public global::KeyLoad.Core.Features.Search.OnlineTextOutcomeAuthority? OnlineTextAuthority
    {
        get; init;
    }

    [JsonIgnore]
    [global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.RemoteTransferFailureAuthority)]
    public global::KeyLoad.Core.Features.Messaging.RemoteTransferAcceptFailureAuthority? RemoteTransferFailureAuthority
    {
        get; init;
    }

    [JsonIgnore]
    [global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.RemoteTransferAuthority)]
    public global::KeyLoad.Core.Features.Messaging.RemoteTransferOutcomeAuthority? RemoteTransferAuthority
    {
        get; init;
    }
}
