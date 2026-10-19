namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class MongoReadinessChecksDocker
{
    private const string Inspect = "inspect", Format = "--format", Stop = "stop", Time = "--time", NoGrace = "0";
    private const string Remove = "rm", Force = "--force";

    internal static async Task<MongoReadinessChecksIdentity> InspectAsync(string name, CancellationToken token)
        => MongoReadinessChecksIdentity.Parse(await RunAsync(
            [Inspect, Format, MongoReadinessChecksIdentity.InspectFormat, name], token));

    internal static async Task RemoveOwnedAsync(MongoReadinessChecksChild child, CancellationToken token)
    {
        var current = await InspectAsync(child.Name, token);
        current.RequireChild(child.Name, child.Image, child.Network, child.Labels);
        if (current.Running)
        {
            _ = await RunAsync([Stop, Time, NoGrace, current.Id], token);
        }
        var stopped = await InspectAsync(child.Name, token);
        stopped.RequireChild(child.Name, child.Image, child.Network, child.Labels);
        MongoReadinessChecksProtocol.Require(stopped.Id == current.Id && !stopped.Running);
        _ = await RunAsync([Remove, Force, stopped.Id], token);
    }

    private static async Task<string> RunAsync(string[] arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = new MongoReadinessChecksProcess(arguments);
        var original = process.CompleteAsync(token);
        try
        {
            await original;
        }
        finally
        {
            await MongoReadinessChecksProcess.JoinAsync(original, StopOriginalAsync(process));
        }
        return await original;
    }

    private static async Task StopOriginalAsync(MongoReadinessChecksProcess process)
    {
        using var cleanup = new CancellationTokenSource(MongoReadinessChecksProtocol.CleanupDeadline, TimeProvider.System);
        var client = process.StopClientAsync(cleanup.Token);
        var readers = process.DrainAsync(cleanup.Token);
        var originals = MongoReadinessChecksProcess.JoinAsync(client, readers);
        try
        {
            await originals;
        }
        finally
        {
            await MongoReadinessChecksProcess.JoinAsync(originals, process.ReleaseAsync(cleanup.Token));
        }
    }
}
