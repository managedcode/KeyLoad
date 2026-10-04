using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteHeavyChildProcessFixture : IAsyncDisposable
{
    private static readonly string Source = $$"""
        import { writeFile, readFile, access } from 'node:fs/promises';
        import { join } from 'node:path';
        import { setTimeout } from 'node:timers/promises';
        const ROOT_ARGUMENT = 2, MODE_ARGUMENT = 3, MAXIMUM_ARGUMENT = 4, PREDECESSOR_ARGUMENT = 5;
        const UTF8_ENCODING = 'utf8', NO_PROCESS = 'ESRCH', OUTPUT_CHARACTER = 'x', EXCLUSIVE_CREATE = 'wx';
        const ZERO = 0, ONE = 1, STARTED_FILE = '{{SiteHeavyChildTokens.FixtureStarted}}';
        const RELEASE_FILE = '{{SiteHeavyChildTokens.FixtureRelease}}', PREDECESSOR_FILE = '{{SiteHeavyChildTokens.FixturePredecessor}}';
        const OVERFLOW_MODE = '{{SiteHeavyChildTokens.FixtureOverflow}}', OUTPUT = '{{SiteHeavyChildTokens.FixtureOutput}}';
        const POLL_MILLISECONDS = {{SiteHeavyChildTokens.FixturePollMilliseconds.ToString(CultureInfo.InvariantCulture)}};
        const root = process.argv[ROOT_ARGUMENT], mode = process.argv[MODE_ARGUMENT], maximum = Number(process.argv[MAXIMUM_ARGUMENT]);
        const predecessorPath = process.argv[PREDECESSOR_ARGUMENT];
        const predecessor = predecessorPath ? Number(await readFile(predecessorPath, UTF8_ENCODING)) : ZERO;
        let predecessorAlive = false;
        if (predecessor > ZERO) {
          try { process.kill(predecessor, ZERO); predecessorAlive = true; }
          catch (error) { if (error.code !== NO_PROCESS) throw error; }
        }
        await writeFile(join(root, PREDECESSOR_FILE), JSON.stringify(predecessorAlive), { flag: EXCLUSIVE_CREATE });
        await writeFile(join(root, STARTED_FILE), String(process.pid), { flag: EXCLUSIVE_CREATE });
        while (!(await access(join(root, RELEASE_FILE)).then(() => true, () => false))) await setTimeout(POLL_MILLISECONDS);
        if (mode === OVERFLOW_MODE) {
          await new Promise((resolve, reject) => process.stdout.write(OUTPUT_CHARACTER.repeat(maximum + ONE),
            error => error ? reject(error) : resolve()));
        } else {
          process.stdout.write(OUTPUT);
        }
        """;
    private CancellationTokenSource? _lifetime;
    private readonly SiteTempDirectory _temporary = SiteTempDirectory.Create();
    private Task<SiteProcessResult>? _running;
    private Process? _raw;
    private Task<string>? _rawStdout;
    private Task<string>? _rawStderr;
    private int _observedPid;
    private string? _predecessorPath;

    private string Root => _temporary.Path;

    internal bool HasStarted => File.Exists(Path.Combine(Root, SiteHeavyChildTokens.FixtureStarted));

    internal static async Task<SiteHeavyChildProcessFixture> CreateAsync(CancellationToken token)
    {
        var fixture = new SiteHeavyChildProcessFixture();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, SiteHeavyChildTokens.FixtureScript), Source, token);
            return fixture;
        }
        catch (Exception)
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    internal Task<SiteProcessResult> RunAsync(SiteHeavyChildAdmission admission, CancellationToken token,
        bool github = false, bool overflow = false, bool missingExecutable = false)
    {
        if (_running is not null || _raw is not null)
        {
            throw new InvalidOperationException(SiteHeavyChildTokens.FixtureAlreadyStarted);
        }
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var start = CreateStart(github, overflow, missingExecutable);
        _running = github ? SiteIsolatedGitHubNativeProcess.RunProbeAsync(start, _lifetime.Token, admission)
            : SiteIsolatedNodeProcess.RunProcessAsync(start, _lifetime.Token, admission);
        return _running;
    }

    internal void StartUnsettled(SiteHeavyChildLease lease)
    {
        _raw = new Process { StartInfo = CreateStart(github: false, overflow: false, missingExecutable: false) };
        if (!_raw.Start())
        {
            throw new InvalidOperationException(SiteTokens.NodeProbeDidNotStart);
        }
        lease.MarkStarted(_raw);
        _rawStdout = SiteProcessOutput.ReadAsync(_raw.StandardOutput, SiteTokens.NodeOutputExceeded, CancellationToken.None);
        _rawStderr = SiteProcessOutput.ReadAsync(_raw.StandardError, SiteTokens.NodeOutputExceeded, CancellationToken.None);
    }

    internal Task WaitStartedAsync(CancellationToken token) => WaitUntilAsync(ReadObservedPid, token);

    internal Task WaitExitedAsync(CancellationToken token) => WaitUntilAsync(HasExited, token);

    internal void ExpectPredecessorExit(SiteHeavyChildProcessFixture predecessor)
    {
        _predecessorPath = Path.Combine(predecessor.Root, SiteHeavyChildTokens.FixtureStarted);
    }

    internal async Task<bool> ReadPredecessorAliveAsync(CancellationToken token) => bool.Parse(
        await File.ReadAllTextAsync(Path.Combine(Root, SiteHeavyChildTokens.FixturePredecessor), token));

    internal static Task WaitQueuedAsync(SiteHeavyChildAdmission admission, int count, CancellationToken token)
        => WaitUntilAsync(() => admission.PendingCount == count, token);

    internal Task ReleaseAsync(CancellationToken token) => File.WriteAllTextAsync(
        Path.Combine(Root, SiteHeavyChildTokens.FixtureRelease), string.Empty, token);

    private ProcessStartInfo CreateStart(bool github, bool overflow, bool missingExecutable)
    {
        var start = new ProcessStartInfo(missingExecutable
            ? Path.Combine(Root, SiteHeavyChildTokens.FixtureMissingExecutable) : SiteTokens.NodeExecutable)
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(Path.Combine(Root, SiteHeavyChildTokens.FixtureScript));
        start.ArgumentList.Add(Root);
        start.ArgumentList.Add(overflow ? SiteHeavyChildTokens.FixtureOverflow : SiteHeavyChildTokens.FixtureHold);
        var maximum = github ? SiteIsolatedGitHubProcessOutput.MaximumOutputCharacters : SiteTokens.MaximumNodeOutputCharacters;
        start.ArgumentList.Add(maximum.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(_predecessorPath ?? string.Empty);
        return start;
    }

    private bool ReadObservedPid()
    {
        if (!HasStarted)
        {
            return false;
        }
        var value = File.ReadAllText(Path.Combine(Root, SiteHeavyChildTokens.FixtureStarted));
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _observedPid) && _observedPid > SiteTokens.Zero;
    }

    private bool HasExited()
    {
        try
        {
            using var process = Process.GetProcessById(_observedPid);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return _observedPid > SiteTokens.Zero;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(SiteHeavyChildTokens.FixtureDeadlineMilliseconds);
        while (!condition())
        {
            await Task.Delay(SiteHeavyChildTokens.FixturePollMilliseconds, deadline.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_lifetime is not null)
        {
            await _lifetime.CancelAsync();
        }
        if (_running is not null)
        {
            await ObserveRunnerAsync(_running);
        }
        if (_raw is not null)
        {
            await SiteHeavyChildLease.StopAndObserveAsync(_raw, _rawStdout!, _rawStderr!, lease: null);
            _raw.Dispose();
        }
        if (ReadObservedPid())
        {
            await WaitExitedAsync(CancellationToken.None);
        }
        _lifetime?.Dispose();
        await _temporary.DisposeAsync();
    }

    private static async Task ObserveRunnerAsync(Task<SiteProcessResult> running)
    {
        try
        {
            await running.WaitAsync(TimeSpan.FromMilliseconds(SiteHeavyChildTokens.FixtureDeadlineMilliseconds));
        }
        catch (Exception error) when (running.IsCompleted && error is OperationCanceledException or InvalidOperationException or Win32Exception or TimeoutException)
        {
            // The test asserts the original failure; disposal separately verifies the actual native PID exited.
        }
    }
}
