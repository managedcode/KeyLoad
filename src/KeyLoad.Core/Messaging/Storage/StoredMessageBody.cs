using KeyLoad.Storage;

namespace KeyLoad.Core.Messaging;

/// <summary>Owns one decoded body and its exact persisted execution byte length.</summary>

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.StoredMessageBody)]
internal readonly record struct StoredMessageBody(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.StoredMessageBodyFields.Body)] MessageBody Body,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.StoredMessageBodyFields.Bytes)] long Bytes)
{
    private const int NoRetainedBytes = 0;

    /// <summary>Decodes borrowed bytes once without retaining the storage-owned span.</summary>
    internal static global::ManagedCode.Communication.Result<StoredMessageBody?> Read(IKeyValueView view, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(key);
        MessageBody? body = null;
        long bytes = NoRetainedBytes;
        if (!view.ReadValue(key, borrowed =>
        {
            bytes = borrowed.Length;
            body = OrleansSerialization.Deserialize<MessageBody>(borrowed);
        }) || body is null)
        {
            return global::ManagedCode.Communication.Result<StoredMessageBody?>.FailNotFound(nameof(Read));
        }
        return global::ManagedCode.Communication.Result<StoredMessageBody?>.Succeed(((StoredMessageBody?)(new(body, bytes))));
    }
}
