using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Messaging;

/// <summary>Owns one decoded body and its exact persisted native byte length.</summary>

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.StoredMessageBody)]
internal readonly record struct StoredMessageBody(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredMessageBodyFields.Body)] MessageBody Body,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredMessageBodyFields.Bytes)] long Bytes)
{
    private const int NoRetainedBytes = 0;

    /// <summary>Decodes borrowed bytes once without retaining the storage-owned span.</summary>
    internal static StoredMessageBody? Read(IKeyValueView view, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(key);
        MessageBody? body = null;
        long bytes = NoRetainedBytes;
        if (!view.ReadValue(key, borrowed =>
        {
            bytes = borrowed.Length;
            body = NativeSerialization.Deserialize<MessageBody>(borrowed);
        }) || body is null)
        {
            return null;
        }
        return new(body, bytes);
    }
}
