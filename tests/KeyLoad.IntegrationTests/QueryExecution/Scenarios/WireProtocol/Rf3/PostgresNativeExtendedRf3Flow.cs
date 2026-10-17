using KeyLoad.Client;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresNativeExtendedRf3Flow
{
    internal static async Task<PostgresNativeExtendedRf3State> WarmAsync(ClusterFixture fixture,
        PostgresRf3Selection selection, KeyLoadClient root, PostgresRf3State original, CancellationToken token)
    {
        var composition = await PostgresCompositionRf3Flow.WarmAsync(fixture, root, original, token);
        var reads = await PostgresPureReadRf3Flow.WarmAsync(fixture, selection, root, composition.Image, token);
        var commands = await PostgresClosedCommandRf3Flow.WarmAsync(fixture, root, composition.Image, token);
        return new(composition, reads, commands);
    }

    internal static async Task ColdAsync(ClusterFixture fixture, PostgresNativeExtendedRf3State state, CancellationToken token)
    {
        await PostgresCompositionRf3Flow.ColdAsync(fixture, state.Composition, token);
        await PostgresPureReadRf3Flow.ColdAsync(fixture, state.Reads, token);
        await PostgresClosedCommandRf3Flow.ColdAsync(fixture, state.ClosedCommands, token);
    }
}
