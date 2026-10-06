using System.Text;

namespace KeyLoad.Comparisons;

internal static class OpenLoopCancellationRequestWatcher
{

    internal static void ValidateFresh(string output)
    {
        RejectExisting(Path.Combine(output, OpenLoopCancellationProofContract.RequestFileName));
        RejectExisting(Path.Combine(output, OpenLoopCancellationProofContract.PendingRequestFileName));
        RejectExisting(Path.Combine(output, OpenLoopCancellationProofContract.ProofFileName));
    }

    internal static async Task<bool> WatchAndCancelAsync(string output,
        CancellationTokenSource runnerCancellation, OpenLoopExecutionPolicy policy,
        CancellationToken stopToken)
    {
        var request = Path.Combine(output, OpenLoopCancellationProofContract.RequestFileName);
        try
        {
            while (true)
            {
                stopToken.ThrowIfCancellationRequested();
                if (TryGetAttributes(request, out var attributes))
                {
                    ValidateRequestAttributes(request, attributes);
                    await ValidateRequestAsync(request, stopToken).ConfigureAwait(false);
                    await runnerCancellation.CancelAsync().ConfigureAwait(false);
                    return true;
                }
                await Task.Delay(policy.ControlPollMilliseconds, stopToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException error) when (error.CancellationToken == stopToken
            && stopToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception failure)
        {
            var cancellationFailure = await OpenLoopOwnerCancellation.CancelAsync(runnerCancellation)
                .ConfigureAwait(false);
            if (cancellationFailure is not null)
            {
                throw OpenLoopFailure.Combine(failure, [cancellationFailure])!;
            }
            throw;
        }
    }

    private static void RejectExisting(string path)
    {
        if (TryGetAttributes(path, out _))
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationControlAlreadyExists);
        }
    }

    private static void ValidateRequestAttributes(string path, FileAttributes attributes)
    {
        const int NoObservedItems = 0;

        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != NoObservedItems
            || new FileInfo(path).LinkTarget is not null)
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationControlInvalid);
        }
    }

    private static async Task ValidateRequestAsync(string path, CancellationToken cancellationToken)
    {
        const int NoObservedItems = 0;
        const int FirstElementIndex = 0;

        var info = new FileInfo(path);
        if (info.Length != OpenLoopCancellationProofContract.RequestBytes)
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationControlInvalid);
        }
        var bytes = new byte[OpenLoopCancellationProofContract.RequestReadBufferBytes];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            OpenLoopCancellationProofContract.RequestReadBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var read = NoObservedItems;
        while (read < bytes.Length)
        {
            var count = await stream.ReadAsync(bytes.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (count == NoObservedItems)
            {
                break;
            }
            read += count;
        }
        var expected = Encoding.UTF8.GetBytes(OpenLoopCancellationProofContract.RequestText);
        if (read != OpenLoopCancellationProofContract.RequestBytes || !bytes.AsSpan(FirstElementIndex, OpenLoopCancellationProofContract.RequestBytes).SequenceEqual(expected))
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationControlInvalid);
        }
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        if (new FileInfo(path).LinkTarget is not null)
        {
            attributes = FileAttributes.ReparsePoint;
            return true;
        }
        attributes = default;
        return false;
    }
}
