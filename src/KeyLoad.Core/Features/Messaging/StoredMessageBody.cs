using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Messaging;

/// <summary>Owns one decoded body and its exact persisted UTF-8 length.</summary>
internal readonly record struct StoredMessageBody(MessageBody Body, long Bytes)
{
    /// <summary>Decodes borrowed bytes once without retaining the storage-owned span.</summary>
    internal static StoredMessageBody? Read(IKeyValueView view, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(key);
        MessageBody? body = null;
        long bytes = 0;
        if (!view.ReadValue(key, borrowed =>
        {
            bytes = borrowed.Length;
            body = JsonDefaults.Deserialize<MessageBody>(borrowed);
        }) || body is null)
        {
            return null;
        }
        return new(body, bytes);
    }
}
