using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.Client.Features.ClientApi;

/// <summary>Owns bounded HTTP serialization, response mapping and transport failure classification.</summary>
internal sealed class KeyLoadClientTransport
{
    private const string CommandIdHeader = "X-KeyLoad-Command-Id";
    private readonly HttpClient http;
    private readonly string apiKey;
    private readonly IOptions<KeyLoadClientExecutionOptions> execution;
    private readonly CqrsStreamClientOptions? streaming;

    internal KeyLoadClientTransport(HttpClient http, string apiKey, IOptions<KeyLoadClientExecutionOptions> executionOptions)
        : this(http, apiKey, executionOptions, null) { }

    internal KeyLoadClientTransport(HttpClient http, string apiKey, IOptions<KeyLoadClientExecutionOptions> executionOptions,
        IOptions<CqrsStreamClientOptions>? streamingOptions)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(executionOptions);
        execution = KeyLoadClientExecutionOptions.Snapshot(executionOptions);
        this.http = http;
        this.apiKey = apiKey;
        streaming = streamingOptions is null ? null : KeyLoadClientStreamPolicy.Snapshot(streamingOptions);
    }

    internal KeyLoadClientExecutionOptions ExecutionOptions => execution.Value;

    internal IAsyncEnumerable<CqrsStreamChunk<EventFeedTailProgress, EventFeedControlResult>> TailEventFeedAsync(
        EventFeedControlRequest request, CancellationToken cancellationToken)
    {
        var policy = streaming ?? throw new OptionsValidationException(KeyLoadClientStreamPolicy.SectionName,
            typeof(CqrsStreamClientOptions), [KeyLoadClientStreamPolicy.Invalid]);
        if (request.Action != EventFeedControlAction.Tail || request.CommandId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Validation, KeyLoadClientStreamPolicy.Invalid); }
        cancellationToken.ThrowIfCancellationRequested();
        return http.SendForCqrsStreamAsync<EventFeedTailProgress, EventFeedControlResult>(
            CreateMessage, policy, cancellationToken);

        HttpRequestMessage CreateMessage()
        {
            var authorization = new AuthenticationHeaderValue(ClientTransportMessages.BearerScheme, apiKey);
            var message = new HttpRequestMessage(HttpMethod.Post, EventFeedTailClientProtocol.Route)
            {
                Version = http.DefaultRequestVersion,
                VersionPolicy = http.DefaultVersionPolicy,
                Content = JsonContent.Create(request, options: JsonDefaults.Options)
            };
            message.Headers.Authorization = authorization;
            message.Headers.Add(CommandIdHeader, request.CommandId.ToString());
            return message;
        }
    }

    internal Task<Result<T>> Send<T>(string path, object? request, bool write, Guid? id, CancellationToken cancellationToken,
        HttpMethod? method = null)
        => SendCore<T>(path, request, write, id, method ?? (request is null ? HttpMethod.Get : HttpMethod.Post), cancellationToken);

    internal async Task<Result> SendStatus(string path, object request, Guid id, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Version = http.DefaultRequestVersion,
            VersionPolicy = http.DefaultVersionPolicy
        };
        message.Headers.Authorization = new AuthenticationHeaderValue(ClientTransportMessages.BearerScheme, apiKey);
        message.Headers.Add(CommandIdHeader, id.ToString());
        message.Content = JsonContent.Create(request, request.GetType(), options: JsonDefaults.Options);
        try
        {
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var problem = await BoundedProblemReader.ReadAsync(response.Content, JsonDefaults.Options,
                    execution.Value.MaximumProblemBodyBytes, cancellationToken).ConfigureAwait(false);
                return Result.Fail(problem ?? Errors.Problem(ErrorCode.UnknownWriteOutcome,
                    ClientTransportMessages.ServerResponseUnavailable));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return response.StatusCode == global::System.Net.HttpStatusCode.NoContent
                ? Result.Succeed()
                : Result.Fail(Errors.Problem(ErrorCode.UnknownWriteOutcome, ClientTransportMessages.WriteResponseUnavailable));
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException or JsonException)
        {
            return Result.Fail(Errors.Problem(ErrorCode.UnknownWriteOutcome, ClientTransportMessages.WriteResponseUnavailable));
        }
    }

    private async Task<Result<T>> SendCore<T>(string path, object? request, bool write, Guid? id,
        HttpMethod method, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(method, path)
        {
            Version = http.DefaultRequestVersion,
            VersionPolicy = http.DefaultVersionPolicy
        };
        message.Headers.Authorization = new AuthenticationHeaderValue(ClientTransportMessages.BearerScheme, apiKey);
        if (id is { } command)
        {
            message.Headers.Add(CommandIdHeader, command.ToString());
        }
        if (request is not null)
        {
            message.Content = JsonContent.Create(request, request.GetType(), options: JsonDefaults.Options);
        }
        try
        {
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var problem = await BoundedProblemReader.ReadAsync(response.Content, JsonDefaults.Options,
                    execution.Value.MaximumProblemBodyBytes, cancellationToken).ConfigureAwait(false);
                return problem ?? Errors.Problem(write ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost,
                    ClientTransportMessages.ServerResponseUnavailable);
            }
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync<T>(body, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
            if (value is null)
            {
                return Errors.Problem(write ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost,
                    write ? ClientTransportMessages.WriteResponseUnavailable : ClientTransportMessages.ReadResponseUnavailable);
            }
            return Result<T>.Succeed(value);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException or JsonException)
        {
            return Errors.Problem(write ? ErrorCode.UnknownWriteOutcome : cancellationToken.IsCancellationRequested
                ? ErrorCode.Cancelled : ErrorCode.OwnershipLost,
                write ? ClientTransportMessages.WriteResponseUnavailable : ClientTransportMessages.ReadResponseUnavailable);
        }
    }
}
