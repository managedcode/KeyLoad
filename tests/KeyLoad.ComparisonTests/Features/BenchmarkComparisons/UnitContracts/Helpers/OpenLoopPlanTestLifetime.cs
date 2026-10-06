using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class OpenLoopPlanTestLifetime
{
    private const string DirectoryPrefix = "keyload-openloop-plan-";
    private const string OutputFileName = "open-loop-plan.json";

    internal static async Task RunAsync(Func<string, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        string? ownedDirectory = null;
        var failures = new List<Exception>();
        async Task ExecuteAsync()
        {
            var candidate = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                throw new IOException("The open-loop plan test path already exists.");
            }
            Directory.CreateDirectory(candidate);
            ownedDirectory = candidate;
            await operation(Path.Combine(candidate, OutputFileName), cancellationToken).ConfigureAwait(false);
        }
        void Cleanup()
        {
            if (ownedDirectory is not null && Directory.Exists(ownedDirectory))
            {
                Directory.Delete(ownedDirectory, recursive: true);
            }
        }
        await ServerFailureObserver.ObserveAsync(ExecuteAsync, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(Cleanup, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
