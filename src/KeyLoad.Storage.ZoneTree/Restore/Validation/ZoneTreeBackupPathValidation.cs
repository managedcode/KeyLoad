namespace KeyLoad.Storage.ZoneTree;

/// <summary>Rejects files and symbolic links in the owned execution backup destination path.</summary>
internal static class ZoneTreeBackupPathValidation
{
    private const int NoFileAttributes = 0;
    private const string InvalidTarget = "The execution backup destination is not a regular directory path.";

    internal static void Require(string target)
    {
        var ancestor = new DirectoryInfo(Path.GetFullPath(target));
        while (ancestor is not null)
        {
            if (File.Exists(ancestor.FullName) || ancestor.Exists
                && (ancestor.Attributes & FileAttributes.ReparsePoint) != NoFileAttributes)
            {
                throw Errors.Fail(ErrorCode.Conflict, InvalidTarget);
            }
            ancestor = ancestor.Parent;
        }
    }
}
