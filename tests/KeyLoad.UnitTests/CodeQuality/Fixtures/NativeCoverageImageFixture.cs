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

    internal NativeCoverageImageTestOptions Options
    {
        get;
    }
    internal NativeCoverageToolPackage Tool
    {
        get;
    }
    internal string Inputs
    {
        get;
    }
    internal string Contexts
    {
        get;
    }
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
