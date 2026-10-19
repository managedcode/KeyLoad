using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class OperationalPolicyBindings
{
    internal static bool IsNonblockingWait(Compilation compilation, IInvocationOperation invocation) =>
        invocation.TargetMethod.Name == MagicRuntimeMetadataNames.WaitAsync &&
        MagicRuntimeOperations.IsType(compilation, invocation.TargetMethod.ContainingType, MagicRuntimeMetadataNames.Semaphore) &&
        invocation.Arguments is [var timeout, var cancellation] &&
        timeout.Value.Type?.SpecialType == SpecialType.System_Int32 &&
        timeout.Value.ConstantValue is { HasValue: true, Value: int milliseconds } &&
        milliseconds == OperationalPolicyMetadataNames.NonblockingWaitMilliseconds &&
        MagicRuntimeOperations.IsType(compilation, cancellation.Parameter?.Type, OperationalPolicyMetadataNames.CancellationToken) &&
        invocation.TargetMethod.ReturnType is INamedTypeSymbol { TypeArguments: [var result] } &&
        result.SpecialType == SpecialType.System_Boolean;

    internal static bool IsCoalescedWakeSignal(Compilation compilation, IObjectCreationOperation creation) =>
        MagicRuntimeOperations.IsType(compilation, creation.Type, OperationalPolicyMetadataNames.BoundedChannel) &&
        creation.Arguments is [var capacity] && capacity.Value.ConstantValue is
        { HasValue: true, Value: int slots } && slots == OperationalPolicyMetadataNames.WakeSignalCapacity &&
        creation.Parent is IArgumentOperation { Parent: IInvocationOperation invocation } &&
        invocation.TargetMethod.Name == OperationalPolicyMetadataNames.CreateBounded &&
        MagicRuntimeOperations.IsType(compilation, invocation.TargetMethod.ContainingType, OperationalPolicyMetadataNames.Channel) &&
        invocation.TargetMethod.TypeArguments is [var item] && item.SpecialType == SpecialType.System_Boolean &&
        creation.Initializer?.Initializers.OfType<ISimpleAssignmentOperation>().Any(assignment =>
            assignment.Target is IPropertyReferenceOperation property && property.Property.Name == OperationalPolicyMetadataNames.FullMode &&
            assignment.Value is IFieldReferenceOperation mode && mode.Field.Name == OperationalPolicyMetadataNames.DropWrite &&
            MagicRuntimeOperations.IsType(compilation, mode.Field.ContainingType, OperationalPolicyMetadataNames.ChannelFullMode)) == true &&
        creation.Initializer.Initializers.OfType<ISimpleAssignmentOperation>().Any(static assignment =>
            assignment.Target is IPropertyReferenceOperation property && property.Property.Name == OperationalPolicyMetadataNames.SingleReader &&
            assignment.Value.ConstantValue is { HasValue: true, Value: true });

    internal static bool IsCapacityMethod(Compilation compilation, IMethodSymbol method,
        ImmutableArray<IArgumentOperation> arguments)
    {
        var type = method.ContainingType;
        if (method.MethodKind == MethodKind.Constructor &&
            MagicRuntimeOperations.IsType(compilation, type, MagicRuntimeMetadataNames.Semaphore))
        {
            return !IsStructuralMutex(arguments);
        }

        return method.MethodKind == MethodKind.Constructor &&
            MagicRuntimeOperations.IsType(compilation, type, OperationalPolicyMetadataNames.BoundedChannel) ||
            method.Name == OperationalPolicyMetadataNames.CreateBounded &&
            MagicRuntimeOperations.IsType(compilation, type, OperationalPolicyMetadataNames.Channel);
    }

    internal static bool IsOperationalProperty(Compilation compilation, IPropertySymbol property)
    {
        var type = property.ContainingType;
        return TimerPolicy.IsOperationalProperty(compilation, property) ||
            MagicRuntimeOperations.IsType(compilation, type, OperationalPolicyMetadataNames.HttpClient) &&
                property.Name == OperationalPolicyMetadataNames.Timeout ||
            IsFileType(compilation, type, OperationalPolicyMetadataNames.FileStreamOptions) &&
                property.Name == OperationalPolicyMetadataNames.FileBufferSizeProperty ||
            MagicRuntimeOperations.IsType(compilation, type, OperationalPolicyMetadataNames.BoundedChannel) &&
                property.Name == OperationalPolicyMetadataNames.Capacity ||
            IsHttpHandler(compilation, type) && property.Name is
                OperationalPolicyMetadataNames.ConnectTimeout or OperationalPolicyMetadataNames.PooledConnectionIdleTimeout or
                OperationalPolicyMetadataNames.PooledConnectionLifetime or OperationalPolicyMetadataNames.MaxConnectionsPerServer or
                OperationalPolicyMetadataNames.MaxResponseHeadersLength or OperationalPolicyMetadataNames.ResponseDrainTimeout or
                OperationalPolicyMetadataNames.MaxResponseDrainSize or OperationalPolicyMetadataNames.Expect100ContinueTimeout ||
            MagicRuntimeOperations.IsType(compilation, type, OperationalPolicyMetadataNames.Socket) && property.Name is
                OperationalPolicyMetadataNames.ReceiveTimeout or OperationalPolicyMetadataNames.SendTimeout or
                OperationalPolicyMetadataNames.ReceiveBufferSize or OperationalPolicyMetadataNames.SendBufferSize ||
            IsRetryOptions(compilation, type) && property.Name is
                OperationalPolicyMetadataNames.MaxRetryAttempts or OperationalPolicyMetadataNames.Delay;
    }

    internal static bool IsFileBufferArgument(Compilation compilation, IMethodSymbol method, IArgumentOperation argument) =>
        method.MethodKind == MethodKind.Constructor && argument.ArgumentKind == ArgumentKind.Explicit &&
        argument.Parameter?.Name == OperationalPolicyMetadataNames.FileBufferSizeParameter &&
        (IsFileType(compilation, method.ContainingType, OperationalPolicyMetadataNames.FileStream) ||
         IsFileType(compilation, method.ContainingType, OperationalPolicyMetadataNames.StreamWriter) ||
         IsFileType(compilation, method.ContainingType, OperationalPolicyMetadataNames.StreamReader));

    private static bool IsFileType(Compilation compilation, ITypeSymbol type, string metadataName) =>
        MagicRuntimeOperations.IsType(compilation, type, metadataName) &&
        SymbolEqualityComparer.Default.Equals(type.ContainingAssembly,
            compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly);

    private static bool IsStructuralMutex(ImmutableArray<IArgumentOperation> arguments) =>
        arguments.Length == OperationalPolicyMetadataNames.MutexConstructorArguments &&
        arguments.All(static argument => argument.Value.ConstantValue is
        { HasValue: true, Value: int permits } && permits == OperationalPolicyMetadataNames.MutexPermits);

    private static bool IsHttpHandler(Compilation compilation, ITypeSymbol type) =>
        MagicRuntimeOperations.IsType(compilation, type, OperationalPolicyMetadataNames.SocketsHttpHandler) ||
        MagicRuntimeOperations.IsType(compilation, type, OperationalPolicyMetadataNames.HttpClientHandler);

    private static bool IsRetryOptions(Compilation compilation, ITypeSymbol type) =>
        MagicRuntimeOperations.IsType(compilation, type, OperationalPolicyMetadataNames.RetryOptions) ||
        MagicRuntimeOperations.IsType(compilation, type, OperationalPolicyMetadataNames.GenericRetryOptions);
}
