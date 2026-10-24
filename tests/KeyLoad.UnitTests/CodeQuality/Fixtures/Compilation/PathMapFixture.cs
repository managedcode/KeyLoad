using System.Text;
using KeyLoad.AppHost.TestInfrastructure;
using KeyLoad.UnitTests.CodeQuality.Processes;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.CodeQuality.Helpers;

internal sealed class PathMapFixture : IDisposable
{
    internal const string SourceFileName = "execution-source.cs";
    internal const string DllFileName = "execution-pathmap.dll";
    internal const string PdbFileName = "execution-pathmap.pdb";
    internal const string CanonicalMap = "/_/";
    internal const string UnknownMap = "/_unknown__pathmap/";
    internal const string EscapingMap = "/_/../escaped-execution-pathmap/";
    private const string DirectoryPrefix = "keyload-execution-pathmap-";
    private const string GuidFormat = "N";
    private const string SourceText = """
        public static class PathMapCompilerFixture
        {
            public static int AddOne(int value)
            {
                return value + 1;
            }
        }
        """;
    private const string SourceChange = "\n// Changed owned source after execution compilation.\n";

    internal IOptions<TestExecutionOptions> ExecutionOptions
    {
        get;
    } =
        ProductionSourceManifestProcess.CaptureExecutionOptions();
    internal string Root { get; } = CreateRootPath();
    internal string SourcePath => Path.Combine(Root, SourceFileName);
    internal string DllPath => Path.Combine(Root, DllFileName);
    internal string PdbPath => Path.Combine(Root, PdbFileName);
    internal static byte[] OriginalSourceBytes => Encoding.UTF8.GetBytes(SourceText);
    internal static byte[] ChangedSourceBytes => Encoding.UTF8.GetBytes(SourceText + SourceChange);

    internal PathMapFixture() => Directory.CreateDirectory(Root);

    public void Dispose() => Directory.Delete(Root, recursive: true);

    private static string CreateRootPath()
    {
        var temporary = Path.GetFullPath(AppContext.BaseDirectory);
        var current = Path.GetPathRoot(temporary)!;
        foreach (var segment in temporary.Substring(current.Length).Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(current, segment);
            current = new DirectoryInfo(candidate).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? candidate;
        }
        return Path.Combine(current, DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
    }
}
