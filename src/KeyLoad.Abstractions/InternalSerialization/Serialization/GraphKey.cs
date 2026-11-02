namespace KeyLoad.InternalSerialization;

internal readonly record struct GraphKey(object /* KLD0041: Only tracked reference nodes use this key; their runtime classes differ and equality is reference identity. */ Value, ValueValidation? Validation);
