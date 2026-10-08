namespace KeyLoad.CrashHost.Features.Search;

internal static class NativeTextIncrementalEvidenceFiles
{
    private const int MaximumEvidenceBytes = 1_048_576;
    private const long EmptyLength = 0;

    internal static async Task<T> ReadAsync<T>(string root, string file, CancellationToken token)
    {
        await using var input = new FileStream(Path.Combine(root, file), FileMode.Open, FileAccess.Read,
            FileShare.Read, CrashExecutionOptions.Child().Value.AuthorityReadBufferBytes, FileOptions.Asynchronous);
        if (input.Length is <= EmptyLength or > MaximumEvidenceBytes)
        { throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid); }
        var bytes = new byte[checked((int)input.Length)];
        await input.ReadExactlyAsync(bytes, token);
        return NativeSerialization.Deserialize<T>(bytes);
    }

    internal static async Task WriteAsync<T>(string root, string file, T value, CancellationToken token)
    {
        if (NativeSerialization.Measure(value) is <= EmptyLength or > MaximumEvidenceBytes)
        { throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid); }
        var bytes = NativeSerialization.Serialize(value);
        await File.WriteAllBytesAsync(Path.Combine(root, file), bytes, token);
    }
}
