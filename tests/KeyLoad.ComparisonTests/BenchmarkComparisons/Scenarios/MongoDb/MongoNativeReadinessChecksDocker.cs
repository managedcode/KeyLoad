namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class MongoNativeReadinessChecksDocker
{
    private const string Inspect = "inspect", Format = "--format", Stop = "stop", Time = "--time", NoGrace = "0";
    private const string Remove = "rm", Force = "--force";

    internal static async Task<MongoNativeReadinessChecksIdentity> InspectAsync(string name, CancellationToken token)
        => MongoNativeReadinessChecksIdentity.Parse(await RunAsync(
            [Inspect, Format, MongoNativeReadinessChecksIdentity.InspectFormat, name], token));

    internal static async Task RemoveOwnedAsync(MongoNativeReadinessChecksChild child, CancellationToken token)
    {
        var current = await InspectAsync(child.Name, token);
        current.RequireChild(child.Name, child.Image, child.Network, child.Labels);
        if (current.Running)
        {
            _ = await RunAsync([Stop, Time, NoGrace, current.Id], token);
        }
        var stopped = await InspectAsync(child.Name, token);
        stopped.RequireChild(child.Name, child.Image, child.Network, child.Labels);
        MongoNativeReadinessChecksProtocol.Require(stopped.Id == current.Id && !stopped.Running);
        _ = await RunAsync([Remove, Force, stopped.Id], token);
    }

    private static async Task<string> RunAsync(string[] arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = new MongoNativeReadinessChecksProcess(arguments);
        var original = process.CompleteAsync(token);
        try
        {
            await original;
        }
        finally
        {
            await MongoNativeReadinessChecksProcess.JoinAsync(original, StopOriginalAsync(process));
        }
        return await original;
    }

    private static async Task StopOriginalAsync(MongoNativeReadinessChecksProcess process)
    {
        using var cleanup = new CancellationTokenSource(MongoNativeReadinessChecksProtocol.CleanupDeadline, TimeProvider.System);
        var client = process.StopClientAsync(cleanup.Token);
        var readers = process.DrainAsync(cleanup.Token);
        var originals = MongoNativeReadinessChecksProcess.JoinAsync(client, readers);
        try
        {
            await originals;
        }
        finally
        {
            await MongoNativeReadinessChecksProcess.JoinAsync(originals, process.ReleaseAsync(cleanup.Token));
        }
    }
}
