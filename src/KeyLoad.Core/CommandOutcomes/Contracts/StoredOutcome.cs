using System.Text.Json.Serialization;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreAliases.StoredOutcome)]
internal sealed record StoredOutcome(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.Fingerprint)] string Fingerprint,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.PolicyEpoch)] long PolicyEpoch,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.Result)] Result<object> Result,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.PreparedCommand)] Command<DatabaseCommand> PreparedCommand)
{
    [JsonIgnore]
    [global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.CompositionAuthority)]
    public global::KeyLoad.Core.Features.DatabaseComposition.CompositionOutcomeAuthority? CompositionAuthority
    {
        get; init;
    }

    [JsonIgnore]
    [global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.ScopeKind)]
    public global::KeyLoad.Core.Features.CommandOutcomes.Contracts.CommandOutcomeScopeKind ScopeKind
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
    [global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutcomeFields.CanonicalSequence)]
    public long CanonicalSequence
    {
        get; init;
    }

}
