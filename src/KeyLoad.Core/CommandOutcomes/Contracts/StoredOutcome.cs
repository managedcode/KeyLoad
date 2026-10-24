using System.Text.Json.Serialization;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.StoredOutcome)]
internal sealed record StoredOutcome(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.StoredOutcomeFields.Fingerprint)] string Fingerprint,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.StoredOutcomeFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.StoredOutcomeFields.PolicyEpoch)] long PolicyEpoch,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.StoredOutcomeFields.Result)] Result<object> Result,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.StoredOutcomeFields.PreparedCommand)] Command<DatabaseCommand> PreparedCommand)
{
    [JsonIgnore]
    [global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.StoredOutcomeFields.CompositionAuthority)]
    public global::KeyLoad.Core.DatabaseComposition.CompositionOutcomeAuthority? CompositionAuthority
    {
        get; init;
    }

    [JsonIgnore]
    [global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.StoredOutcomeFields.ScopeKind)]
    public global::KeyLoad.Core.CommandOutcomes.Contracts.CommandOutcomeScopeKind ScopeKind
    {
        get; init;
    }

    [JsonIgnore]
    [global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.StoredOutcomeFields.Partition)]
    public PartitionRef? Partition
    {
        get; init;
    }

    [JsonIgnore]
    [global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.StoredOutcomeFields.OnlineTextAuthority)]
    public global::KeyLoad.Core.Search.OnlineTextOutcomeAuthority? OnlineTextAuthority
    {
        get; init;
    }

    [JsonIgnore]
    [global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.StoredOutcomeFields.RemoteTransferFailureAuthority)]
    public global::KeyLoad.Core.Messaging.RemoteTransferAcceptFailureAuthority? RemoteTransferFailureAuthority
    {
        get; init;
    }

    [JsonIgnore]
    [global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.StoredOutcomeFields.CanonicalSequence)]
    public long CanonicalSequence
    {
        get; init;
    }

}
