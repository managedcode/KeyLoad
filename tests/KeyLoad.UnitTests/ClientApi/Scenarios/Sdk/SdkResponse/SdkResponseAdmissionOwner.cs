using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using static KeyLoad.UnitTests.Features.ClientApi.SdkResponseAdmissionProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class SdkResponseAdmissionOwner(TestDatabase database, bool contentLength)
{
    internal CommandRequest Command
    {
        get;
    } = new(Guid.NewGuid(), database.Partition,
        [new PutDocument(Collection, Document, SdkResponseAdmissionProtocol.Json, InitialRevision)]);
    internal CommitToken ExpectedToken
    {
        get;
    } = CreateExpectedToken(database);

    internal CommitReceipt Expected => new(Command.CommandId, ExpectedToken, [new(PutKind, Collection, Document, CommittedRevision)], DurabilityProfile.ProcessDurable, 1);

    internal CommitReceipt? Original
    {
        get;
        private set;
    }

    internal ConcurrentQueue<Task> Requests
    {
        get;
    } = new();

    internal Task HandleAsync(HttpContext context)
    {
        var original = HandleCoreAsync(context);
        Requests.Enqueue(original);
        return original;
    }

    private static CommitToken CreateExpectedToken(TestDatabase database)
        => database.NextCommitToken();

    private async Task HandleCoreAsync(HttpContext context)
    {
        byte[] bytes;
        if (context.Request.Path == Commands)
        {
            var actual = await JsonSerializer.DeserializeAsync<CommandRequest>(context.Request.Body,
                JsonDefaults.Options, context.RequestAborted);
            await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(Command))).IsTrue();
            await Assert.That(context.Request.Headers[CommandHeader].SingleOrDefault()).IsEqualTo(Command.CommandId.ToString());
            var receipt = database.Submit(OperationKind.Batch, actual!, id: actual!.CommandId).Get<CommitReceipt>();
            Original ??= receipt;
            bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, JsonDefaults.Options);
        }
        else
        {
            await Assert.That(context.Request.Path.Value).IsEqualTo(Documents);
            var actual = await JsonSerializer.DeserializeAsync<GetDocumentRequest>(context.Request.Body,
                JsonDefaults.Options, context.RequestAborted);
            var reference = new EntityRef(database.Partition, Collection, Document);
            await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(new GetDocumentRequest(reference)))).IsTrue();
            bytes = JsonSerializer.SerializeToUtf8Bytes(new DocumentReadResult(database.Database.GetDocument(Root, reference)),
                JsonDefaults.Options);
        }
        context.Response.ContentType = JsonType;
        if (contentLength)
        {
            context.Response.ContentLength = bytes.Length;
        }
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
    }
}
