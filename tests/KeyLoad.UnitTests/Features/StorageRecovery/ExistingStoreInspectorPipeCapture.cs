using System.Text;

using KeyLoad.CrashHost;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ExistingStoreInspectorPipeCapture
{
    private const int RetainedCharacterLimit = 8192;
    private const int ReadBufferLength = 512;
    private readonly StringBuilder retained = new(RetainedCharacterLimit);
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int readyMarkerPosition;

    internal const string ReadyMarker = ExistingStoreInspectorProtocol.ReadyMarker;
    internal string Text => retained.ToString();
    internal long CharacterCount { get; private set; }
    internal Task ReadyTask => ready.Task;

    internal async Task DrainAsync(TextReader reader)
    {
        var buffer = new char[ReadBufferLength];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory());
            if (count == 0)
            {
                return;
            }
            CharacterCount += count;
            Retain(buffer, count);
            FindReadyMarker(buffer, count);
        }
    }

    private void Retain(char[] buffer, int count)
    {
        var available = RetainedCharacterLimit - retained.Length;
        if (available > 0)
        {
            retained.Append(buffer, 0, Math.Min(count, available));
        }
    }

    private void FindReadyMarker(char[] buffer, int count)
    {
        if (ready.Task.IsCompleted)
        {
            return;
        }
        foreach (var character in buffer.AsSpan(0, count))
        {
            readyMarkerPosition = character == ReadyMarker[readyMarkerPosition]
                ? readyMarkerPosition + 1
                : character == ReadyMarker[0] ? 1 : 0;
            if (readyMarkerPosition == ReadyMarker.Length)
            {
                ready.TrySetResult();
                break;
            }
        }
    }
}
