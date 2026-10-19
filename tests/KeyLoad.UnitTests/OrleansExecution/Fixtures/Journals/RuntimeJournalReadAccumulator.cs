#pragma warning disable ORLEANSEXP005
using Orleans.Journaling;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class RuntimeJournalReadAccumulator : IJournalStorageConsumer, IDisposable
{
    private readonly MemoryStream bytes = new();

    internal bool IsCompleted
    {
        get; private set;
    }
    internal IJournalMetadata? Metadata
    {
        get; private set;
    }
    internal byte[] ToArray() => bytes.ToArray();

    public void Read(JournalBufferReader buffer, IJournalMetadata? metadata)
    {
        if (buffer.Length > 0)
        {
            var segment = buffer.ToArray();
            bytes.Write(segment);
            buffer.Skip(segment.Length);
        }

        if (buffer.IsCompleted)
        {
            IsCompleted = true;
        }
        Metadata = metadata;
    }

    public void Dispose() => bytes.Dispose();
}
#pragma warning restore ORLEANSEXP005
