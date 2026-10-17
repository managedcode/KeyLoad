using KeyLoad.Clustering;
using KeyLoad.Core;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class NativeResourceOwnerInventoryDiagnosticBody
{
    private const int Empty = 0;
    private const int OverflowByte = 1;

    internal static async Task<NativeResourceOwnerInventoryDiagnosticRequest> ReadAsync(HttpRequest request,
        IOptions<DatabaseLimits> limits, CancellationToken token)
    {
        if (request.Query.Count != Empty || request.Headers.ContentEncoding.Count != Empty
            || !string.Equals(request.ContentType, NativeResourceOwnerInventoryDiagnosticProtocol.ContentType, StringComparison.OrdinalIgnoreCase)
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
        NativeResourceOwnerInventoryDiagnosticRequest value;
        try
        {
            value = NativeSerialization.Deserialize<NativeResourceOwnerInventoryDiagnosticRequest>(bytes.AsSpan(Empty, length))
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

    private static void Require(NativeResourceOwnerInventoryDiagnosticRequest value, IOptions<DatabaseLimits> limits)
    {
        if (value.Nonce == Guid.Empty || value.PageSize <= Empty || value.PageSize > limits.Value.MaxResults)
        {
            throw Invalid();
        }
        JsonData.Identifier(value.TenantId);
        JsonData.Identifier(value.DatabaseId);
        JsonData.Identifier(value.ResourceName);
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Validation, ResourceOwnerInventoryNativeProtocol.InvalidProof);

    private static KeyLoadException Exhausted()
        => Errors.Fail(ErrorCode.ResourceExhausted, ResourceOwnerInventoryNativeProtocol.Unavailable);
}
