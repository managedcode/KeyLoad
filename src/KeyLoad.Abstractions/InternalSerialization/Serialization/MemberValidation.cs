namespace KeyLoad.Features.InternalSerialization;

internal sealed record MemberValidation(Func<object, object?> Read, ValueValidation? Value);
