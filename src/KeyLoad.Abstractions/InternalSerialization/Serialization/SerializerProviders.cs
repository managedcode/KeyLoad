using System.Collections.Concurrent;
using System.Reflection;
using ManagedCode.Communication.Orleans.Surrogates;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Codecs;

namespace KeyLoad.Features.InternalSerialization;

internal static class SerializerProviders
{
    private const string OwnedAssemblyPrefix = "KeyLoad.";
    private static readonly Assembly SharedAssembly = typeof(OrleansSerialization).Assembly;
    private static readonly ConcurrentDictionary<Assembly, Lazy<SerializerContext>> Providers = new();

    internal static SerializerContext Get(Type type)
        => Providers.GetOrAdd(GetOwningAssembly(type), static assembly => new(() => Create(assembly))).Value;

    // The caller caches this codec profile. Execution providers retain the shared envelope and sessions;
    // profile-specific FieldCodecs can inspect borrowed opaque fields before admission.
    internal static SerializerContext CreateInspection(Type rootType, Action<ISerializerBuilder>? configure = null)
        => Create(GetOwningAssembly(rootType), builder =>
        {
            builder.Services.AddSingleton(new PayloadInspectionCodec(rootType));
            builder.Configure(options => options.FieldCodecs.Add(typeof(PayloadInspectionCodec)));
            configure?.Invoke(builder);
        });

    private static SerializerContext Create(Assembly assembly, Action<ISerializerBuilder>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFieldCodec<ReadOnlyMemory<byte>>, ReadOnlyMemoryOfByteCodec>();
        services.AddSerializer(builder =>
        {
            builder.AddAssembly(SharedAssembly);
            builder.AddAssembly(typeof(ResultTSurrogate<>).Assembly);
            if (assembly != SharedAssembly)
            {
                builder.AddAssembly(assembly);
            }
            configure?.Invoke(builder);
        });
        return new SerializerContext(services.BuildServiceProvider());
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
