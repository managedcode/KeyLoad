using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ConnectionRf3OverlapTests
{
    [Test]
    public Task AcCrs058OneHttp2ConnectionCompletesIndependentCommandAndCancelsOnlySelectedOperation()
        => ConnectionRf3Scenario.RunAsync(ExecuteAsync, TestContext.Current!.Execution.CancellationToken);

    private static async Task ExecuteAsync(ConnectionRf3Scenario scenario, CancellationToken token)
    {
        var caller = await scenario.CallerAsync(token, multiplexed: true);
        using var originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var firstCommand = RequestCqrsPhaseFaultProvisioning.UpdateCommand(scenario.Identity, Guid.NewGuid());
        var held = await scenario.StartHeldAsync(scenario.Identity, firstCommand.CommandId, null,
            () => caller.Sdk.CommitAsync(firstCommand, originalCancellation.Token), token);
        var independent = caller.WithCredential(scenario.SecondIdentity.Secret);
        var secondCommand = RequestCqrsPhaseFaultProvisioning.UpdateCommand(scenario.SecondIdentity, Guid.NewGuid());
        var completed = await scenario.ObserveAsync(scenario.SecondIdentity, secondCommand.CommandId, null,
            () => independent.CommitAsync(secondCommand, token), token);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(completed.Result);
        await ConnectionRf3PublicOperations.ReceiptAsync(new(receipt, null), secondCommand, completed.Witness);
        await ConnectionRf3WitnessReader.SameOwnerAsync(held.Witness, completed.Witness);
        await Assert.That(held.Original.IsCompleted).IsFalse();
        var reference = ConnectionRf3PublicOperations.Reference(scenario.SecondIdentity);
        var read = await scenario.ObserveAsync(scenario.SecondIdentity, Guid.Empty, GrainReadKind.Document,
            () => independent.GetAsync(reference, token), token);
        var document = await McpCallerAssertions.SdkSuccessAsync(read.Result);
        await ConnectionRf3PublicOperations.DocumentAsync(new(document, null), scenario.SecondIdentity, read.Witness,
            RequestCqrsRf3Protocol.ChangedDocumentJson, ConnectionRf3Protocol.UpdatedRevision);
        await ConnectionRf3WitnessReader.SameOwnerAsync(held.Witness, read.Witness);
        await Assert.That(held.Original.IsCompleted).IsFalse();
        await originalCancellation.CancelAsync();
        await RequestCqrsFaultOutcome.AssertSdkUnknownWriteAsync(await held.Original);
        await scenario.SettleAsync(held.Arm, held.Marker, token);
        await scenario.VerifyDocumentAsync(scenario.Identity, RequestCqrsRf3Protocol.DocumentJson,
            ConnectionRf3Protocol.InitialRevision, token);
        await scenario.VerifyDocumentAsync(scenario.SecondIdentity, RequestCqrsRf3Protocol.ChangedDocumentJson,
            ConnectionRf3Protocol.UpdatedRevision, token);
        await ConnectionRf3IdleContinuation.VerifyAsync(scenario, caller, independent, held.Witness, held.Marker, token);
    }
}
