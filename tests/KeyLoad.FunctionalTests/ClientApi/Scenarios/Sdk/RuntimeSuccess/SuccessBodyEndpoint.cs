using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using KeyLoad.FunctionalTests.OrleansExecution;
using Microsoft.AspNetCore.Http;
using D = KeyLoad.FunctionalTests.ClientApi.DeclaredResponseEofProtocol;
using P = KeyLoad.FunctionalTests.ClientApi.SuccessBodyProtocol;

namespace KeyLoad.FunctionalTests.ClientApi;

internal sealed class SuccessBodyEndpoint(ConnectionScenario scenario, PrincipalRecord principal,
    DeclaredResponseEofTasks tasks, string unusable, bool appendPrefix, bool declareLength, int maximumResponseBytes)
{
    private int count;
    private readonly TaskCompletionSource<Task>[] handlers = Enumerable.Range(P.FirstOrdinal, P.RequestCount)
        .Select(_ => new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
    private readonly ConcurrentDictionary<SuccessBodyStep, SuccessBodyDelivery> completed = new();
    internal CommandRequest Original
    {
        get;
    } = scenario.Command(D.Document, D.OriginalJson, D.AbsentRevision);
    internal CommandRequest Healthy
    {
        get;
    } = scenario.Command(D.Document, P.HealthyJson, D.Revision);
    internal CommandRequest Changed => Original with
    {
        Mutations = [new PutDocument(ConnectionProtocol.Collection, D.Document, P.ChangedJson, D.Revision)]
    };
    internal CommitReceipt? OriginalReceipt
    {
        get;
        private set;
    }
    internal CommitReceipt? HealthyReceipt
    {
        get;
        private set;
    }

    internal Task HandleAsync(HttpContext context)
    {
        var ordinal = Interlocked.Increment(ref count);
        if (ordinal > P.RequestCount)
        {
            throw new InvalidOperationException(P.Failure);
        }
        var actual = tasks.Retain(HandleCoreAsync(context, (SuccessBodyStep)ordinal));
        handlers[ordinal - P.FirstStep].TrySetResult(actual);
        return actual;
    }

    internal Task<Task> HandlerAsync(SuccessBodyStep step, CancellationToken token)
        => handlers[(int)step - P.FirstStep].Task.WaitAsync(token);

    internal async Task RequireDeliveryAsync(SuccessBodyStep step)
    {
        await Assert.That(completed.TryGetValue(step, out var delivery)).IsTrue();
        var actual = delivery ?? throw new InvalidOperationException(P.Failure);
        await Assert.That(actual.Command).IsEqualTo(IsCommand(step));
        await Assert.That(actual.Status).IsEqualTo(step == SuccessBodyStep.Conflict
            ? StatusCodes.Status409Conflict : StatusCodes.Status200OK);
        await Assert.That(actual.DeclaredBytes).IsEqualTo(declareLength ? (long?)actual.WrittenBytes : null);
        await Assert.That(actual.WrittenBytes).IsGreaterThan(D.Empty);
        if (step is SuccessBodyStep.OriginalWrite or SuccessBodyStep.RefusedRead)
        {
            var prefixBytes = appendPrefix ? PrefixBytes(step) : D.Empty;
            await Assert.That(actual.WrittenBytes).IsEqualTo(checked(prefixBytes + Encoding.UTF8.GetByteCount(unusable)));
            if (appendPrefix)
            {
                await Assert.That(actual.WrittenBytes).IsLessThan(maximumResponseBytes);
            }
        }
    }

    internal async Task RequireCountsAsync()
    {
        await Assert.That(count).IsEqualTo(P.RequestCount);
        await Assert.That(completed.Count).IsEqualTo(P.RequestCount);
    }

    private async Task HandleCoreAsync(HttpContext context, SuccessBodyStep step)
    {
        if (context.Request.Headers[D.Authorization].SingleOrDefault() != D.Bearer + P.ApiKey
            || scenario.Fixture.Database.Database.Authenticate(P.ApiKey, scenario.Fixture.Clock.GetUtcNow()) != principal.Id)
        {
            throw new InvalidOperationException(P.Failure);
        }
        var command = IsCommand(step);
        await Assert.That(context.Request.Path.Value).IsEqualTo(command ? D.Commands : D.Documents);
        var body = command ? await CommandAsync(context, step) : await DocumentAsync(context, step);
        context.Response.ContentType = D.ContentType;
        context.Response.ContentLength = declareLength ? body.Length : null;
        await context.Response.Body.WriteAsync(body, context.RequestAborted);
        await context.Response.CompleteAsync();
        if (!completed.TryAdd(step, new(command, context.Response.StatusCode,
            context.Response.ContentLength, body.Length)))
        {
            throw new InvalidOperationException(P.Failure);
        }
    }

    private async Task<byte[]> CommandAsync(HttpContext context, SuccessBodyStep step)
    {
        var actual = await JsonSerializer.DeserializeAsync<CommandRequest>(context.Request.Body,
            JsonDefaults.Options, context.RequestAborted) ?? throw new InvalidOperationException(P.Failure);
        var expected = step switch
        {
            SuccessBodyStep.OriginalWrite or SuccessBodyStep.OriginalReplay => Original,
            SuccessBodyStep.Conflict => Changed,
            SuccessBodyStep.HealthyWrite or SuccessBodyStep.HealthyReplay => Healthy,
            _ => throw new InvalidOperationException(P.Failure)
        };
        await DeclaredResponseEofAssertions.EqualAsync(expected, actual);
        await Assert.That(context.Request.Headers[D.CommandHeader].SingleOrDefault()).IsEqualTo(actual.CommandId.ToString());
        var requestId = Guid.NewGuid();
        var reply = await scenario.CommandAsync(principal, requestId, actual, context.RequestAborted);
        await scenario.Observation.Read(requestId).Disposed.Task.WaitAsync(context.RequestAborted);
        if (reply.Error is { } error)
        {
            context.Response.StatusCode = Errors.Status(error);
            return JsonSerializer.SerializeToUtf8Bytes(Errors.Problem(error,
                reply.SafeDetail ?? throw new InvalidOperationException(P.Failure)), JsonDefaults.Options);
        }
        var receipt = ConnectionAssertions.Value<CommitReceipt>(reply);
        if (step == SuccessBodyStep.OriginalWrite)
        {
            OriginalReceipt = receipt;
            return SuccessBodyResponse.Compose(receipt, unusable, appendPrefix, maximumResponseBytes);
        }
        if (step == SuccessBodyStep.HealthyWrite)
        {
            HealthyReceipt = receipt;
        }
        return JsonSerializer.SerializeToUtf8Bytes(receipt, JsonDefaults.Options);
    }

    private async Task<byte[]> DocumentAsync(HttpContext context, SuccessBodyStep step)
    {
        var actual = await JsonSerializer.DeserializeAsync<GetDocumentRequest>(context.Request.Body,
            JsonDefaults.Options, context.RequestAborted);
        await DeclaredResponseEofAssertions.EqualAsync(new GetDocumentRequest(scenario.Reference(D.Document)), actual);
        var requestId = Guid.NewGuid();
        var reply = await scenario.ReadAsync(principal, requestId, D.Document, context.RequestAborted);
        await scenario.Observation.Read(requestId).Disposed.Task.WaitAsync(context.RequestAborted);
        await Assert.That(reply.Error).IsNull();
        if (step == SuccessBodyStep.RefusedRead && !appendPrefix)
        {
            return Encoding.UTF8.GetBytes(unusable);
        }
        var value = new DocumentReadResult(ConnectionAssertions.Value<DocumentResult>(reply));
        return step == SuccessBodyStep.RefusedRead ? SuccessBodyResponse.Compose(value, unusable, appendPrefix, maximumResponseBytes)
            : JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Options);
    }

    private int PrefixBytes(SuccessBodyStep step)
        => step == SuccessBodyStep.OriginalWrite
            ? JsonSerializer.SerializeToUtf8Bytes(OriginalReceipt ?? throw new InvalidOperationException(P.Failure),
                JsonDefaults.Options).Length
            : JsonSerializer.SerializeToUtf8Bytes(new DocumentReadResult(new(scenario.Reference(D.Document),
                D.Revision, D.OriginalJson, false, [])), JsonDefaults.Options).Length;

    private static bool IsCommand(SuccessBodyStep step)
        => step is SuccessBodyStep.OriginalWrite or SuccessBodyStep.OriginalReplay
            or SuccessBodyStep.Conflict or SuccessBodyStep.HealthyWrite or SuccessBodyStep.HealthyReplay;
}
