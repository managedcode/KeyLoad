namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeFormatUpgradePathSafety
{
    private const int NoFileAttributes = 0;

    internal static FileAttributes? VerifyNoLinks(string path, bool allowMissingFinal)
    {
        var fullPath = Path.GetFullPath(path);
        var current = fullPath;
        var final = true;
        FileAttributes? finalAttributes = null;
        while (true)
        {
            var attributes = ReadAttributes(current, final, allowMissingFinal);
            if (attributes is null)
            {
                current = ParentPath(current);
                final = false;
                continue;
            }

            if ((attributes.Value & FileAttributes.ReparsePoint) != NoFileAttributes
                || !final && (attributes.Value & FileAttributes.Directory) == NoFileAttributes)
            {
                throw Errors.Fail(ErrorCode.FormatUnsupported, UpgradePathLinkOrAncestor);
            }
            if (final)
            {
                finalAttributes = attributes.Value;
            }

            var parent = Path.GetDirectoryName(current);
            if (parent is null || string.Equals(parent, current, StringComparison.Ordinal))
            {
                return finalAttributes;
            }
            current = parent;
            final = false;
        }
    }

    private static FileAttributes? ReadAttributes(string path, bool final, bool allowMissingFinal)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (FileNotFoundException) when (final && allowMissingFinal)
        {
            return null;
        }
        catch (DirectoryNotFoundException) when (final && allowMissingFinal)
        {
            return null;
        }
        catch (FileNotFoundException)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, UpgradePathLinkOrAncestor);
        }
        catch (DirectoryNotFoundException)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, UpgradePathLinkOrAncestor);
        }
    }

    private static string ParentPath(string path)
    {
        var parent = Path.GetDirectoryName(path);
        return parent is null ? path : parent;
    }

    private const string UpgradePathLinkOrAncestor = "Offline upgrade paths cannot contain links or non-directory ancestors.";
}
