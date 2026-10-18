using System;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class AggregateTypeLimitException
{
    private const string AllowedAssemblyName = "KeyLoad.Core";
    private const string AllowedTypeName = "KeyLoad.Core.DatabaseEngine";
    private static readonly DateOnly ExpirationDate = new(2026, 11, 1);

    internal static bool IsAllowed(string? assemblyName, string? typeName, DateOnly utcDate) =>
        string.Equals(assemblyName, AllowedAssemblyName, StringComparison.Ordinal) &&
        string.Equals(typeName, AllowedTypeName, StringComparison.Ordinal) &&
        utcDate < ExpirationDate;
}
