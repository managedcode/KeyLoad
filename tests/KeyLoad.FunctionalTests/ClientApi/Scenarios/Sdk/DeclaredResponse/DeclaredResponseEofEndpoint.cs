using System.Text.Json;
using KeyLoad.FunctionalTests.OrleansExecution;
using Microsoft.AspNetCore.Http;
using static KeyLoad.FunctionalTests.ClientApi.DeclaredResponseEofProtocol;
using P = KeyLoad.FunctionalTests.ClientApi.DeclaredResponseEofProtocol;

namespace KeyLoad.FunctionalTests.ClientApi;

internal sealed class DeclaredResponseEofEndpoint(ConnectionScenario scenario, PrincipalRecord principal,
    DeclaredResponseEofTasks tasks)
{
    private int writes;
    private int reads;
    internal CommandRequest Command
    {
        get;
    } = scenario.Command(Document, OriginalJson, AbsentRevision);
    internal CommitReceipt? Original
    {
        get;
        private set;
    }
    internal DeclaredResponseEofBoundary Write
    {
        get;
    } = new();
    internal DeclaredResponseEofBoundary Read
    {
        get;
    } = new();

    internal Task HandleAsync(HttpContext context)
    {
        var command = context.Request.Path == Commands;
        var first = command ? Interlocked.Increment(ref writes) == FirstRequest
            : Interlocked.Increment(ref reads) == FirstRequest;
        var boundary = first ? command ? Write : Read : null;
        var actual = tasks.Retain(HandleCoreAsync(context, command, boundary));
        boundary?.Retained.TrySetResult(actual);
        return actual;
    }

    private async Task HandleCoreAsync(HttpContext context, bool command, DeclaredResponseEofBoundary? boundary)
    {
        try
        {
            RequireCredential(context);
            var bytes = command ? await CommandAsync(context) : await DocumentAsync(context);
            context.Response.ContentType = ContentType;
            context.Response.ContentLength = bytes.Length;
            if (boundary is null)
            {
                await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
            }
            else
            {
                await AbortDeclaredAsync(context, bytes, boundary);
            }
        }
        catch (OperationCanceledException) when (boundary is not null && boundary.Flushed.Task.IsCompletedSuccessfully
            && context.RequestAborted.IsCancellationRequested)
        {
        }
    }

    internal async Task RequireCountsAsync()
    {
        await Assert.That(writes).IsEqualTo(RequestsPerRoute);
        await Assert.That(reads).IsEqualTo(RequestsPerRoute);
    }

    private void RequireCredential(HttpContext context)
    {
        if (context.Request.Headers[P.Authorization].SingleOrDefault() != Bearer + ApiKey
            || scenario.Fixture.Database.Database.Authenticate(ApiKey, scenario.Fixture.Clock.GetUtcNow()) != principal.Id)
        {
            throw new InvalidOperationException(Failure);
        }
    }

    private async Task<byte[]> CommandAsync(HttpContext context)
    {
        var actual = await JsonSerializer.DeserializeAsync<CommandRequest>(context.Request.Body,
            JsonDefaults.Options, context.RequestAborted);
        await DeclaredResponseEofAssertions.EqualAsync(Command, actual);
        if (context.Request.Headers[CommandHeader].SingleOrDefault() != Command.CommandId.ToString())
        {
            throw new InvalidOperationException(Failure);
        }
        var requestId = Guid.NewGuid();
        var reply = await scenario.CommandAsync(principal, requestId, Command, context.RequestAborted);
        await scenario.Observation.Read(requestId).Disposed.Task.WaitAsync(context.RequestAborted);
        await Assert.That(reply.Error).IsNull();
        var receipt = ConnectionAssertions.Value<CommitReceipt>(reply);
        Original ??= receipt;
        return JsonSerializer.SerializeToUtf8Bytes(receipt, JsonDefaults.Options);
    }

    private async Task<byte[]> DocumentAsync(HttpContext context)
    {
        await Assert.That(context.Request.Path.Value).IsEqualTo(Documents);
        var actual = await JsonSerializer.DeserializeAsync<GetDocumentRequest>(context.Request.Body,
            JsonDefaults.Options, context.RequestAborted);
        await DeclaredResponseEofAssertions.EqualAsync(new GetDocumentRequest(scenario.Reference(Document)), actual);
        var requestId = Guid.NewGuid();
        var reply = await scenario.ReadAsync(principal, requestId, Document, context.RequestAborted);
        await scenario.Observation.Read(requestId).Disposed.Task.WaitAsync(context.RequestAborted);
        await Assert.That(reply.Error).IsNull();
        return JsonSerializer.SerializeToUtf8Bytes(new DocumentReadResult(
            ConnectionAssertions.Value<DocumentResult>(reply)), JsonDefaults.Options);
    }

    private static async Task AbortDeclaredAsync(HttpContext context, byte[] bytes, DeclaredResponseEofBoundary boundary)
    {
        using var registration = context.RequestAborted.Register(() => boundary.Aborted.TrySetResult());
        await Assert.That(bytes.Length).IsGreaterThan(PrefixBytes);
        await context.Response.Body.WriteAsync(bytes.AsMemory(Empty, PrefixBytes), context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
        boundary.Flushed.TrySetResult();
        await boundary.Release.Task.WaitAsync(context.RequestAborted);
        context.Abort();
        await boundary.Aborted.Task;
    }
}
