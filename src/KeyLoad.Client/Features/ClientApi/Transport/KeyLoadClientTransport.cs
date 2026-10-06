using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.Communication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Client.Features.ClientApi;

/// <summary>Owns bounded HTTP serialization, response mapping and transport failure classification.</summary>
internal sealed class KeyLoadClientTransport
{
    private const string CommandIdHeader = "X-KeyLoad-Command-Id";
    private readonly HttpClient http;
    private readonly string apiKey;
    private readonly KeyLoadClientExecutionOptions execution;

    internal KeyLoadClientTransport(HttpClient http, string apiKey, IOptions<KeyLoadClientExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(executionOptions);
        execution = executionOptions.Value;
        execution.Validate();
        this.http = http;
        this.apiKey = apiKey;
    }

    internal KeyLoadClientExecutionOptions ExecutionOptions => execution;

    internal async Task<Result<T>> Send<T>(string path, object? request, bool write, Guid? id, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(request is null ? HttpMethod.Get : HttpMethod.Post, path);
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
                    execution.MaximumProblemBodyBytes, cancellationToken).ConfigureAwait(false);
                return problem ?? Errors.Problem(write ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost,
                    ClientTransportMessages.ServerResponseUnavailable);
            }
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync<T>(body, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
            return Result<T>.Succeed(value!);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException or JsonException)
        {
            return Errors.Problem(write ? ErrorCode.UnknownWriteOutcome : cancellationToken.IsCancellationRequested
                ? ErrorCode.Cancelled : ErrorCode.OwnershipLost,
                write ? ClientTransportMessages.WriteResponseUnavailable : ClientTransportMessages.ReadResponseUnavailable);
        }
    }
}
