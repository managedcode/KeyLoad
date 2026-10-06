using System.Diagnostics;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class NativeSerializationBenchmarkStartInfo
{
    // Customized Dry becomes a mutator in official BDN, replacing the declared job settings.
    internal static readonly string[] DryArguments =
    [
        "--job", "Dry", "--launchCount", "1", "--warmupCount", "0", "--iterationCount", "1", "--iterationTime", "1",
        "--exporters", "fulljson", "--filter", "*Native*SerializationBenchmarks*", "--keepFiles", "--stopOnFirstError"
    ];

    internal static ProcessStartInfo Create(string directory)
    {
        var root = EmbeddedBenchmarkProcess.RepositoryRoot();
        var assembly = Path.Combine(root, "benchmarks", "KeyLoad.Benchmarks", "bin", "Release", "net10.0", "KeyLoad.Benchmarks.dll");
        if (!File.Exists(assembly))
        {
            throw new FileNotFoundException("The Release native benchmark executable is not built.", assembly);
        }
        Directory.CreateDirectory(directory);
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root
        };
        startInfo.ArgumentList.Add(assembly);
        foreach (var argument in DryArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.ArgumentList.Add("--artifacts");
        startInfo.ArgumentList.Add(directory);
        startInfo.Environment[NativeSerializationBenchmarkManifest.DirectoryVariable] = Path.Combine(directory, "corpus");
        return startInfo;
    }
}
