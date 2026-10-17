using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost.Features.ClusterRouting.Processes;

internal sealed class C1OutcomeInspectionCapture
{
    private const int GetBytesStartEmptyCount = 0;
    private const int NoOutputBytes = 0;

    private readonly CrashHostExecutionOptions settings;
    private readonly int maximumOutputBytes;
    private readonly byte[] retained;
    private readonly byte[] buffer;

    internal C1OutcomeInspectionCapture(IOptions<CrashHostExecutionOptions> executionOptions, int? maximumOutputBytes = null)
    {
        settings = executionOptions.Value;
        this.maximumOutputBytes = maximumOutputBytes ?? settings.InspectionMaximumOutputBytes;
        if (this.maximumOutputBytes is <= NoOutputBytes or > C1SubmissionOutcomeInspectionProtocol.MaximumReceiptBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumOutputBytes));
        }
        retained = new byte[this.maximumOutputBytes];
        buffer = new byte[settings.InspectionReadBufferBytes];
    }
    private long observedBytes;

    internal bool Exceeded => observedBytes > maximumOutputBytes;
    internal byte[] Bytes => retained.AsSpan(GetBytesStartEmptyCount, checked((int)Math.Min(observedBytes, maximumOutputBytes))).ToArray();

    internal async Task DrainAsync(Stream stream)
    {
        const int EmptyRead = 0;
        const int StartEmptyCount = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (read == EmptyRead)
            {
                return;
            }
            var offset = checked((int)Math.Min(observedBytes, maximumOutputBytes));
            var copy = Math.Min(maximumOutputBytes - offset, read);
            buffer.AsSpan(StartEmptyCount, copy).CopyTo(retained.AsSpan(offset));
            observedBytes = checked(observedBytes + read);
        }
    }
}
