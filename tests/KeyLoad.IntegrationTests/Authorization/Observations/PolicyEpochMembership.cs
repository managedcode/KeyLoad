using System.Net.Http.Json;
using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class PolicyEpochMembership
{
    private const string Route = "/health/membership-ready";
    private const int MaximumResponseBytes = 16_384;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    internal static async Task WaitForSurvivorAsync(ClusterFixture fixture, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        http.MaxResponseContentBufferSize = MaximumResponseBytes;
        using var timer = new PeriodicTimer(PollInterval, TimeProvider.System);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            using var response = await http.GetAsync(new Uri(Route, UriKind.Relative), token);
            response.EnsureSuccessStatusCode();
            var snapshot = await response.Content.ReadFromJsonAsync<MembershipSnapshot?>(JsonDefaults.Options, token)
                ?? throw new InvalidDataException("The Orleans membership health response is missing.");
            await File.WriteAllTextAsync(Path.Combine(TestContext.ResultsDirectory, "policy-authority-membership.json"),
                JsonSerializer.Serialize(snapshot, JsonDefaults.Options), token);
            if (snapshot.ActiveSilos.SequenceEqual(new[] { McpCallerProtocol.Node2 }, StringComparer.Ordinal))
            {
                return;
            }
            await timer.WaitForNextTickAsync(token);
        }
    }

    private readonly record struct MembershipSnapshot(long Version, string[] ActiveSilos);
}
