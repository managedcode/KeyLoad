namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnFileBounds
{
    internal const int FilesPerGeneration = 2;
    internal const int Initial = 0;

    internal static long MeasureGenerations(string root, NativeAnnExecutionOptions options)
    {
        var count = Initial;
        long bytes = Initial;
        foreach (var path in Directory.EnumerateDirectories(root))
        {
            if (++count > options.MaximumOwnedGenerations)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound); }
            var leaf = Path.GetFileName(path);
            NativeAnnPaths.RequireClosedGeneration(root, leaf);
            foreach (var file in Directory.EnumerateFiles(path))
            {
                var length = new FileInfo(file).Length;
                if (length > options.MaximumDiskBytes - bytes)
                { throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound); }
                bytes += length;
            }
        }
        return bytes;
    }
}
