using System.Net;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.DocumentStorage;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.Authorization;

internal static class CredentialVerifierR53Refusals
{
    internal static async Task SdkAsync(KeyLoadClient oldReader, CredentialSubjectReplacementState state,
        CredentialVerifierR53State verifier, CancellationToken token)
    {
        var refused = await oldReader.GetAsync(state.First.Scenario.Reference, token);
        await PrivateAsync(JsonSerializer.Serialize(refused, JsonDefaults.Options), state, verifier);
        await Assert.That(refused.IsSuccess).IsFalse();
        await Assert.That(refused.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.Unauthenticated,
            CredentialVerifierR53Protocol.UnavailableDetail), refused.Problem);
    }

    internal static async Task LiveAsync(McpOfficialClient session, CredentialSubjectReplacementState state,
        CredentialVerifierR53State verifier, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => session.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(state.First.Scenario.Reference), token), failures);
        await UnauthorizedAsync(failures, state, verifier);
    }

    internal static async Task InitializeAsync(ClusterFixture fixture, CredentialSubjectReplacementState state,
        CredentialVerifierR53State verifier, CancellationToken token)
    {
        var failures = new List<Exception>();
        Task<McpOfficialClient>? original = null;
        await ServerFailureObserver.ObserveAsync(() => original = McpOfficialClient.ConnectAsync(fixture,
            McpCallerProtocol.Node2, state.First.Identity.Secret, token), failures);
        if (original is { IsCompletedSuccessfully: true })
        {
            failures.Add(new InvalidOperationException(CredentialVerifierR53Protocol.UnexpectedAdmission));
            var unexpected = await original;
            await ServerFailureObserver.ObserveAsync(() => unexpected.DisposeAsync().AsTask(), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        await UnauthorizedAsync(failures, state, verifier);
    }

    private static async Task UnauthorizedAsync(List<Exception> failures, CredentialSubjectReplacementState state,
        CredentialVerifierR53State verifier)
    {
        if (failures.Count == CredentialVerifierR53Protocol.ExpectedUnauthorizedErrors
            && failures[CredentialVerifierR53Protocol.FirstError] is HttpRequestException
            { StatusCode: HttpStatusCode.Unauthorized } unauthorized)
        {
            await PrivateAsync(unauthorized.Message, state, verifier);
            return;
        }
        if (failures.Count == CredentialVerifierR53Protocol.FirstError)
        {
            failures.Add(new InvalidOperationException(CredentialVerifierR53Protocol.UnexpectedAdmission));
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task PrivateAsync(string text, CredentialSubjectReplacementState state,
        CredentialVerifierR53State verifier)
    {
        await Assert.That(text.Contains(state.First.Identity.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(state.Second.Identity.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(verifier.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsFalse();
    }
}
