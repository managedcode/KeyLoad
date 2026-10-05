using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class OperationalPolicyBindings
{
    internal static bool IsCapacityMethod(Compilation compilation, IMethodSymbol method,
        ImmutableArray<IArgumentOperation> arguments)
    {
        var type = method.ContainingType;
        if (method.MethodKind == MethodKind.Constructor &&
            MagicRuntimeOperations.IsNativeType(compilation, type, MagicRuntimeMetadataNames.Semaphore))
        {
            return !IsStructuralMutex(arguments);
        }

        return method.MethodKind == MethodKind.Constructor &&
            MagicRuntimeOperations.IsNativeType(compilation, type, OperationalPolicyMetadataNames.BoundedChannel) ||
            method.Name == OperationalPolicyMetadataNames.CreateBounded &&
            MagicRuntimeOperations.IsNativeType(compilation, type, OperationalPolicyMetadataNames.Channel);
    }

    internal static bool IsOperationalProperty(Compilation compilation, IPropertySymbol property)
    {
        var type = property.ContainingType;
        return MagicRuntimeOperations.IsNativeType(compilation, type, OperationalPolicyMetadataNames.HttpClient) &&
                property.Name == OperationalPolicyMetadataNames.Timeout ||
            MagicRuntimeOperations.IsNativeType(compilation, type, OperationalPolicyMetadataNames.BoundedChannel) &&
                property.Name == OperationalPolicyMetadataNames.Capacity ||
            IsHttpHandler(compilation, type) && property.Name is
                OperationalPolicyMetadataNames.ConnectTimeout or OperationalPolicyMetadataNames.PooledConnectionIdleTimeout or
                OperationalPolicyMetadataNames.PooledConnectionLifetime or OperationalPolicyMetadataNames.MaxConnectionsPerServer or
                OperationalPolicyMetadataNames.MaxResponseHeadersLength or OperationalPolicyMetadataNames.ResponseDrainTimeout or
                OperationalPolicyMetadataNames.MaxResponseDrainSize or OperationalPolicyMetadataNames.Expect100ContinueTimeout ||
            MagicRuntimeOperations.IsNativeType(compilation, type, OperationalPolicyMetadataNames.Socket) && property.Name is
                OperationalPolicyMetadataNames.ReceiveTimeout or OperationalPolicyMetadataNames.SendTimeout or
                OperationalPolicyMetadataNames.ReceiveBufferSize or OperationalPolicyMetadataNames.SendBufferSize ||
            IsRetryOptions(compilation, type) && property.Name is
                OperationalPolicyMetadataNames.MaxRetryAttempts or OperationalPolicyMetadataNames.Delay;
    }

    private static bool IsStructuralMutex(ImmutableArray<IArgumentOperation> arguments) =>
        arguments.Length == OperationalPolicyMetadataNames.MutexPermits + OperationalPolicyMetadataNames.MutexPermits &&
        arguments.All(static argument => argument.Value.ConstantValue is
            { HasValue: true, Value: int permits } && permits == OperationalPolicyMetadataNames.MutexPermits);

    private static bool IsHttpHandler(Compilation compilation, ITypeSymbol type) =>
        MagicRuntimeOperations.IsNativeType(compilation, type, OperationalPolicyMetadataNames.SocketsHttpHandler) ||
        MagicRuntimeOperations.IsNativeType(compilation, type, OperationalPolicyMetadataNames.HttpClientHandler);

    private static bool IsRetryOptions(Compilation compilation, ITypeSymbol type) =>
        MagicRuntimeOperations.IsNativeType(compilation, type, OperationalPolicyMetadataNames.RetryOptions) ||
        MagicRuntimeOperations.IsNativeType(compilation, type, OperationalPolicyMetadataNames.GenericRetryOptions);
}
