using System.Text.Json;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal sealed class ServerErrorMiddleware(RequestDelegate next, ILogger<ServerErrorMiddleware> logger, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private const string UnexpectedFailure = "The server could not complete the database operation.";

    public async Task InvokeAsync(HttpContext context)
    {
        try
        { await next(context).ConfigureAwait(false); }
        catch (KeyLoadException error) when (!context.Response.HasStarted)
        { await WriteAsync(context, error.Code, error.Message).ConfigureAwait(false); }
        catch (JsonException) when (!context.Response.HasStarted)
        {
            await WriteAsync(context, ErrorCode.Validation, ServerProtocol.InvalidJson).ConfigureAwait(false);
        }
        catch (BadHttpRequestException error) when (!context.Response.HasStarted)
        {
            var large = error.StatusCode == StatusCodes.Status413PayloadTooLarge;
            await WriteAsync(context, large ? ErrorCode.ResourceExhausted : ErrorCode.Validation,
                large ? ServerProtocol.BodyExceeded : ServerProtocol.InvalidJson).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The caller owns this cancellation; a committed command is resolved by its stable retry ID.
        }
        catch (Exception error) when (!context.Response.HasStarted && NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            RequestFailureDiagnostic.LogFailure(logger, context, error, time);
            await WriteAsync(context, ErrorCode.RecoveryRequired, UnexpectedFailure).ConfigureAwait(false);
        }
    }

    internal static async Task WriteAsync(HttpContext context, ErrorCode code, string safeDetail)
    {
        context.Response.StatusCode = Errors.Status(code);
        context.Response.ContentLength = null;
        await context.Response.WriteAsJsonAsync(Errors.Problem(code, safeDetail), JsonDefaults.Options,
            context.RequestAborted).ConfigureAwait(false);
    }
}
