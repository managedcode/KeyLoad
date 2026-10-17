namespace KeyLoad.Features.InternalSerialization;

internal sealed record NativeMemberValidation(Func<object, object?> Read, NativeValueValidation? Value);
