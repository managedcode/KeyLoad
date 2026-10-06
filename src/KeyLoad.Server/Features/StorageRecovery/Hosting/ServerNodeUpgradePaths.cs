namespace KeyLoad.Server;

internal sealed record ServerNodeUpgradePaths(string Source, string Destination, string Stage)
{
    internal static ServerNodeUpgradePaths Create(string source, string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var original = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        var stage = target + ServerNodeUpgradeProtocol.StageSuffix;
        if (original.Length > ServerNodeUpgradeProtocol.MaximumPathCharacters
            || stage.Length > ServerNodeUpgradeProtocol.MaximumPathCharacters
            || IsNested(original, target) || IsNested(target, original)
            || IsNested(original, stage) || IsNested(stage, original)
            || Path.GetDirectoryName(target) is not { } parent || !Directory.Exists(parent))
        { throw Errors.Fail(ErrorCode.Validation, ServerNodeUpgradeProtocol.Invalid); }
        CheckAncestors(original, false);
        CheckAncestors(target, true);
        CheckAncestors(stage, true);
        if (!Directory.Exists(original))
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
        return new(original, target, stage);
    }

    internal static void CheckAncestors(string path, bool allowMissing)
    {
        const int EmptyInfoAttributesFileAttributesReparsePoint = 0;

        var current = Path.GetFullPath(path);
        while (true)
        {
            var info = new FileInfo(current);
            if (info.LinkTarget is not null || info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != EmptyInfoAttributesFileAttributesReparsePoint)
            { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
            if (!allowMissing && !File.Exists(current) && !Directory.Exists(current))
            { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
            if (Path.GetDirectoryName(current) is not { } parent || parent == current)
            { return; }
            current = parent;
        }
    }

    private static bool IsNested(string parent, string candidate)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar;
        return string.Equals(parent, candidate, comparison) || candidate.StartsWith(prefix, comparison);
    }
}
