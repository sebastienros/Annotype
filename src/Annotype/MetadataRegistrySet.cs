using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Annotype;

/// <summary>
/// A thread-safe, copy-on-write set of registries. Reads never lock; registration replaces the array.
/// </summary>
internal sealed class MetadataRegistrySet
{
    private readonly object _sync = new();
    private IMetadataRegistry[] _registries = [];

    public IMetadataRegistry[] Registries => Volatile.Read(ref _registries);

    public void Register(IMetadataRegistry registry)
    {
        ThrowIfNull(registry);

        lock (_sync)
        {
            var current = _registries;

            foreach (var existing in current)
            {
                if (ReferenceEquals(existing, registry))
                {
                    return;
                }
            }

            var next = new IMetadataRegistry[current.Length + 1];
            Array.Copy(current, next, current.Length);
            next[current.Length] = registry;
            Volatile.Write(ref _registries, next);
        }
    }

    public bool TryGet(Type implementationType, Type attributeType, [NotNullWhen(true)] out Attribute? metadata)
    {
        ThrowIfNull(implementationType);
        ThrowIfNull(attributeType);

        metadata = null;
        IMetadataRegistry? owner = null;

        // Every registry is queried so that two registries claiming the same pair are reported instead of
        // silently resolved by registration order.
        foreach (var registry in Registries)
        {
            if (!registry.TryGet(implementationType, attributeType, out var candidate))
            {
                continue;
            }

            if (owner is not null)
            {
                throw new AmbiguousMetadataException(implementationType, attributeType, owner, registry);
            }

            if (candidate is null || !attributeType.IsInstanceOfType(candidate))
            {
                throw new InvalidOperationException(
                    $"Registry '{registry.GetType()}' returned an invalid value for metadata '{attributeType}' of type '{implementationType}'.");
            }

            owner = registry;
            metadata = candidate;
        }

        return metadata is not null;
    }

    private static void ThrowIfNull(object argument, [CallerArgumentExpression(nameof(argument))] string? name = null)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(argument, name);
#else
        if (argument is null)
        {
            throw new ArgumentNullException(name);
        }
#endif
    }
}
