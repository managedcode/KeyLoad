using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.UnitTests.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.ReleaseDelivery;

internal static class ReleaseVersionNodeProcess
{
    private const string ModuleFile = "release-version.mjs";
    private const string ScriptsDirectory = "scripts";
    private const string FeaturesDirectory = "Features";
    private const string SliceDirectory = "ReleaseDelivery";
    private const string PathVariable = "PATH";
    private const int TimeoutSeconds = 15;
    private const int OutputLimit = 65_536;
    private const int BufferSize = 4096;
    private const string Failure = "The real release-version Node child exceeded its bound or failed.";
    private const string Probe = """
        import { readFileSync } from 'node:fs';
        import { pathToFileURL } from 'node:url';
        const [modulePath] = process.argv.slice(1);
        const request = JSON.parse(readFileSync(0, 'utf8'));
        const api = await import(pathToFileURL(modulePath).href);
        try {
          const result = api.resolveReleaseVersion(request);
          process.stdout.write(JSON.stringify({ result }));
        } catch (error) {
          process.stdout.write(JSON.stringify({ error: error?.code ?? 'UNEXPECTED' }));
        }
        """;

    internal static async Task<JsonElement> RunAsync(JsonObject request, CancellationToken token)
    {
        using var deadlineTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds), TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, deadlineTimeout.Token);
        using var process = new Process { StartInfo = StartInfo() };
        if (!process.Start())
        {
            throw new InvalidOperationException(Failure);
        }

        var output = ReadBoundedAsync(process.StandardOutput, deadline.Token);
        var error = ReadBoundedAsync(process.StandardError, deadline.Token);
        try
        {
            await process.StandardInput.WriteAsync(request.ToJsonString().AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            var streams = await Task.WhenAll(output, error).WaitAsync(deadline.Token);
            if (process.ExitCode != 0 || streams[1].Length != 0)
            {
                throw new InvalidOperationException(Failure);
            }

            using var document = JsonDocument.Parse(streams[0]);
            return document.RootElement.Clone();
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private static ProcessStartInfo StartInfo()
    {
        var repository = IsolatedAggregateNodeProcess.RepositoryRoot();
        var module = Path.Combine(repository, ScriptsDirectory, FeaturesDirectory, SliceDirectory, ModuleFile);
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = repository,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("--input-type=module");
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add(Probe);
        start.ArgumentList.Add(module);
        var path = Environment.GetEnvironmentVariable(PathVariable);
        start.Environment.Clear();
        if (!string.IsNullOrEmpty(path))
        {
            start.Environment[PathVariable] = path;
        }

        return start;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var result = new StringBuilder();
        var buffer = new char[BufferSize];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), token);
            if (read == 0)
            {
                return result.ToString();
            }

            if (result.Length + read > OutputLimit)
            {
                throw new InvalidOperationException(Failure);
            }

            result.Append(buffer, 0, read);
        }
    }
}
