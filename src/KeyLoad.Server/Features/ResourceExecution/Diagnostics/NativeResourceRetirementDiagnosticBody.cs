using KeyLoad.Clustering;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class NativeResourceRetirementDiagnosticBody
{
    private const int Empty = 0;
    private const int OverflowByte = 1;

    internal static async Task<NativeResourceRetirementDiagnosticRequest> ReadAsync(HttpRequest request,
        IOptions<DatabaseLimits> limits, CancellationToken token)
    {
        if (request.Query.Count != Empty || request.Headers.ContentEncoding.Count != Empty
            || !string.Equals(request.ContentType, NativeResourceRetirementDiagnosticProtocol.ContentType, StringComparison.OrdinalIgnoreCase)
            || request.ContentLength is <= Empty
            || request.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == false)
        {
            throw Invalid();
        }
        if (request.ContentLength > limits.Value.MaxBatchBytes)
        {
            throw Exhausted();
        }
        var bytes = new byte[checked(limits.Value.MaxBatchBytes + OverflowByte)];
        var length = Empty;
        while (length < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            var read = await request.Body.ReadAsync(bytes.AsMemory(length), token).ConfigureAwait(false);
            if (read == Empty)
            {
                break;
            }
            length += read;
        }
        token.ThrowIfCancellationRequested();
        if (length == bytes.Length)
        {
            throw Exhausted();
        }
        if (length == Empty)
        {
            throw Invalid();
        }
        NativeResourceRetirementDiagnosticRequest value;
        try
        {
            value = NativeSerialization.Deserialize<NativeResourceRetirementDiagnosticRequest>(bytes.AsSpan(Empty, length))
                ?? throw Invalid();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)
            && error is not OperationCanceledException && error is not KeyLoadException)
        {
            throw Invalid();
        }
        Require(value, limits);
        token.ThrowIfCancellationRequested();
        return value;
    }

    private static void Require(NativeResourceRetirementDiagnosticRequest value, IOptions<DatabaseLimits> limits)
    {
        if (value.Nonce == Guid.Empty || value.PageSize <= Empty || value.PageSize > limits.Value.MaxResults
            || !Enum.IsDefined(value.Operation))
        {
            throw Invalid();
        }
        KeyLoad.Core.Features.ResourceExecution.ResourceRetirementValidation.Request(value.Request, limits.Value);
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Validation, ResourceRetirementNativeProtocol.InvalidProof);

    private static KeyLoadException Exhausted()
        => Errors.Fail(ErrorCode.ResourceExhausted, ResourceRetirementNativeProtocol.Unavailable);
}
