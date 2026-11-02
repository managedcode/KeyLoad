namespace KeyLoad.InternalSerialization;

internal sealed record MemberValidation(Func<object, object?> /* KLD0041: These delegates bind native PropertyInfo.GetValue and FieldInfo.GetValue for runtime Id-member metadata. */ Read, ValueValidation? Value, bool PublicInput = false);
