using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal sealed record NativeCoverageImageExpectedFile(string RelativePath, long Length, int Mode, string Sha256);
internal sealed record NativeCoverageImageExpected(IReadOnlyDictionary<string, NativeCoverageImageExpectedFile> Files,
    string ToolClosureDigest);

internal static class NativeCoverageImageExpectedFiles
{
    private const string ScriptRoot = "scripts/Features/CodeQuality";
    private const string TemplateName = "functional-coverage.server-image.dockerfile";
    private const string Placeholder = "{{SOURCE_BOUND_ASPNET_IMAGE}}";
    private const int ExecutableBits = 0x49;
    private const int ModeMask = 0x1ff;
    private const string RuntimeIdentityPrefix = "KEYLOAD_NATIVE_COVERAGE_";

    internal static NativeCoverageImageExpected Create(NativeCoverageImageFixture fixture,
        NativeCoverageImageInvocation invocation)
    {
        var files = new Dictionary<string, NativeCoverageImageExpectedFile>(StringComparer.Ordinal);
        AddServerFiles(files, fixture, invocation);
        var toolDigest = NativeCoverageImagePackageOracle.AddFiles(files, fixture);
        AddTemplates(files, fixture, invocation);
        AddReceipts(files, fixture, invocation);
        AddRuntimeIdentity(files, fixture, invocation, toolDigest);
        return new(files, toolDigest);
    }

    internal static NativeCoverageImageExpectedFile FromPath(string relativePath, string sourcePath,
        int maximumBytes, int outputModeAdjustment = 0)
    {
        var info = new FileInfo(sourcePath);
        if (!info.Exists || info.Length < 0 || info.Length > maximumBytes
            || (File.GetAttributes(sourcePath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("An independent expected image input exceeds its configured bound.");
        }
        using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var digest = Convert.ToHexStringLower(SHA256.HashData(stream));
        return new(relativePath, info.Length, Mode(sourcePath) | outputModeAdjustment, digest);
    }

    internal static NativeCoverageImageExpectedFile FromBytes(string relativePath, byte[] bytes, int mode)
        => new(relativePath, bytes.LongLength, mode & ModeMask,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));

    internal static byte[] ReadSourceBytes(string sourcePath, int maximumBytes)
        => NativeCoverageImageOracleSupport.ReadBounded(sourcePath, maximumBytes);

    private static void AddServerFiles(Dictionary<string, NativeCoverageImageExpectedFile> files,
        NativeCoverageImageFixture fixture, NativeCoverageImageInvocation invocation)
    {
        var bounds = fixture.Options.Coverage.Value;
        var count = 0;
        long totalBytes = 0;
        foreach (var path in Directory.EnumerateFiles(invocation.ServerDirectory, "*", SearchOption.AllDirectories))
        {
            if (++count > bounds.MaximumFiles || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("The observed Server closure exceeds its admitted file inventory.");
            }
            var relative = Path.GetRelativePath(invocation.ServerDirectory, path).Replace(Path.DirectorySeparatorChar, '/');
            var entry = FromPath("server/" + relative, path, bounds.MaximumFileBytes);
            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > bounds.MaximumTotalBytes)
            {
                throw new InvalidDataException("The observed Server closure exceeds its admitted byte bound.");
            }
            Add(files, entry);
        }
    }

