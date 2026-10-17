namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsAuthorityRevokedCallerAssertions
{
    private const string MissingOwner = "The authority-fault wave did not retain its owner.";

    internal static async Task RequireAsync(RequestCqrsAuthorityFaultIdentity? identity,
        Func<RequestCqrsRf3Callers?> readCaller, CancellationToken cancellationToken)
    {
        var activeIdentity = identity ?? throw new InvalidOperationException(MissingOwner);
        var reference = new EntityRef(activeIdentity.Partition, RequestCqrsRf3Protocol.AdminCollection,
            RequestCqrsRf3Protocol.DocumentId);
        var result = await (readCaller() ?? throw new InvalidOperationException(MissingOwner)).Sdk.GetAsync(reference,
            cancellationToken).ConfigureAwait(false);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(ErrorCode.Unauthenticated.ToString());
        await RequestCqrsAuthorityFaultAssertions.VerifyMcpCredentialRejectedAsync(
            readCaller()!.Mcp, activeIdentity.Secret,
            reference, cancellationToken).ConfigureAwait(false);
    }

}
