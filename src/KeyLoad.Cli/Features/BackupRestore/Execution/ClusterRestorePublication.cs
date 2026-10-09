namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Publishes all joined restored nodes while retaining the actual original empty destination on failure.</summary>
internal static class ClusterRestorePublication
{
    private const string EmptySuffix = ".empty";
    private const string Changed = "The configured restore destination changed before publication.";

    internal static void Publish(string stage, string root, bool originallyEmpty)
    {
        ClusterRestorePathValidation.Root(root);
        var empty = stage + EmptySuffix;
        if (Directory.Exists(empty) || File.Exists(empty) || Directory.Exists(root) != originallyEmpty)
        { throw Errors.Fail(ErrorCode.Conflict, Changed); }
        var ownsEmpty = false;
        var published = false;
        try
        {
            if (originallyEmpty)
            {
                Directory.Move(root, empty);
                ownsEmpty = true;
            }
            Directory.Move(stage, root);
            published = true;
            if (ownsEmpty)
            { Directory.Delete(empty, recursive: false); }
        }
        catch (Exception primary)
        {
            if (ownsEmpty && !published)
            {
                RestoreEmpty(empty, root, primary);
            }
            throw;
        }
    }

    private static void RestoreEmpty(string empty, string root, Exception primary)
    {
        try
        {
            if (Directory.Exists(root) || File.Exists(root))
            { throw Errors.Fail(ErrorCode.Conflict, Changed); }
            Directory.Move(empty, root);
        }
        catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
    }
}