    private static void AddTemplates(Dictionary<string, NativeCoverageImageExpectedFile> files,
        NativeCoverageImageFixture fixture, NativeCoverageImageInvocation invocation)
    {
        var root = Path.Combine(NativeCoverageImageFixture.RepositoryRoot, ScriptRoot);
        AddTemplate(files, NativeCoverageImageConstants.Settings, "functional-coverage.server.settings.xml", root, fixture, 0);
        AddTemplate(files, NativeCoverageImageConstants.Wrapper, "functional-coverage.server-wrapper.sh", root, fixture, ExecutableBits);
        AddTemplate(files, NativeCoverageImageConstants.Lifecycle, "functional-coverage.server-lifecycle.sh", root, fixture, 0);
        AddTemplate(files, NativeCoverageImageConstants.Target, "functional-coverage.server-target.sh", root, fixture, ExecutableBits);
        AddTemplate(files, ".dockerignore", ".dockerignore", root, fixture, 0);
        var dockerPath = Path.Combine(root, TemplateName);
        var source = Encoding.UTF8.GetString(ReadSourceBytes(dockerPath, fixture.Options.Coverage.Value.MaximumManifestBytes));
        if (source.Split(Placeholder, StringSplitOptions.None).Length != 2)
        {
            throw new InvalidDataException("The source Dockerfile template does not contain exactly one pin placeholder.");
        }
        var rendered = Encoding.UTF8.GetBytes(source.Replace(Placeholder, invocation.BaseReference, StringComparison.Ordinal));
        Add(files, FromBytes(NativeCoverageImageConstants.Dockerfile, rendered, Mode(dockerPath)));
    }

    private static void AddTemplate(Dictionary<string, NativeCoverageImageExpectedFile> files,
        string destination, string fileName, string root, NativeCoverageImageFixture fixture, int modeAdjustment)
    {
        var path = Path.Combine(root, fileName);
        Add(files, FromPath(destination, path, fixture.Options.Coverage.Value.MaximumManifestBytes, modeAdjustment));
    }

    private static void AddReceipts(Dictionary<string, NativeCoverageImageExpectedFile> files,
        NativeCoverageImageFixture fixture, NativeCoverageImageInvocation invocation)
    {
        var maximum = fixture.Options.Coverage.Value.MaximumManifestBytes;
        Add(files, FromPath(NativeCoverageImageConstants.ServerReceipt, invocation.SourceReceiptPath, maximum));
        Add(files, FromPath(NativeCoverageImageConstants.BaseReceipt, invocation.BaseReceiptPath, maximum));
    }

    private static void AddRuntimeIdentity(Dictionary<string, NativeCoverageImageExpectedFile> files,
        NativeCoverageImageFixture fixture, NativeCoverageImageInvocation invocation, string toolDigest)
    {
        var options = fixture.Options.Coverage.Value;
        var settings = Path.Combine(NativeCoverageImageFixture.RepositoryRoot, ScriptRoot,
            "functional-coverage.server.settings.xml");
        var values = new[]
        {
            ("SERVER_DLL_SHA256", invocation.ServerDllSha256),
            ("SERVER_PDB_SHA256", invocation.ServerPdbSha256),
            ("SERVER_MVID", invocation.Mvid),
            ("SOURCE_RECEIPT_SHA256", HashFile(invocation.SourceReceiptPath, options.MaximumManifestBytes)),
            ("TOOL_VERSION", fixture.Tool.Version),
            ("TOOL_CLOSURE_DIGEST", toolDigest),
            ("SETTINGS_SHA256", HashFile(settings, options.MaximumManifestBytes)),
            ("SHUTDOWN_SECONDS", Seconds(options.ShutdownTimeout)),
            ("SETTLEMENT_SECONDS", Seconds(options.SettlementTimeout)),
            ("MAX_REPORT_BYTES", options.MaximumReportBytes.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        var content = string.Concat(values.Select(item => $"{RuntimeIdentityPrefix}{item.Item1}={item.Item2}\n"));
        Add(files, FromBytes(NativeCoverageImageConstants.RuntimeIdentity, Encoding.UTF8.GetBytes(content), 0x1a4));
    }

    private static void Add(Dictionary<string, NativeCoverageImageExpectedFile> files,
        NativeCoverageImageExpectedFile file)
    {
        if (!files.TryAdd(file.RelativePath, file))
        {
            throw new InvalidDataException("The independent image inventory contains a duplicate path.");
        }
    }

    private static string Seconds(TimeSpan duration)
        => checked((int)duration.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string HashFile(string path, int maximumBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 0 || info.Length > maximumBytes)
        {
            throw new InvalidDataException("An expected template exceeds its configured file bound.");
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static int Mode(string path) => NativeCoverageImageOracleSupport.Mode(path);
}
