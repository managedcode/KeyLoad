using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class ClusterStartupTrace
{
    private readonly List<ClusterStartupEvent> events = [];
    private int continuationIndex = -1;
    internal IReadOnlyList<ClusterStartupEvent> Events => events;
    internal bool Saturated
    {
        get; private set;
    }

    internal ClusterStartupFile Read(string path, string role)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The execution startup trace cannot be a link.");
        }
        var failures = new List<Exception>();
        ClusterStartupFile? result = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                KeyLoad.Server.ServerFailureObserver.Observe(
                    () => result = ReadContents(stream, digest, role), failures);
            }
            catch (Exception digestFailure) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(digestFailure)) { failures.Add(digestFailure); }
            catch (Exception digestFailure) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(digestFailure)) { failures.Add(digestFailure); }
        }
        catch (Exception openOrCloseFailure) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(openOrCloseFailure)) { failures.Add(openOrCloseFailure); }
        catch (Exception openOrCloseFailure) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(openOrCloseFailure)) { failures.Add(openOrCloseFailure); }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new IOException("The execution startup trace did not settle.");
    }

    private ClusterStartupFile ReadContents(FileStream stream, IncrementalHash digest, string role)
    {
        var buffer = new byte[ClusterStartupProtocol.BufferBytes];
        var line = new byte[ClusterStartupProtocol.MaximumLineBytes];
        var consumed = 0L;
        var lineLength = 0;
        var lineOffset = 0L;
        int read;
        while (consumed < ClusterStartupProtocol.MaximumFileBytes
            && (read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length,
                ClusterStartupProtocol.MaximumFileBytes - consumed))) != 0)
        {
            digest.AppendData(buffer, 0, read);
            ConsumeBuffer(buffer.AsSpan(0, read), line, role, consumed, ref lineLength, ref lineOffset);
            consumed += read;
        }
        if (lineLength != 0)
        {
            Observe(Encoding.UTF8.GetString(line, 0, lineLength), role, lineOffset);
        }
        return new(role, consumed, stream.Length > consumed,
            Convert.ToHexString(digest.GetHashAndReset()));
    }

    private void ConsumeBuffer(ReadOnlySpan<byte> buffer, byte[] line, string role, long consumed,
        ref int lineLength, ref long lineOffset)
    {
        for (var index = 0; index < buffer.Length; index++)
        {
            if (buffer[index] != (byte)'\n')
            {
                if (lineLength < line.Length)
                {
                    line[lineLength++] = buffer[index];
                }
                continue;
            }
            Observe(Encoding.UTF8.GetString(line, 0, lineLength), role, lineOffset);
            lineLength = 0;
            lineOffset = consumed + index + 1;
        }
    }

    private void Observe(string line, string role, long offset)
    {
        var start = line.IndexOf('[', StringComparison.Ordinal);
        var end = line.IndexOf(']', StringComparison.Ordinal);
        if (start is 0 or 1)
        {
            continuationIndex = -1;
        }
        if (start >= 0 && start < end && start <= 1)
        {
            Header(line[(start + 1)..end], role, offset);
            return;
        }
        if (continuationIndex < 0)
        {
            return;
        }
        var current = events[continuationIndex];
        if (current.Role != role || current.Level is not ("Warning" or "Error" or "Critical"))
        {
            return;
        }
        var words = line.Split([' ', ':', '\t', ',', '(', ')'], StringSplitOptions.RemoveEmptyEntries);
        var type = words.FirstOrDefault(static word => word.EndsWith("Exception", StringComparison.Ordinal)
            && ClusterStartupProtocol.Identifier(word)
            && (word.StartsWith("System.", StringComparison.Ordinal)
                || word.StartsWith("Orleans.", StringComparison.Ordinal)
                || word.StartsWith("Microsoft.", StringComparison.Ordinal)));
        if (type is not null && current.ExceptionType is null)
        {
            events[continuationIndex] = current with
            {
                ExceptionType = type,
                Reason = (Reason(line)).Value
            };
        }
    }

    private void Header(string header, string role, long offset)
    {
        continuationIndex = -1;
        var fields = header.Split('\t');
        if (fields.Length != 4 || !ClusterStartupProtocol.Identifier(fields[2])
            || !ClusterStartupProtocol.Identifier(fields[3])
            || fields[1] is not ("Information" or "Warning" or "Error" or "Critical"))
        {
            return;
        }
        var category = fields[3];
        if (!category.StartsWith("Orleans.", StringComparison.Ordinal)
            && !category.StartsWith("Microsoft.Extensions.Hosting.", StringComparison.Ordinal))
        {
            return;
        }
        if (!AdmitHeader(fields[1]))
        {
            return;
        }
        events.Add(new(role, offset, fields[1], fields[2], category));
        continuationIndex = events.Count - 1;
    }

    private bool AdmitHeader(string level)
    {
        if (events.Count < ClusterStartupProtocol.MaximumEvents)
        {
            return true;
        }
        Saturated = true;
        if (level == "Information")
        {
            return false;
        }
        var informational = events.FindLastIndex(static item => item.Level == "Information");
        if (informational < 0)
        {
            return false;
        }
        events.RemoveAt(informational);
        return true;
    }

    private static (bool HasValue, string? Value) Reason(string line)
    {
        if (line.Contains("Address already in use", StringComparison.OrdinalIgnoreCase))
        {
            return (true, (string?)("AddressInUse"));
        }
        if (line.Contains("Connection refused", StringComparison.OrdinalIgnoreCase))
        {
            return (true, (string?)("ConnectionRefused"));
        }
        if (line.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return (true, (string?)("PermissionDenied"));
        }
        if (line.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            return (true, (string?)("TimedOut"));
        }
        if (line.Contains("canceled", StringComparison.OrdinalIgnoreCase))
        {
            return (true, (string?)("Cancelled"));
        }
        return (false, default(string?));
    }
}
