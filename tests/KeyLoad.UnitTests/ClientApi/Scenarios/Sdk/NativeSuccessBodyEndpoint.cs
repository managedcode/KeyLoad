using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.AspNetCore.Http;
using D = KeyLoad.UnitTests.Features.ClientApi.DeclaredResponseEofProtocol;
using P = KeyLoad.UnitTests.Features.ClientApi.NativeSuccessBodyProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class NativeSuccessBodyEndpoint(ConnectionNativeScenario scenario, PrincipalRecord principal,
    DeclaredResponseEofTasks tasks, string unusable, bool appendNativePrefix, bool declareLength, int maximumResponseBytes)
{
    private int count;
    private readonly TaskCompletionSource<Task>[] handlers = Enumerable.Range(P.FirstOrdinal, P.RequestCount)
        .Select(_ => new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
    private readonly ConcurrentDictionary<NativeSuccessBodyStep, NativeSuccessBodyDelivery> completed = new();
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
        Mutations = [new PutDocument(ConnectionNativeProtocol.Collection, D.Document, P.ChangedJson, D.Revision)]
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
        var actual = tasks.Retain(HandleCoreAsync(context, (NativeSuccessBodyStep)ordinal));
        handlers[ordinal - P.FirstStep].TrySetResult(actual);
        return actual;
    }

    internal Task<Task> HandlerAsync(NativeSuccessBodyStep step, CancellationToken token)
        => handlers[(int)step - P.FirstStep].Task.WaitAsync(token);

    internal async Task RequireDeliveryAsync(NativeSuccessBodyStep step)
    {
        await Assert.That(completed.TryGetValue(step, out var delivery)).IsTrue();
        var actual = delivery ?? throw new InvalidOperationException(P.Failure);
        await Assert.That(actual.Command).IsEqualTo(IsCommand(step));
        await Assert.That(actual.Status).IsEqualTo(step == NativeSuccessBodyStep.Conflict
            ? StatusCodes.Status409Conflict : StatusCodes.Status200OK);
        await Assert.That(actual.DeclaredBytes).IsEqualTo(declareLength ? (long?)actual.WrittenBytes : null);
        await Assert.That(actual.WrittenBytes).IsGreaterThan(D.Empty);
        if (step is NativeSuccessBodyStep.OriginalWrite or NativeSuccessBodyStep.RefusedRead)
        {
            var prefixBytes = appendNativePrefix ? PrefixBytes(step) : D.Empty;
            await Assert.That(actual.WrittenBytes).IsEqualTo(checked(prefixBytes + Encoding.UTF8.GetByteCount(unusable)));
            if (appendNativePrefix)
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

    private async Task HandleCoreAsync(HttpContext context, NativeSuccessBodyStep step)
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

    private async Task<byte[]> CommandAsync(HttpContext context, NativeSuccessBodyStep step)
    {
        var actual = await JsonSerializer.DeserializeAsync<CommandRequest>(context.Request.Body,
            JsonDefaults.Options, context.RequestAborted) ?? throw new InvalidOperationException(P.Failure);
        var expected = step switch
        {
            NativeSuccessBodyStep.OriginalWrite or NativeSuccessBodyStep.OriginalReplay => Original,
            NativeSuccessBodyStep.Conflict => Changed,
            NativeSuccessBodyStep.HealthyWrite or NativeSuccessBodyStep.HealthyReplay => Healthy,
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
        var receipt = ConnectionNativeAssertions.Value<CommitReceipt>(reply);
        if (step == NativeSuccessBodyStep.OriginalWrite)
        {
            OriginalReceipt = receipt;
            return NativeSuccessBodyResponse.Compose(receipt, unusable, appendNativePrefix, maximumResponseBytes);
        }
        if (step == NativeSuccessBodyStep.HealthyWrite)
        {
            HealthyReceipt = receipt;
        }
        return JsonSerializer.SerializeToUtf8Bytes(receipt, JsonDefaults.Options);
    }

    private async Task<byte[]> DocumentAsync(HttpContext context, NativeSuccessBodyStep step)
    {
        var actual = await JsonSerializer.DeserializeAsync<GetDocumentRequest>(context.Request.Body,
            JsonDefaults.Options, context.RequestAborted);
        await DeclaredResponseEofAssertions.EqualAsync(new GetDocumentRequest(scenario.Reference(D.Document)), actual);
        var requestId = Guid.NewGuid();
        var reply = await scenario.ReadAsync(principal, requestId, D.Document, context.RequestAborted);
        await scenario.Observation.Read(requestId).Disposed.Task.WaitAsync(context.RequestAborted);
        await Assert.That(reply.Error).IsNull();
        if (step == NativeSuccessBodyStep.RefusedRead && !appendNativePrefix)
        {
            return Encoding.UTF8.GetBytes(unusable);
        }
        var value = new DocumentReadResult(ConnectionNativeAssertions.Value<DocumentResult>(reply));
        return step == NativeSuccessBodyStep.RefusedRead ? NativeSuccessBodyResponse.Compose(value, unusable, appendNativePrefix, maximumResponseBytes)
            : JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Options);
    }

    private int PrefixBytes(NativeSuccessBodyStep step)
        => step == NativeSuccessBodyStep.OriginalWrite
            ? JsonSerializer.SerializeToUtf8Bytes(OriginalReceipt ?? throw new InvalidOperationException(P.Failure),
                JsonDefaults.Options).Length
            : JsonSerializer.SerializeToUtf8Bytes(new DocumentReadResult(new(scenario.Reference(D.Document),
                D.Revision, D.OriginalJson, false, [])), JsonDefaults.Options).Length;

    private static bool IsCommand(NativeSuccessBodyStep step)
        => step is NativeSuccessBodyStep.OriginalWrite or NativeSuccessBodyStep.OriginalReplay
            or NativeSuccessBodyStep.Conflict or NativeSuccessBodyStep.HealthyWrite or NativeSuccessBodyStep.HealthyReplay;
}
