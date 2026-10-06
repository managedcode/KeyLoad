using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class TemporaryGitCheckout : IAsyncDisposable
{
    private const string AcceptedProperty = "accepted";
    private const string MessageProperty = "message";
    private const string HeadProperty = "head";
    private const string SourceDirectory = "src/KeyLoad.Server";
    private const string TrackedFileName = "TrackedSource.cs";
    private const string UntrackedFileName = "UntrackedProbe.cs";
    private const string IgnoredFileName = "GeneratedCache.tmp";
    private const string UntrackedFile = "namespace TemporaryCheckout;\ninternal sealed class UntrackedProbe { }\n";
    private const string IgnoredFile = "generated temporary content\n";

    private const string SetupProgram = """
        import { copyFile, mkdir, writeFile } from 'node:fs/promises';
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const { runBounded } = await import(pathToFileURL(process.argv[1]).href);
        const gitignore = process.argv[2];
        const root = process.argv[3];
        const source = path.join(root, 'src/KeyLoad.Server');
        await mkdir(source, { recursive: true });
        await copyFile(gitignore, path.join(root, '.gitignore'));
        const trackedSource = 'namespace TemporaryCheckout;\ninternal sealed class TrackedSource { }\n';
        await writeFile(path.join(source, 'TrackedSource.cs'), trackedSource);
        const environment = { ...process.env, GIT_CONFIG_NOSYSTEM: '1', GIT_CONFIG_GLOBAL: '/dev/null' };
        async function git(args) {
          const result = await runBounded('git', args, {
            cwd: root, environment, timeoutMs: 30000, maximumOutputBytes: 65536,
          });
          if (!result.success) throw new Error('Temporary Git setup operation failed.');
          return result.stdout.trim();
        }
        await git(['init', '--quiet']);
        await git(['config', 'user.name', 'KeyLoad source checkout test']);
        await git(['config', 'user.email', 'source-checkout-test@example.invalid']);
        await git(['add', '--', '.gitignore', 'src/KeyLoad.Server/TrackedSource.cs']);
        await git(['commit', '--quiet', '-m', 'committed source fixture']);
        const head = await git(['rev-parse', 'HEAD']);
        process.stdout.write(JSON.stringify({ head }));
        """;

    private const string VerifyProgram = """
        import { pathToFileURL } from 'node:url';
        const [module, workspace, sourceSha] = process.argv.slice(1);
        process.argv = [];
        const { verifySourceCheckout } = await import(pathToFileURL(module).href);
        const context = { workspace, sourceSha };
        try {
          await verifySourceCheckout(context);
          process.stdout.write(JSON.stringify({ accepted: true }));
        } catch (failure) {
          process.stdout.write(JSON.stringify({
            accepted: false,
            message: failure instanceof Error ? failure.message : 'Source checkout validation failed.',
          }));
        }
        """;

    private readonly string root;
    private readonly string head;

    private TemporaryGitCheckout(string root, string head)
    {
        this.root = root;
        this.head = head;
    }

    internal string UntrackedSourcePath => Path.Combine(root, SourceDirectory, UntrackedFileName);
    internal string IgnoredGeneratedFilePath => Path.Combine(root, SourceDirectory, IgnoredFileName);

    internal static async Task<TemporaryGitCheckout> CreateAsync(CancellationToken token)
    {
        var root = Directory.CreateTempSubdirectory("keyload-source-checkout-").FullName;
        try
        {
            return await IsolatedAggregateNodeGuardedInvocation.InvokeAsync(async () =>
            {
                var repository = IsolatedAggregateNodeProcess.RepositoryRoot();
                var arguments = new[]
                {
                    "--input-type=module", "-e", SetupProgram,
                    IsolatedAggregateNodeProcess.Module("image-process.mjs"),
                    Path.Combine(repository, ".gitignore"), root
                };
                var result = await IsolatedAggregateNodeProcess.RunAsync(arguments, token);
                if (result.ExitCode != 0 || result.Error.Length != 0)
                {
                    throw new InvalidOperationException("Actual temporary Git checkout setup failed.");
                }

                using var document = JsonDocument.Parse(result.Output);
                var head = document.RootElement.GetProperty(HeadProperty).GetString()
                    ?? throw new InvalidOperationException("Actual temporary Git HEAD is missing.");
                return new TemporaryGitCheckout(root, head);
            });
        }
        catch (AggregateException envelope)
        {
            var primary = IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope);
            var cleanupFailures = new List<Exception>();
            IsolatedAggregateNodeGuardedInvocation.Capture(() => DeleteOwnedRoot(root), cleanupFailures.Add);
            if (cleanupFailures.Count > 0)
            {
                throw new AggregateException("Temporary Git setup and owned-root cleanup both failed.",
                    [primary, .. cleanupFailures]);
            }
            ExceptionDispatchInfo.Capture(primary).Throw();
            throw;
        }
    }

    internal async Task<SourceCheckoutResult> VerifySourceCheckoutAsync(CancellationToken token)
    {
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", VerifyProgram,
                IsolatedAggregateNodeProcess.Module("prepare-images.mjs"), root, head], token);
        if (result.ExitCode != 0 || result.Error.Length != 0)
        {
            throw new InvalidOperationException("Production source checkout validation did not settle successfully.");
        }

        using var document = JsonDocument.Parse(result.Output);
        var report = document.RootElement;
        var accepted = report.GetProperty(AcceptedProperty).GetBoolean();
        var message = report.TryGetProperty(MessageProperty, out var value) ? value.GetString() : null;
        return new(accepted, message);
    }

    internal Task CreateUntrackedSourceAsync(CancellationToken token)
        => File.WriteAllTextAsync(UntrackedSourcePath, UntrackedFile, token);

    internal Task RemoveUntrackedSourceAsync(CancellationToken token)
        => RemoveOwnedFileAsync(UntrackedSourcePath, token);

    internal Task CreateIgnoredGeneratedFileAsync(CancellationToken token)
        => File.WriteAllTextAsync(IgnoredGeneratedFilePath, IgnoredFile, token);

    internal async Task<string> ReadTrackedHashAsync(CancellationToken token)
    {
        var path = Path.Combine(root, SourceDirectory, TrackedFileName);
        var bytes = await File.ReadAllBytesAsync(path, token);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public ValueTask DisposeAsync()
    {
        DeleteOwnedRoot(root);
        return ValueTask.CompletedTask;
    }

    private static void DeleteOwnedRoot(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static Task RemoveOwnedFileAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        File.Delete(path);
        return Task.CompletedTask;
    }

    internal sealed record SourceCheckoutResult(bool Accepted, string? Message);
}
