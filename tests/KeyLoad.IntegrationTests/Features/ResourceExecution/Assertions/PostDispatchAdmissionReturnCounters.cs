using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class PostDispatchAdmissionReturnCounters
{
    private const int StatusRequest = 1;
    private const int NoControlWork = 0;
    private const long EnvelopeBytes = 4_096;
    private const long BodyReservationScale = 4;
    private const long NativeCharacterWidth = 2;
    private const long WorkingReservationRoundUp = 1;

    internal static async Task<HttpAdmissionStatus> BaselineAsync(KeyLoadClient root, CancellationToken token)
    {
        while (true)
        {
            var current = await ReadAsync(root, token);
            if (current.Node.Commands == StatusRequest && current.VerifiedScopes.Commands == StatusRequest
                && current.Node.ControlCommands == NoControlWork && current.VerifiedScopes.ControlCommands == NoControlWork)
            {
                return current;
            }
            await Task.Delay(new ConnectionRf3Options().PollInterval, token);
        }
    }

    internal static Task HeldAsync(KeyLoadClient root, HttpAdmissionStatus baseline, CancellationToken token)
    {
        // The real SDK sends native JsonContent without buffering or a declared length.
        var working = baseline.Limits.OtherReservedBytes;
        var originalBytes = checked(BodyReservationScale * baseline.Limits.MaxBodyBytes
            + NativeCharacterWidth * ((working + WorkingReservationRoundUp) / NativeCharacterWidth) + EnvelopeBytes);
        var expected = baseline with
        {
            Node = baseline.Node with
            {
                Commands = baseline.Node.Commands + StatusRequest,
                RetainedBytes = baseline.Node.RetainedBytes + originalBytes
            },
            VerifiedScopes = baseline.VerifiedScopes with
            {
                Commands = baseline.VerifiedScopes.Commands + StatusRequest,
                RetainedBytes = baseline.VerifiedScopes.RetainedBytes + EnvelopeBytes,
                ActiveTenantScopes = baseline.VerifiedScopes.ActiveTenantScopes + StatusRequest,
                ActivePrincipalScopes = baseline.VerifiedScopes.ActivePrincipalScopes + StatusRequest
            }
        };
        return ExactAsync(root, expected, token);
    }

    internal static async Task ExactAsync(KeyLoadClient root, HttpAdmissionStatus baseline, CancellationToken token)
    {
        while (await ReadAsync(root, token) != baseline)
        {
            await Task.Delay(new ConnectionRf3Options().PollInterval, token);
        }
    }

    private static async Task<HttpAdmissionStatus> ReadAsync(KeyLoadClient root, CancellationToken token)
        => (await McpCallerAssertions.SdkSuccessAsync(await root.AdmissionStatusAsync(token))).Http
            ?? throw new InvalidOperationException("The actual HTTP admission snapshot is absent.");
}
