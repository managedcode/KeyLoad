using System.Text.Json;
using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal sealed class ServerErrorMiddleware(RequestDelegate next, ILogger<ServerErrorMiddleware> logger, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private const string UnexpectedFailure = "The server could not complete the database operation.";

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (KeyLoadException error) when (!context.Response.HasStarted)
        {
            await WriteAsync(context, error.Code, error.Message);
        }
        catch (JsonException) when (!context.Response.HasStarted)
        {
            await WriteAsync(context, ErrorCode.Validation, ServerProtocol.InvalidJson);
        }
        catch (BadHttpRequestException error) when (!context.Response.HasStarted)
        {
            var large = error.StatusCode == StatusCodes.Status413PayloadTooLarge;
            await WriteAsync(context, large ? ErrorCode.ResourceExhausted : ErrorCode.Validation,
                large ? ServerProtocol.BodyExceeded : ServerProtocol.InvalidJson);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The caller owns this cancellation; a committed command is resolved by its stable retry ID.
        }
        catch (Exception error) when (!context.Response.HasStarted && CqrsBoundaryErrors.IsNonFatal(error))
        {
            RequestFailureDiagnostic.LogFailure(logger, context, error, time);
            await WriteAsync(context, ErrorCode.RecoveryRequired, UnexpectedFailure);
        }
    }

    internal static async Task WriteAsync(HttpContext context, ErrorCode code, string safeDetail)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(Errors.Problem(code, safeDetail), JsonDefaults.Options);
        context.Response.StatusCode = Errors.Status(code);
        context.Response.ContentType = ServerProtocol.JsonContentType;
        context.Response.ContentLength = payload.Length;
        await context.Response.Body.WriteAsync(payload, context.RequestAborted);
        await context.Response.CompleteAsync();
    }
}
