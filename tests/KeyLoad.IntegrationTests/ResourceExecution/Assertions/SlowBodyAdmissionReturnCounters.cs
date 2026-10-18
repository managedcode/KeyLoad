using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using static KeyLoad.IntegrationTests.Features.ResourceExecution.EarlyAdmissionQualificationProtocol;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class SlowBodyAdmissionReturnCounters
{
    internal static async Task<HttpAdmissionStatus> BaselineAsync(RequestCqrsClusterCallers root, CancellationToken token)
    {
        while (true)
        {
            var status = await ReadAsync(root, token);
            if (status.Node.Commands == QuietCommands && status.VerifiedScopes.Commands == QuietCommands
                && status.Node.ControlCommands == EmptyControls && status.VerifiedScopes.ControlCommands == EmptyControls)
            {
                return status;
            }
            await Task.Delay(Poll, token);
        }
    }
    internal static Task HeldAsync(RequestCqrsClusterCallers root, HttpAdmissionStatus baseline, int length, CancellationToken token)
        => ExactAsync(root, baseline with
        {
            Node = baseline.Node with
            {
                Commands = baseline.Node.Commands + QuietCommands,
                RetainedBytes = baseline.Node.RetainedBytes + HeavyBytes + EnvelopeBytes + length * BodyScale
            },
            VerifiedScopes = baseline.VerifiedScopes with
            {
                Commands = baseline.VerifiedScopes.Commands + QuietCommands,
                RetainedBytes = baseline.VerifiedScopes.RetainedBytes + EnvelopeBytes
            }
        }, token);
    internal static async Task ExactAsync(RequestCqrsClusterCallers root, HttpAdmissionStatus expected, CancellationToken token)
    {
        while (true)
        {
            if (await ReadAsync(root, token) == expected)
            {
                return;
            }
            await Task.Delay(Poll, token);
        }
    }
    private static async Task<HttpAdmissionStatus> ReadAsync(RequestCqrsClusterCallers root, CancellationToken token)
        => (await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.AdmissionStatusAsync(token))).Http!;
}
