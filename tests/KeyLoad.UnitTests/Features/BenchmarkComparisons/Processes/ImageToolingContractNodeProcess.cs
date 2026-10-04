using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed record ImageToolingContractNodeResult(bool Succeeded, JsonElement Value, string Message);

internal static class ImageToolingContractNodeProgram
{
    internal const string Source = """
        import { readFileSync } from 'node:fs';
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const moduleRoot = process.env.KEYLOAD_IMAGE_MODULE_ROOT;
        const moduleUrl = name => pathToFileURL(path.join(moduleRoot, name)).href;
        const inputs = await import(moduleUrl('image-inputs.mjs'));
        const manifest = await import(moduleUrl('image-manifest.mjs'));
        const evidence = await import(moduleUrl('image-evidence.mjs'));
        const request = JSON.parse(readFileSync(0, 'utf8'));
        const operation = request.operation;
        const environment = request.environment;
        try {
          let value;
          if (operation === 'context') {
            const context = inputs.createRunContext(environment, request.platform);
            value = { sourceSha: context.sourceSha, runId: context.runId, runAttempt: context.runAttempt, repository: context.repository, ref: context.ref };
          } else if (operation === 'source') {
            const context = inputs.createRunContext(environment, request.platform);
            inputs.assertCleanSourceRevision(context, request.head, request.diffExitCode, request.status);
            value = true;
          } else if (operation === 'endpoint') {
            inputs.assertLocalUnixEndpoint(request.endpoint, request.dockerHost);
            value = true;
          } else if (operation === 'metadata') {
            value = manifest.parseImageMetadata(request.output, request.expectedRevision);
          } else if (operation === 'manifest') {
            const bytes = Buffer.from(request.bytesBase64, 'base64');
            const result = manifest.parseManifestEvidence(bytes, request.digestHeader, request.contentTypeHeader,
              request.expectedRevision, request.configId, request.imageName, request.taggedReference);
            value = { finalReference: result.finalReference, manifestDigest: result.manifest.manifestSha256,
              registryDigest: result.manifest.digestHeader, revisionLabel: result.sourceLabelValue,
              configId: result.configImageId, manifestBytesBase64: result.manifest.manifestBytesBase64 };
          } else if (operation === 'diagnostics') {
            value = evidence.sanitizeNativeOutput(request.output);
          } else {
            throw new Error('Unsupported image tooling contract operation.');
          }
          process.stdout.write(JSON.stringify({ ok: true, value }) + '\n');
        } catch (error) {
          const safeMessage = error instanceof Error ? error.message : 'Image tooling contract input was rejected.';
          process.stdout.write(JSON.stringify({ ok: false, message: safeMessage }) + '\n');
        }
        """;
}

internal static class ImageToolingContractNodeProcess
{
    private const string NodeCommand = "node";
    private const string NodeModuleRootEnvironment = "KEYLOAD_IMAGE_MODULE_ROOT";
    private const string PathEnvironment = "PATH";
    private const string NodeArgumentsMode = "--input-type=module";
    private const string NodeArgumentsEval = "-e";
    private const string SuccessProperty = "ok";
    private const string ValueProperty = "value";
    private const string MessageProperty = "message";
    private const string ProbeFailureMessage = "The image tooling Node contract probe failed.";
    private const string InputLimitMessage = "The image tooling Node contract input exceeded its bound.";
    private const int ProcessTimeoutSeconds = 10;
    private const int CleanupTimeoutSeconds = 2;
    private const int MaximumInputBytes = 64 * 1024;
    private const int MaximumOutputCharacters = 64 * 1024;

    internal static async Task<ImageToolingContractNodeResult> RunAsync(object request,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request);
        if (JsonSerializer.SerializeToUtf8Bytes(request).Length > MaximumInputBytes)
        {
            throw new InvalidOperationException(InputLimitMessage);
        }

        var repository = RepositoryRoot();
        var moduleRoot = Path.Combine(repository, "scripts", "Features", "BenchmarkComparisons");
        using var process = new Process { StartInfo = CreateStartInfo(repository, moduleRoot) };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(ProbeFailureMessage);
            }
        }
        catch (Exception)
        {
            throw new InvalidOperationException(ProbeFailureMessage);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(ProcessTimeoutSeconds));
        var stdoutTask = ReadBoundedAsync(process.StandardOutput, deadline.Token);
        var stderrTask = ReadBoundedAsync(process.StandardError, deadline.Token);
        try
        {
            await process.StandardInput.WriteLineAsync(json.AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            var output = await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(deadline.Token);
            if (process.ExitCode != 0 || output[1].Length != 0)
            {
                throw new InvalidOperationException(ProbeFailureMessage);
            }

            return ParseResult(output[0]);
        }
        catch (Exception)
        {
            KillOwnedProcess(process);
            await ObserveAsync(stdoutTask);
            await ObserveAsync(stderrTask);
            throw;
        }
    }

    private static ImageToolingContractNodeResult ParseResult(string output)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var succeeded = root.GetProperty(SuccessProperty).GetBoolean();
        var value = succeeded ? root.GetProperty(ValueProperty).Clone() : default;
        var message = succeeded ? string.Empty : root.GetProperty(MessageProperty).GetString() ?? string.Empty;
        return new(succeeded, value, message);
    }

    private static ProcessStartInfo CreateStartInfo(string repository, string moduleRoot)
    {
        var start = new ProcessStartInfo(NodeCommand)
        {
            WorkingDirectory = repository,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(NodeArgumentsMode);
        start.ArgumentList.Add(NodeArgumentsEval);
        start.ArgumentList.Add(ImageToolingContractNodeProgram.Source);
        var path = Environment.GetEnvironmentVariable(PathEnvironment);
        start.Environment.Clear();
        if (!string.IsNullOrWhiteSpace(path))
        {
            start.Environment[PathEnvironment] = path;
        }

        start.Environment[NodeModuleRootEnvironment] = moduleRoot;
        return start;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var output = new StringBuilder();
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0)
            {
                return output.ToString();
            }

            if (output.Length + count > MaximumOutputCharacters)
            {
                throw new InvalidOperationException(ProbeFailureMessage);
            }
            output.Append(buffer, 0, count);
        }
    }

    private static void KillOwnedProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: false);
            }
        }
        catch (InvalidOperationException)
        {
            // The owned Node process can exit while the timeout path is observing it.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The owned process may become unavailable while its exit is observed.
        }
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(CleanupTimeoutSeconds));
        }
        catch (OperationCanceledException)
        {
            // The bounded reader was canceled after its owned child was stopped.
        }
        catch (IOException)
        {
            // The reader can close while the owned child is being reaped.
        }
        catch (ObjectDisposedException)
        {
            // The reader can be disposed at the bounded cleanup limit.
        }
        catch (InvalidOperationException exception) when (exception.Message == ProbeFailureMessage)
        {
            // The bounded reader rejected excessive output with its fixed safe error.
        }
        catch (TimeoutException)
        {
            _ = task.ContinueWith(static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private static string RepositoryRoot()
    {
        foreach (var path in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "KeyLoad.slnx"))
                    && File.Exists(Path.Combine(directory.FullName, "scripts", "Features", "BenchmarkComparisons", "image-inputs.mjs")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException(ProbeFailureMessage);
    }
}
