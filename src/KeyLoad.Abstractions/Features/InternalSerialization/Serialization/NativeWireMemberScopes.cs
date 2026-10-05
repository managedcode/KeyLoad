using System.Reflection;
using System.Runtime.CompilerServices;

namespace KeyLoad.Features.InternalSerialization;

// Orleans emits a constructor scope (even when empty) before attributed record properties.
// Explicit property/field Ids are NONconstructor members in FieldIdAssignmentHelper10.3.1.
internal static class NativeWireMemberScopes
{
    private const int FirstParameterIndex = 0;
    private const int EmptyParameterCount = 0;
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private const string RecordCloneMethodName = "<Clone>$";
    private const string RecordPrintMembersMethodName = "PrintMembers";

    internal static IEnumerable<IReadOnlyDictionary<uint, Type>> Create(Type type)
    {
        var members = type.GetMembers(Flags);
        var includePrimary = IncludesPrimary(type);
        var parameters = includePrimary ? PrimaryParameters(type) : [];
        var primary = new Dictionary<uint, Type>();
        var body = new Dictionary<uint, Type>();
        foreach (var member in members)
        {
            AddMember(member, parameters, primary, body);
        }
        return includePrimary ? [primary, body] : [body];
    }

    private static void AddMember(MemberInfo member, ParameterInfo[] parameters, Dictionary<uint, Type> primary, Dictionary<uint, Type> body)
    {
        var memberType = member switch { PropertyInfo property => property.PropertyType, FieldInfo field => field.FieldType, _ => null };
        if (memberType is null)
        {
            return;
        }
        if (member.GetCustomAttribute<Orleans.IdAttribute>() is { } id)
        {
            body[id.Id] = memberType;
            return;
        }
        var index = Array.FindIndex(parameters, parameter => parameter.Name == member.Name && parameter.ParameterType == memberType);
        if (index >= FirstParameterIndex && member is PropertyInfo propertyInfo && IsGenerated(propertyInfo.GetMethod))
        {
            primary[parameters[index].GetCustomAttribute<Orleans.IdAttribute>()?.Id ?? (uint)index] = memberType;
        }
    }

    private static bool IncludesPrimary(Type type)
    {
        var annotation = type.CustomAttributes.First(attribute => attribute.AttributeType == typeof(Orleans.GenerateSerializerAttribute));
        foreach (var option in annotation.NamedArguments)
        {
            if (option.MemberName == nameof(Orleans.GenerateSerializerAttribute.IncludePrimaryConstructorParameters))
            {
                return option.TypedValue.Value is true;
            }
        }
        return IsRecord(type) || PrimaryParameters(type).Length > EmptyParameterCount;
    }

    private static ParameterInfo[] PrimaryParameters(Type type)
    {
        var constructors = type.GetConstructors(Flags).OrderBy(constructor => constructor.MetadataToken);
        if (IsRecord(type))
        {
            return constructors.FirstOrDefault(constructor => !IsGenerated(constructor))?.GetParameters() ?? [];
        }
        var properties = type.GetProperties(Flags);
        return constructors.Select(constructor => constructor.GetParameters()).FirstOrDefault(parameters => parameters.Length > EmptyParameterCount
            && parameters.All(parameter => properties.Any(property => property.Name == parameter.Name && IsGenerated(property.GetMethod)))) ?? [];
    }

    private static bool IsRecord(Type type)
        => type.GetMethods(Flags).Any(method => method.Name == RecordCloneMethodName
            || method.Name == RecordPrintMembersMethodName && IsGenerated(method));

    private static bool IsGenerated(MemberInfo? member)
        => member?.IsDefined(typeof(CompilerGeneratedAttribute), false) == true;
}
