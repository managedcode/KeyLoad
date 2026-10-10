using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextResourceRoot
{
    private const long EmptyDiskBytes = 0;
    private const int EmptyFileCount = 0;
    private const int EmptyGenerationCount = 0;
    internal static void Check(Dictionary<string, Action<ReadExecutionBudget?>> roots,
        HashSet<string> reservations, Dictionary<object, NativeTextResourceFileReservation> fileReservations, NativeTextOpenFileGroups openFiles, ReadExecutionBudget? budget, IOptions<NativeTextExecutionOptions> options)
    {
        var bytes = EmptyDiskBytes;
        var files = EmptyFileCount;
        var generations = EmptyGenerationCount;
        foreach (var (root, verify) in roots)
        {
            budget?.Check();
            verify(budget);
            var observed = NativeTextFileIO.MeasureRegularFiles(root, options.Value.MaximumFiles,
                options.Value.MaximumDiskBytes, options, budget);
            if (observed.Files > options.Value.MaximumFiles - files
                || observed.Bytes > options.Value.MaximumDiskBytes - bytes)
            { throw NativeTextErrors.BoundExceeded(); }
            files += observed.Files;
            bytes += observed.Bytes;
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                budget?.Check();
                if (NativeTextValidation.IsGenerationLeaf(Path.GetFileName(directory)))
                { generations++; }
            }
        }
        foreach (var reservation in reservations)
        {
            budget?.Check();
            if (!Directory.Exists(reservation))
            { generations++; }
        }
        NativeTextResourceFileAccounting.Check(fileReservations, ref files, ref bytes, options);
        openFiles.Check(fileReservations, ref files, ref bytes, options);
        if (generations > options.Value.MaximumGenerations)
        { throw NativeTextErrors.BoundExceeded(); }
    }
}
