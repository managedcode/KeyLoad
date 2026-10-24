using KeyLoad.Client;

namespace KeyLoad.UnitTests.ClientApi;

internal static class KeyLoadClientNullWriteReadAssertions
{
    internal static async Task VerifyRequestsAndReadsAsync(KeyLoadClient client, CommandRequest command,
        List<CommandRequest> requests, List<string?> headers, EntityRef reference, DocumentResult document, CancellationToken cancellationToken)
    {
        await Assert.That(requests.Count).IsEqualTo(2);
        await Assert.That(headers.Count).IsEqualTo(2);
        foreach (var request in requests)
        {
            await Assert.That(JsonDefaults.Serialize(request).SequenceEqual(JsonDefaults.Serialize(command))).IsTrue();
        }
        foreach (var header in headers)
        {
            await Assert.That(header).IsEqualTo(command.CommandId.ToString());
        }
        var absent = await client.GetAsync(reference, cancellationToken);
        if (absent.IsFailed)
        {
            throw new InvalidOperationException(absent.Problem.Detail);
        }
        await Assert.That(absent.Value.Document).IsNull();
        var healthy = await client.GetAsync(reference, cancellationToken);
        if (healthy.IsFailed)
        {
            throw new InvalidOperationException(healthy.Problem.Detail);
        }
        await Assert.That(healthy.Value.Document).IsNotNull();
        await Assert.That(OrleansSerialization.Serialize(healthy.Value.Document).SequenceEqual(OrleansSerialization.Serialize(document))).IsTrue();
    }
}
