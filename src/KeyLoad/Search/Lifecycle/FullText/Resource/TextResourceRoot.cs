using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextResourceRoot
{
    private const long EmptyDiskBytes = 0;
    private const int EmptyFileCount = 0;
    private const int EmptyGenerationCount = 0;
    internal static void Check(Dictionary<string, Action<ReadExecutionBudget?>> roots,
        HashSet<string> reservations, Dictionary<object, TextResourceFileReservation> /* KLD0041: One reservation ledger preserves string value equality for canonical paths and reference identity for TextOpenFileGroup instances; both keys are existing references. */ fileReservations, TextOpenFileGroups openFiles, ReadExecutionBudget? budget, IOptions<TextExecutionOptions> options)
    {
        var bytes = EmptyDiskBytes;
        var files = EmptyFileCount;
        var generations = EmptyGenerationCount;
        foreach (var (root, verify) in roots)
        {
            budget?.Check();
            verify(budget);
            var observed = TextFileIO.MeasureRegularFiles(root, options.Value.MaximumFiles,
                options.Value.MaximumDiskBytes, options, budget);
            if (observed.Files > options.Value.MaximumFiles - files
                || observed.Bytes > options.Value.MaximumDiskBytes - bytes)
            {
                throw TextErrors.BoundExceeded();
            }
            files += observed.Files;
            bytes += observed.Bytes;
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                budget?.Check();
                if (TextValidation.IsGenerationLeaf(Path.GetFileName(directory)))
                {
                    generations++;
                }
            }
        }
        foreach (var reservation in reservations)
        {
            budget?.Check();
            if (!Directory.Exists(reservation))
            {
                generations++;
            }
        }
        TextResourceFileAccounting.Check(fileReservations, ref files, ref bytes, options) /* KLD0041: Count path reservations from the existing mixed path/group ledger; open groups are accounted by their shared physical identity separately. */;
        openFiles.Check(fileReservations, ref files, ref bytes, options) /* KLD0041: Reconcile the same path/group ledger with open-file groups so hardlinked physical bytes are not counted twice. */;
        if (generations > options.Value.MaximumGenerations)
        {
            throw TextErrors.BoundExceeded();
        }
    }
}
