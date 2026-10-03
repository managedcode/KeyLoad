using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;

namespace KeyLoad.Features.InternalSerialization;

internal static class NativeSerializerProviders
{
    private const string OwnedAssemblyPrefix = "KeyLoad.";
    private static readonly Assembly SharedAssembly = typeof(NativeSerialization).Assembly;
    private static readonly ConcurrentDictionary<Assembly, Lazy<NativeSerializerContext>> Providers = new();

    internal static NativeSerializerContext Get(Type type)
        => Providers.GetOrAdd(GetOwningAssembly(type), static assembly => new(() => Create(assembly))).Value;

    // The caller caches this codec profile. Native providers retain the shared envelope and sessions;
    // profile-specific FieldCodecs can inspect borrowed opaque fields before admission.
    internal static NativeSerializerContext CreateInspection(Type rootType, Action<ISerializerBuilder>? configure = null)
        => Create(GetOwningAssembly(rootType), builder =>
        {
            builder.Services.AddSingleton(new NativePayloadInspectionCodec(rootType));
            builder.Configure(options => options.FieldCodecs.Add(typeof(NativePayloadInspectionCodec)));
            configure?.Invoke(builder);
        });

    private static NativeSerializerContext Create(Assembly assembly, Action<ISerializerBuilder>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFieldCodec<ReadOnlyMemory<byte>>, ReadOnlyMemoryOfByteCodec>();
        services.AddSerializer(builder =>
        {
            builder.AddAssembly(SharedAssembly);
            if (assembly != SharedAssembly)
            {
                builder.AddAssembly(assembly);
            }
            configure?.Invoke(builder);
        });
        return new NativeSerializerContext(services.BuildServiceProvider());
    }

    private static Assembly GetOwningAssembly(Type type)
    {
        if (type.HasElementType)
        {
            return GetOwningAssembly(type.GetElementType()!);
        }
        foreach (var argument in type.GenericTypeArguments)
        {
            var argumentAssembly = GetOwningAssembly(argument);
            if (argumentAssembly != SharedAssembly)
            {
                return argumentAssembly;
            }
        }
        return type.Assembly.GetName().Name?.StartsWith(OwnedAssemblyPrefix, StringComparison.Ordinal) == true
            ? type.Assembly : SharedAssembly;
    }
}

// The context owns serializer-only services for the process lifetime, independent of silo activations.
internal sealed class NativeSerializerContext(ServiceProvider services)
{
    internal SerializerSessionPool Sessions { get; } = services.GetRequiredService<SerializerSessionPool>();

    internal Serializer<NativePayload> Serializer { get; } = services.GetRequiredService<Serializer<NativePayload>>();
}
