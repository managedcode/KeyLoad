namespace KeyLoad.Core;

internal static class EventVectorIdentityInputs
{
    private const int EmptyTextLength = 0;
    internal static PartitionRef Partition(PartitionRef value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new PartitionRef(Text(value.TenantId), Text(value.DatabaseId),
            Text(value.TransactionDomainId), Text(value.PartitionKey));
    }

    internal static string Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length == EmptyTextLength ? string.Empty : new string(value.AsSpan());
    }

    internal static ReadOnlyMemory<byte> Digest(ReadOnlyMemory<byte> value) =>
        value.IsEmpty ? ReadOnlyMemory<byte>.Empty : value.ToArray();
}
