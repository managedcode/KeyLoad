namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryEquivalentIntegerDecimalProtocol
{
    internal const string ByteField = "byteNumber";
    internal const string SignedByteField = "signedByteNumber";
    internal const string ShortField = "shortNumber";
    internal const string UnsignedShortField = "unsignedShortNumber";
    internal const string IntField = "intNumber";
    internal const string UnsignedIntField = "unsignedIntNumber";
    internal const string LongField = "longNumber";
    internal const string UnsignedLongField = "unsignedLongNumber";
    internal const string Collection = "integer_decimal_rows";
    internal const string Subject = "integer-decimal-reader";
    internal const string Tenant = "tenant";
    internal const string Database = "database";
    internal const string Root = "root";
    internal const string MinimumParameter = "minimum";
    internal const string Scan = "bounded-full-scan";
    internal const string Unsupported = "This expression is outside the supported Q1 C# subset.";
    internal const string SourcePrefix = "SELECT * FROM integer_decimal_rows d WHERE d.[";
    internal const string SourceTail = "] >= @minimum ORDER BY d.id";
    internal const string Compare = ">=";
    internal const string IdPath = "/@id";
    internal const string Star = "*";
    internal const string PathPrefix = "/";
    internal const string PutKind = "putDocument";
    internal const int Limit = 100;
    internal const int ColdCuts = 2;
    internal const long Revision = 1;
    internal const long InitialEpoch = 1;
    internal const long DeniedEpoch = 2;
    internal const long RepairEpoch = 3;
    internal const long RevokedEpoch = 4;
    internal const long FinalEpoch = 5;
    internal const decimal Fraction = 0.5m;
    internal const decimal Threshold = 1.5m;
    internal const string LowJson = """{"byteNumber":1,"signedByteNumber":1,"shortNumber":1,"unsignedShortNumber":1,"intNumber":1,"unsignedIntNumber":1,"longNumber":1,"unsignedLongNumber":1}""";
    internal const string MiddleJson = """{"byteNumber":2,"signedByteNumber":2,"shortNumber":2,"unsignedShortNumber":2,"intNumber":2,"unsignedIntNumber":2,"longNumber":2,"unsignedLongNumber":2}""";
    internal const string UpperJson = """{"byteNumber":3,"signedByteNumber":3,"shortNumber":3,"unsignedShortNumber":3,"intNumber":3,"unsignedIntNumber":3,"longNumber":3,"unsignedLongNumber":3}""";
    internal const string MaximumJson = """{"byteNumber":255,"signedByteNumber":127,"shortNumber":32767,"unsignedShortNumber":65535,"intNumber":2147483647,"unsignedIntNumber":4294967295,"longNumber":9223372036854775807,"unsignedLongNumber":18446744073709551615}""";
    internal const string NullJson = """{"byteNumber":null,"signedByteNumber":null,"shortNumber":null,"unsignedShortNumber":null,"intNumber":null,"unsignedIntNumber":null,"longNumber":null,"unsignedLongNumber":null}""";
    internal const string MissingJson = """{}""";
    internal static (string Id, string Json)[] Records =>
        [("a", LowJson), ("b", MiddleJson), ("c", UpperJson), ("d", MaximumJson), ("n", NullJson), ("z", MissingJson)];
    internal static string Field(QueryEquivalentIntegerKind kind) => kind switch
    {
        QueryEquivalentIntegerKind.Byte => ByteField,
        QueryEquivalentIntegerKind.SignedByte => SignedByteField,
        QueryEquivalentIntegerKind.Short => ShortField,
        QueryEquivalentIntegerKind.UnsignedShort => UnsignedShortField,
        QueryEquivalentIntegerKind.Int => IntField,
        QueryEquivalentIntegerKind.UnsignedInt => UnsignedIntField,
        QueryEquivalentIntegerKind.Long => LongField,
        QueryEquivalentIntegerKind.UnsignedLong => UnsignedLongField,
        _ => IntField
    };
    internal static decimal Maximum(QueryEquivalentIntegerKind kind) => kind switch
    {
        QueryEquivalentIntegerKind.Byte => byte.MaxValue,
        QueryEquivalentIntegerKind.SignedByte => sbyte.MaxValue,
        QueryEquivalentIntegerKind.Short => short.MaxValue,
        QueryEquivalentIntegerKind.UnsignedShort => ushort.MaxValue,
        QueryEquivalentIntegerKind.Int => int.MaxValue,
        QueryEquivalentIntegerKind.UnsignedInt => uint.MaxValue,
        QueryEquivalentIntegerKind.Long => long.MaxValue,
        QueryEquivalentIntegerKind.UnsignedLong => ulong.MaxValue,
        _ => int.MaxValue
    };
    internal static QueryRow[] Expected(bool boundary) => boundary
        ? [new("d", Revision, MaximumJson, RedactedFields: [])]
        : [new("b", Revision, MiddleJson, RedactedFields: []), new("c", Revision, UpperJson, RedactedFields: []),
            new("d", Revision, MaximumJson, RedactedFields: [])];
}
