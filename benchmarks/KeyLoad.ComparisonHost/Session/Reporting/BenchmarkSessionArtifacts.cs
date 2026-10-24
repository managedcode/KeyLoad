using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.BenchmarkComparisons;

/// <summary>Retains immutable original reports before their execution test assertion can fail.</summary>
internal static class BenchmarkSessionArtifacts
{
    internal const string StateFile = "session-state.json";
    private const string Reports = "reports";
    private const string JsonSuffix = ".json";
    private const string PdbSuffix = ".pdb";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<BenchmarkSessionArtifact> WriteCaseAsync(string root, BenchmarkSessionCaseReport report,
        IOptions<ComparisonHostExecutionOptions> execution)
    {
        var relative = Reports + Path.AltDirectorySeparatorChar + report.CaseId + JsonSuffix;
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            execution.Value.FileBufferBytes, FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(output, report, Json);
            await output.FlushAsync();
        }
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            execution.Value.FileBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return new(report.CaseId, relative, Convert.ToHexStringLower(await SHA256.HashDataAsync(input)));
    }

    internal static Task WriteStateAsync(string root, BenchmarkSessionState state,
        IOptions<ComparisonHostExecutionOptions> execution)
        => WriteAsync(Path.Combine(root, StateFile), JsonSerializer.SerializeToUtf8Bytes(state, Json), execution);

    internal static ImmutableArray<BenchmarkSessionAssembly> Assemblies()
        => new[] { typeof(BenchmarkSessionArtifacts).Assembly, typeof(BenchmarkSession).Assembly,
                typeof(KeyLoad.Client.KeyLoadClient).Assembly }
            .Select(CaptureAssembly).ToImmutableArray();

    private static BenchmarkSessionAssembly CaptureAssembly(Assembly assembly)
    {
        var file = assembly.Location;
        var pdb = Path.ChangeExtension(file, PdbSuffix);
        return new(Path.GetFileName(file), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(pdb))));
    }

    private static async Task WriteAsync(string file, byte[] bytes, IOptions<ComparisonHostExecutionOptions> execution)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            execution.Value.FileBufferBytes, FileOptions.Asynchronous);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }
}
