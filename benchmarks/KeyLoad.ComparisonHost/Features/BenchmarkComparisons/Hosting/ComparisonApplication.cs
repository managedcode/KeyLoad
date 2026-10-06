using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Composes the comparison workload, cancellation lifetime and report output.</summary>
[KeyLoad.ConfigurationBinding]
internal static class ComparisonApplication
{
    /// <summary>Validates configuration, runs the existing comparison library and writes its reports.</summary>
    /// <param name="arguments">Command-line configuration arguments.</param>
    /// <returns>The existing comparison process exit meaning.</returns>
    internal static async Task<int> RunAsync(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        _ = SerializationExecutionRegistration.Process.Value;
        using var configuration = new ConfigurationManager();
        configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, NativeComparisonExecutionRegistration.ConfigurationPath), optional: false);
        configuration.AddEnvironmentVariables();
        configuration.AddCommandLine(arguments);
        var startup = ComparisonStartupRegistration.Read(configuration).Value;
        var executionOptions = NativeComparisonExecutionRegistration.Read(configuration);
        var timeProvider = TimeProvider.System;
        var owner = new ComparisonTargetOwner(executionOptions,
            NativeComparisonExecutionRegistration.ReadDiagnostics(configuration),
            NativeComparisonExecutionRegistration.ReadIsolatedAdmission(configuration),
            NativeComparisonExecutionRegistration.ReadLifecycle(configuration),
            NativeComparisonExecutionRegistration.ReadClient(configuration),
            NativeComparisonExecutionRegistration.ReadTranslation(configuration), timeProvider);
        ComparisonCancellationLifetime? cancellationLifetime = null;
        try
        {
            cancellationLifetime = new ComparisonCancellationLifetime(executionOptions, timeProvider);
            if (startup.Target is not null)
            {
                return await IsolatedHostApplication.RunAsync(configuration, cancellationLifetime.Token, timeProvider);
            }

            if (string.Equals(startup.Profile,
                    ComparisonHostConstants.TimeSeriesProfile, StringComparison.OrdinalIgnoreCase))
            {
                return await TimeSeriesComparisonApplication.RunAsync(configuration, cancellationLifetime.Token, timeProvider);
            }

            var settingsOptions = ComparisonHostSettings.ReadOptions(configuration);
            var settings = settingsOptions.Value;
            var targets = owner.CreateTargets(settings);
            return await RunComparisonAsync(settings, targets, executionOptions, timeProvider, cancellationLifetime.Token);
        }
        finally
        {
            cancellationLifetime?.DetachAndFence();
            try
            {
                await owner.DisposeAsync();
            }
            finally
            {
                cancellationLifetime?.Dispose();
            }
        }
    }

    private static async Task<int> RunComparisonAsync(ComparisonHostSettings settings,
        KeyLoad.Comparisons.IComparisonTarget[] targets, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var runner = new KeyLoad.Comparisons.ComparisonRunner(settings.WorkloadOptions, executionOptions, Console.WriteLine, timeProvider);
        var report = await runner.RunAsync(targets, settings.SourceRevision, cancellationToken, settings.Storage);
        if (settings.ExecutionIdentity is { } identity)
        {
            report = report with
            {
                Provenance = identity.Provenance,
                LoadGeneratorImage = identity.LoadGeneratorImage
            };
        }

        await KeyLoad.Comparisons.ReportWriter.WriteAsync(report, settings.OutputDirectory, executionOptions, cancellationToken);
        Console.WriteLine(KeyLoad.Comparisons.ReportWriter.Markdown(report));
        Console.WriteLine(ComparisonHostConstants.ReportsPrefix + settings.OutputDirectory);
        return report.Cases.Any(item => item.Status == ComparisonHostConstants.FailedStatus)
            ? ComparisonHostConstants.FailedExitCode
            : ComparisonHostConstants.SuccessfulExitCode;
    }

    private sealed class ComparisonCancellationLifetime : IDisposable
    {
        private readonly System.Threading.Lock gate = new();
        private readonly CancellationTokenSource source;
        private readonly ConsoleCancelEventHandler handler;
        private readonly CancellationToken token;
        private bool closing;
        private bool handlerAttached;
        private bool cancellationInProgress;
        private bool disposeRequested;
        private bool disposed;

        internal ComparisonCancellationLifetime(IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider timeProvider)
        {
            source = new(executionOptions.Value.HostLifetime, timeProvider);
            token = source.Token;
            handler = OnCancelKeyPress;
            try
            {
                Console.CancelKeyPress += handler;
                handlerAttached = true;
            }
            catch (Exception)
            {
                lock (gate)
                {
                    closing = true;
                    Console.CancelKeyPress -= handler;
                }
                source.Dispose();
                throw;
            }
        }

        internal CancellationToken Token => token;

        internal void DetachAndFence()
        {
            lock (gate)
            {
                if (closing)
                {
                    return;
                }

                closing = true;
                if (handlerAttached)
                {
                    Console.CancelKeyPress -= handler;
                    handlerAttached = false;
                }
            }
        }

        public void Dispose()
        {
            DetachAndFence();
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                if (cancellationInProgress)
                {
                    disposeRequested = true;
                    return;
                }

                DisposeSource();
            }
        }

        private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            lock (gate)
            {
                eventArgs.Cancel = true;
                if (closing)
                {
                    return;
                }

                cancellationInProgress = true;
                try
                {
                    source.Cancel();
                }
                finally
                {
                    cancellationInProgress = false;
                    if (disposeRequested)
                    {
                        DisposeSource();
                    }
                }
            }
        }

        private void DisposeSource()
        {
            if (!disposed)
            {
                disposed = true;
                source.Dispose();
            }
        }
    }
}
