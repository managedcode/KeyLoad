using System.Text.Json;

namespace KeyLoad.Server;

internal sealed class ServerErrorMiddleware(RequestDelegate next, ILogger<ServerErrorMiddleware> logger)
{
    private const string UnexpectedFailure = "The server could not complete the database operation.";
    private const string UnexpectedLog = "Database request failed with {ExceptionType}.";
    private const int UnexpectedEventId = 1001;
    private static readonly Action<ILogger, string, Exception?> LogFailure = LoggerMessage.Define<string>(LogLevel.Error,
        new EventId(UnexpectedEventId), UnexpectedLog);

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
        catch (Exception error) when (!context.Response.HasStarted)
        {
            LogFailure(logger, error.GetType().FullName ?? error.GetType().Name, null);
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
