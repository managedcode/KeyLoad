using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.Communication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Client.ClientApi;

/// <summary>Owns bounded HTTP serialization, response mapping and transport failure classification.</summary>
internal sealed class KeyLoadClientTransport
{
    private const string CommandIdHeader = "X-KeyLoad-Command-Id";
    private readonly HttpClient http;
    private readonly string apiKey;
    private readonly IOptions<KeyLoadClientExecutionOptions> execution;
    internal KeyLoadClientTransport(HttpClient http, string apiKey, IOptions<KeyLoadClientExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(executionOptions);
        execution = KeyLoadClientExecutionOptions.Snapshot(executionOptions);
        this.http = http;
        this.apiKey = apiKey;
    }

    internal KeyLoadClientExecutionOptions ExecutionOptions => execution.Value;

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
                var problem = (await BoundedProblemReader.ReadAsync(response.Content, JsonDefaults.Options,
                    execution.Value.MaximumProblemBodyBytes, cancellationToken).ConfigureAwait(false)).Value;
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
                var problem = (await BoundedProblemReader.ReadAsync(response.Content, JsonDefaults.Options,
                    execution.Value.MaximumProblemBodyBytes, cancellationToken).ConfigureAwait(false)).Value;
                return problem ?? Errors.Problem(write ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost,
                    ClientTransportMessages.ServerResponseUnavailable);
            }
            var maximumBytes = execution.Value.MaximumResponseBodyBytes;
            BoundedResponseStream.RequireLength(response.Content, maximumBytes);
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var admitted = new BoundedResponseStream(body, maximumBytes);
            var value = await JsonSerializer.DeserializeAsync<T>(admitted, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
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
