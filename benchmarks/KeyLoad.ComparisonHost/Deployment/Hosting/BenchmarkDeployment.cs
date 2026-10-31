using System.Text.Json;
using Aspire.Hosting;
using KeyLoad.ComparisonHost.Measurement;
using TUnit.Core.Interfaces;

namespace KeyLoad.ComparisonHost.Deployment;

/// <summary>
/// One live database for the whole job. Every case of the type runs against it and records its result; the result
/// file is rewritten after every case so a failed or timed-out job still leaves what it measured.
/// </summary>
internal sealed class BenchmarkDeployment : IAsyncInitializer, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string WrongType = "Run only the test class of the job's type: ";
    private static readonly TimeSpan StartupLimit = TimeSpan.FromMinutes(30);
    private readonly List<CaseResult> cases = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly DateTimeOffset started = TimeProvider.System.GetUtcNow();
    private DistributedApplication? application;
    private IAsyncDisposable? driver;
    private string? failure;
    private DateTimeOffset? finished;

    internal BenchmarkJob Job { get; } = BenchmarkJob.Read();

    internal BenchmarkConnection? Connection { get; private set; }

    public async Task InitializeAsync()
    {
        using var startup = new CancellationTokenSource(StartupLimit);
        try
        {
            application = await BenchmarkApplication.StartAsync(Job, startup.Token);
            Connection = await BenchmarkApplication.ConnectAsync(application, Job, startup.Token);
        }
        catch (Exception error)
        {
            failure = CaseResult.Describe(error);
            throw;
        }
        finally
        {
            await WriteAsync();
        }
    }

    /// <summary>Runs one case with the type's driver, records its result and fails the test when the case failed.</summary>
    internal async Task RunAsync<TDriver>(string type, string caseId, Func<BenchmarkConnection, TDriver> create,
        Func<TDriver, BenchmarkSettings, CancellationToken, Task<CaseResult>> run)
        where TDriver : class, IAsyncDisposable
    {
        if (type != Job.Type)
        {
            throw new InvalidOperationException(WrongType + Job.Type);
        }
        var token = TestContext.Current!.Execution.CancellationToken;
        var begin = TimeProvider.System.GetTimestamp();
        CaseResult result;
        try
        {
            result = await run(Driver(create), Job.Settings, token);
        }
        catch (OperationCanceledException error) when (token.IsCancellationRequested)
        {
            // The run's time limit stopped the case: keep that as its result before the cancellation ends the run.
            await RecordAsync(CaseResult.Failure(caseId, error, TimeProvider.System.GetElapsedTime(begin)));
            throw;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            result = CaseResult.Failure(caseId, error, TimeProvider.System.GetElapsedTime(begin));
        }
        await RecordAsync(result);
        if (result.IsFailed)
        {
            throw new InvalidOperationException(caseId + ": " + result.Error);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (driver is not null)
            {
                await driver.DisposeAsync();
            }
        }
        finally
        {
            if (application is not null)
            {
                await application.DisposeAsync();
            }
            finished = TimeProvider.System.GetUtcNow();
            await WriteAsync();
            gate.Dispose();
        }
    }

    private TDriver Driver<TDriver>(Func<BenchmarkConnection, TDriver> create) where TDriver : class, IAsyncDisposable
    {
        driver ??= create(Connection ?? throw new InvalidOperationException(failure ?? nameof(Connection)));
        return (TDriver)driver;
    }

    private async Task RecordAsync(CaseResult result)
    {
        await gate.WaitAsync();
        try
        {
            cases.Add(result);
        }
        finally
        {
            gate.Release();
        }
        await WriteAsync();
    }

    private async Task WriteAsync()
    {
        var result = new BenchmarkResult(Job.Database, Job.Type, Job.Nodes, Connection is { Image.Length: > 0 } ? Connection.Image : Job.KeyLoadImage, Job.Commit,
            Job.RunId, started, finished, Job.Settings.Records, Job.Settings.Operations, Job.Settings.Clients, failure, [.. cases]);
        Directory.CreateDirectory(Job.Output);
        var temporary = Job.ResultPath + ".tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, result, Json);
        }
        File.Move(temporary, Job.ResultPath, overwrite: true);
    }
}
