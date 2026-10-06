using KeyLoad.AppHost.Features.CodeQuality;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal sealed class NativeCoverageImageFixture : IAsyncDisposable
{
    private const string SolutionFile = "KeyLoad.slnx";
    private const string TemporaryPrefix = "keyload-native-coverage-image-";
    private const string ServerOutputPath = "src/KeyLoad.Server/bin/Release/net10.0";
    internal const string CliOutputRelativePath = "src/KeyLoad.Cli/bin/Release/net10.0";
    private const string ServerDllName = "KeyLoad.Server.dll";
    private readonly string root;
    private bool disposalAttempted;

    private NativeCoverageImageFixture(string root, NativeCoverageImageTestOptions options,
        NativeCoverageToolPackage tool)
    {
        this.root = root;
        Options = options;
        Tool = tool;
        Inputs = Path.Combine(root, "inputs");
        Contexts = Path.Combine(root, "contexts");
        Directory.CreateDirectory(Inputs);
        Directory.CreateDirectory(Contexts);
    }

    internal NativeCoverageImageTestOptions Options { get; }
    internal NativeCoverageToolPackage Tool { get; }
    internal string Inputs { get; }
    internal string Contexts { get; }
    internal static string ServerOutput => Path.Combine(RepositoryRoot, ServerOutputPath);
    internal static string CliOutput => Path.Combine(RepositoryRoot, CliOutputRelativePath);
    internal static string RepositoryRoot { get; } = FindRepositoryRoot();

    internal static NativeCoverageImageFixture Create()
    {
        var options = NativeCoverageImageTestOptions.Capture();
        var tool = NativeCoverageToolPackage.Read();
        var temporary = Directory.CreateTempSubdirectory(TemporaryPrefix);
        var canonical = Resolve(temporary);
        return new(canonical, options, tool);
    }

    internal string CopyServerClosure(string name)
        => CopyClosure(ServerOutput, name);

    internal static string CopyCliClosure(string destinationRoot, string name,
        NativeCoverageExecutionOptions options)
        => NativeCoverageImageClosure.CopyToRoot(CliOutput, destinationRoot, name, options);

    internal string CopyClosure(string source, string name)
    {
        return NativeCoverageImageClosure.CopyToRoot(source, Inputs, name, Options.Coverage.Value);
    }

    internal void AlterServerAssembly(string directory)
    {
        var path = Path.Combine(directory, ServerDllName);
        if (new FileInfo(path).Length > Options.Coverage.Value.MaximumFileBytes)
        {
            throw new InvalidDataException("The copied Server assembly exceeds its configured file bound.");
        }
        var bytes = NativeCoverageImageOracleSupport.ReadBounded(path,
            Options.Coverage.Value.MaximumFileBytes);
        if (bytes.Length == 0)
        {
            throw new InvalidDataException("The Release Server assembly is empty.");
        }
        bytes[bytes.Length / 2] ^= 0x01;
        File.WriteAllBytes(path, bytes);
    }

    internal string ContextPath(string name) => Path.Combine(Contexts, name);

    public ValueTask DisposeAsync()
    {
        if (!disposalAttempted)
        {
            disposalAttempted = true;
            Directory.Delete(root, recursive: true);
        }
        return ValueTask.CompletedTask;
    }

    private static string Resolve(DirectoryInfo directory)
    {
        var parent = directory.Parent;
        if (parent is null)
        {
            return directory.FullName;
        }
        var candidate = Path.Combine(Resolve(parent), directory.Name);
        var entry = new DirectoryInfo(candidate);
        return entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? entry.FullName;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("KeyLoad.slnx was not found above the test assembly.");
    }
}

internal static class NativeCoverageImageClosure
{
    private const int InitialBufferOffset = 0;
    private const int NoBytesRead = 0;
    private const int EndOfFile = -1;
    private const string SourceShrankDuringCopyMessage =
        "A source native coverage file shrank during fixture creation.";
    private const string SourceGrewDuringCopyMessage =
        "A source native coverage file grew during fixture creation.";

    internal static string CopyToRoot(string source, string destinationRoot, string name,
        NativeCoverageExecutionOptions options)
    {
        var root = Path.GetFullPath(destinationRoot);
        var destination = Path.GetFullPath(Path.Combine(root, name));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var rootInfo = new DirectoryInfo(root);
        if (!rootInfo.Exists || IsReparsePoint(rootInfo) || root.Length > options.MaximumPathCharacters
            || destination.Length > options.MaximumPathCharacters
            || !destination.StartsWith(rootPrefix, StringComparison.Ordinal)
            || IsOccupiedOrLinked(destination))
        {
            throw new InvalidDataException("A native coverage deployment copy path is unsafe or already occupied.");
        }
        Copy(source, destination, options.MaximumFiles, options.MaximumFileBytes,
            options.MaximumTotalBytes, options.MaximumPathCharacters, options.ReadBufferBytes);
        return destination;
    }

