namespace KeyLoad.Core.Features.Messaging;

/// <summary>Reports a rejected persisted discovery record without retaining its contents.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(DueRejectedRecord.SerializerAlias)]
public sealed record DueRejectedRecord([property: Orleans.Id(0)] DueWorkKind Kind,
    [property: Orleans.Id(1)] ErrorCode Code)
{
    internal const string SerializerAlias = "keyload.core.due-rejected-record.v1";
}
