using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageImageInventoryAssertions
{
    internal static void Verify(JsonElement files,
        IReadOnlyDictionary<string, NativeCoverageImageExpectedFile> expected, string contextPath,
        JsonElement result, byte[] manifestBytes, NativeCoverageExecutionOptions options)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files.EnumerateArray())
        {
            VerifyOne(file, expected, contextPath, paths, options.MaximumPathCharacters);
        }
        NativeCoverageImageOracleSupport.Ensure(paths.SetEquals(expected.Keys),
            "The context manifest omitted an admitted file.");
        var actualFileCount = Directory.EnumerateFiles(contextPath, "*", SearchOption.AllDirectories)
            .Take(options.MaximumFiles + 1).Count();
        NativeCoverageImageOracleSupport.Ensure(actualFileCount == expected.Count + 1
            && Path.Combine(contextPath, NativeCoverageImageConstants.ContextManifest).Length <= options.MaximumPathCharacters
            && File.Exists(Path.Combine(contextPath, NativeCoverageImageConstants.ContextManifest)),
            "The create-only output context contains an unexpected or missing file.");
        VerifyCounts(result, files, manifestBytes, expected, options);
    }

    internal static void VerifySourceReceipts(NativeCoverageImageInvocation invocation, int maximumBytes)
    {
        var serverBytes = NativeCoverageImageOracleSupport.ReadBounded(invocation.SourceReceiptPath, maximumBytes);
        var baseBytes = NativeCoverageImageOracleSupport.ReadBounded(invocation.BaseReceiptPath, maximumBytes);
        var outputServer = NativeCoverageImageOracleSupport.ReadBounded(Path.Combine(invocation.ContextPath,
            NativeCoverageImageConstants.ServerReceipt), maximumBytes);
        var outputBase = NativeCoverageImageOracleSupport.ReadBounded(Path.Combine(invocation.ContextPath,
            NativeCoverageImageConstants.BaseReceipt), maximumBytes);
        NativeCoverageImageOracleSupport.Ensure(outputServer.SequenceEqual(serverBytes)
            && outputBase.SequenceEqual(baseBytes), "The context did not preserve its original local receipts.");
        using var server = JsonDocument.Parse(serverBytes);
        var sourceDll = RelativeSource(invocation.ServerDirectory, NativeCoverageImageConstants.ServerDll);
        var sourcePdb = RelativeSource(invocation.ServerDirectory, NativeCoverageImageConstants.ServerPdb);
        var root = server.RootElement;
        NativeCoverageImageOracleSupport.RequireKeys(root, NativeCoverageImageFields.SchemaVersion, NativeCoverageImageFields.EvidenceKind,
            NativeCoverageImageFields.ServerDll, NativeCoverageImageFields.ServerDllSha256, NativeCoverageImageFields.ServerPdb, NativeCoverageImageFields.ServerPdbSha256, NativeCoverageImageFields.ModuleVersionId);
        NativeCoverageImageOracleSupport.Ensure(root.GetProperty(NativeCoverageImageFields.SchemaVersion).GetInt32()
            == NativeCoverageImageConstants.SchemaVersion
            && root.GetProperty(NativeCoverageImageFields.EvidenceKind).GetString() == NativeCoverageImageConstants.LocalEvidenceKind
            && root.GetProperty(NativeCoverageImageFields.ServerDll).GetString() == sourceDll
            && root.GetProperty(NativeCoverageImageFields.ServerPdb).GetString() == sourcePdb
            && root.GetProperty(NativeCoverageImageFields.ServerDllSha256).GetString() == invocation.ServerDllSha256
            && root.GetProperty(NativeCoverageImageFields.ServerPdbSha256).GetString() == invocation.ServerPdbSha256
            && root.GetProperty(NativeCoverageImageFields.ModuleVersionId).GetString() == invocation.Mvid,
            "The local Server receipt does not bind the observed assembly identity.");
    }

    private static void VerifyOne(JsonElement file,
        IReadOnlyDictionary<string, NativeCoverageImageExpectedFile> expected, string contextPath,
        HashSet<string> paths, int maximumPathCharacters)
    {
        NativeCoverageImageOracleSupport.RequireKeys(file, NativeCoverageImageFields.Path, NativeCoverageImageFields.Mode, NativeCoverageImageFields.Length, NativeCoverageImageFields.Sha256);
        var relative = file.GetProperty(NativeCoverageImageFields.Path).GetString()!;
        NativeCoverageImageOracleSupport.Ensure(paths.Add(relative), "The context manifest has a duplicate file path.");
        if (!expected.TryGetValue(relative, out var source))
        {
            throw new InvalidDataException("The context manifest contains an unexpected file.");
        }
        NativeCoverageImageOracleSupport.Ensure(source.Length == file.GetProperty(NativeCoverageImageFields.Length).GetInt64()
            && source.Sha256 == file.GetProperty(NativeCoverageImageFields.Sha256).GetString()
            && source.Mode == file.GetProperty(NativeCoverageImageFields.Mode).GetInt32(),
            "A context inventory item differs from its independently observed input.");
        var target = Path.Combine(contextPath, relative.Replace('/', Path.DirectorySeparatorChar));
        NativeCoverageImageOracleSupport.Ensure(target.Length <= maximumPathCharacters && File.Exists(target)
            && new FileInfo(target).Length == source.Length
            && NativeCoverageImageOracleSupport.HashFile(target, source.Length) == source.Sha256
            && NativeCoverageImageOracleSupport.Mode(target) == source.Mode,
            "A materialized file differs from its source-bound manifest entry.");
    }

    private static void VerifyCounts(JsonElement result, JsonElement files, byte[] manifestBytes,
        IReadOnlyDictionary<string, NativeCoverageImageExpectedFile> expected,
        NativeCoverageExecutionOptions options)
    {
        var total = expected.Values.Sum(file => file.Length) + manifestBytes.LongLength;
        NativeCoverageImageOracleSupport.Ensure(files.GetArrayLength() == expected.Count
            && result.GetProperty(NativeCoverageImageFields.FileCount).GetInt32() == expected.Count + 1
            && result.GetProperty(NativeCoverageImageFields.TotalBytes).GetInt64() == total
            && expected.Count + 1 <= options.MaximumFiles
            && total <= options.MaximumTotalBytes
            && manifestBytes.Length <= options.MaximumManifestBytes
            && expected.Values.All(file => file.Length <= options.MaximumFileBytes),
            "The materializer counts or complete context exceed independently measured bounds.");
    }

    private static string RelativeSource(string directory, string file)
        => Path.GetRelativePath(NativeCoverageImageFixture.RepositoryRoot,
            Path.Combine(directory, file)).Replace(Path.DirectorySeparatorChar, '/');
}