    internal static void Copy(string source, string destination, int maximumFiles,
        int maximumFileBytes, long maximumTotalBytes, int maximumPathCharacters, int readBufferBytes)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Native coverage image permissions require a Unix filesystem.");
        }
        if (!Directory.Exists(source) || IsReparsePoint(new DirectoryInfo(source)))
        {
            throw new InvalidDataException("The source native coverage deployment closure is missing or linked.");
        }
        Directory.CreateDirectory(destination);
        long totalBytes = 0;
        foreach (var file in EnumerateFiles(source, maximumFiles, maximumPathCharacters))
        {
            var sourceInfo = new FileInfo(file);
            if (!sourceInfo.Exists || sourceInfo.Length < 0 || sourceInfo.Length > maximumFileBytes)
            {
                throw new InvalidDataException("A source native coverage file exceeds its admitted inventory or file bound.");
            }
            totalBytes = checked(totalBytes + sourceInfo.Length);
            if (totalBytes > maximumTotalBytes)
            {
                throw new InvalidDataException("The source native coverage closure exceeds its configured byte bound.");
            }
            CopyFile(source, destination, file, sourceInfo.Length, maximumPathCharacters, readBufferBytes);
        }
    }

    internal static IEnumerable<string> EnumerateFiles(string root, int maximumFiles,
        int maximumPathCharacters)
    {
        var fullRoot = Path.GetFullPath(root);
        if (fullRoot.Length > maximumPathCharacters || !Directory.Exists(fullRoot)
            || IsReparsePoint(new DirectoryInfo(fullRoot)))
        {
            throw new InvalidDataException("A native coverage closure root is missing, linked or too long.");
        }
        var pending = new Stack<string>();
        pending.Push(fullRoot);
        var count = 0;
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                var information = (attributes & FileAttributes.Directory) != 0
                    ? (FileSystemInfo)new DirectoryInfo(entry) : new FileInfo(entry);
                if (entry.Length > maximumPathCharacters || IsReparsePoint(information))
                {
                    throw new InvalidDataException("A native coverage closure contains an unsafe or overlong path.");
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }
                if ((attributes & FileAttributes.Device) != 0 || ++count > maximumFiles)
                {
                    throw new InvalidDataException("A native coverage closure contains an unsafe or excessive file inventory.");
                }
                yield return entry;
            }
        }
    }

    private static void CopyFile(string source, string destination, string file,
        long expectedLength, int maximumPathCharacters, int readBufferBytes)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Native coverage image permissions require a Unix filesystem.");
        }
        var relative = Path.GetRelativePath(source, file);
        var target = Path.Combine(destination, relative);
        var destinationRoot = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (target.Length > maximumPathCharacters || !Path.GetFullPath(target).StartsWith(
                destinationRoot, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A native coverage copy path escaped its bounded destination.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        CopyExactFile(file, target, expectedLength, readBufferBytes);
        var copied = new FileInfo(target);
        if (!copied.Exists || copied.Length != expectedLength || IsReparsePoint(copied))
        {
            throw new InvalidDataException("A copied native coverage file changed during fixture creation.");
        }
        File.SetUnixFileMode(target, File.GetUnixFileMode(file));
    }

    private static void CopyExactFile(string source, string destination, long expectedLength,
        int readBufferBytes)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            readBufferBytes, FileOptions.SequentialScan);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            readBufferBytes, FileOptions.SequentialScan);
        var buffer = new byte[readBufferBytes];
        var remaining = expectedLength;
        while (remaining > 0)
        {
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = input.Read(buffer, InitialBufferOffset, requested);
            if (read == NoBytesRead)
            {
                throw new InvalidDataException(SourceShrankDuringCopyMessage);
            }
            output.Write(buffer, InitialBufferOffset, read);
            remaining -= read;
        }
        if (input.ReadByte() != EndOfFile)
        {
            throw new InvalidDataException(SourceGrewDuringCopyMessage);
        }
    }

    private static bool IsReparsePoint(FileSystemInfo entry)
        => entry.LinkTarget is not null || (entry.Attributes & FileAttributes.ReparsePoint) != 0;

    private static bool IsOccupiedOrLinked(string path)
        => Directory.Exists(path) || File.Exists(path)
            || new DirectoryInfo(path).LinkTarget is not null || new FileInfo(path).LinkTarget is not null;
}
