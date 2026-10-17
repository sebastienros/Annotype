using System;
using System.Diagnostics.CodeAnalysis;

namespace Annotype;

/// <summary>
/// Retrieves type-level metadata from registered <see cref="IMetadataRegistry"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// Lookups only query registries that were explicitly registered. Annotype never scans assemblies, never
/// calls <c>GetCustomAttributes</c> and never instantiates the implementation type.
/// </para>
/// <para>
/// Registration is process-wide and permanent: registries cannot be removed and keep their assembly alive.
/// </para>
/// </remarks>
public static class Metadata
{
    private static readonly MetadataRegistrySet s_registries = new();

    /// <summary>
    /// Registers a metadata registry, usually the generated
    /// <c>Annotype.Generated.&lt;AssemblyName&gt;.MetadataRegistry.Instance</c> of an assembly.
    /// Registering the same instance more than once has no effect.
    /// </summary>
    /// <param name="registry">The registry to register.</param>
    public static void Register(IMetadataRegistry registry) => s_registries.Register(registry);

    /// <summary>
    /// Returns a new snapshot of the <typeparamref name="TAttribute"/> metadata declared on
    /// <typeparamref name="TImplementation"/>.
    /// </summary>
    /// <typeparam name="TImplementation">The exact implementation type.</typeparam>
    /// <typeparam name="TAttribute">The exact attribute type.</typeparam>
    /// <returns>A new attribute instance on every call. Mutating it does not affect other snapshots.</returns>
    /// <exception cref="MetadataNotFoundException">No registered registry provides the metadata.</exception>
    /// <exception cref="AmbiguousMetadataException">More than one registered registry provides the metadata.</exception>
    public static TAttribute Get<TImplementation, TAttribute>()
        where TAttribute : Attribute
        => TryGet<TImplementation, TAttribute>(out var metadata)
            ? metadata
            : throw new MetadataNotFoundException(typeof(TImplementation), typeof(TAttribute));

    /// <summary>
    /// Attempts to return a new snapshot of the <typeparamref name="TAttribute"/> metadata declared on
    /// <typeparamref name="TImplementation"/>.
    /// </summary>
    /// <typeparam name="TImplementation">The exact implementation type.</typeparam>
    /// <typeparam name="TAttribute">The exact attribute type.</typeparam>
    /// <param name="metadata">A new attribute instance, or <see langword="null"/> when no registry provides it.</param>
    /// <returns><see langword="true"/> if the metadata was found.</returns>
    /// <exception cref="AmbiguousMetadataException">More than one registered registry provides the metadata.</exception>
    public static bool TryGet<TImplementation, TAttribute>([NotNullWhen(true)] out TAttribute? metadata)
        where TAttribute : Attribute
    {
        if (s_registries.TryGet(typeof(TImplementation), typeof(TAttribute), out var value))
        {
            metadata = (TAttribute)value;
            return true;
        }

        metadata = null;
        return false;
    }

    /// <summary>
    /// Returns a new snapshot of the <paramref name="attributeType"/> metadata declared on
    /// <paramref name="implementationType"/>.
    /// </summary>
    /// <param name="implementationType">The exact implementation type.</param>
    /// <param name="attributeType">The exact attribute type.</param>
    /// <returns>A new attribute instance on every call.</returns>
    /// <exception cref="MetadataNotFoundException">No registered registry provides the metadata.</exception>
    /// <exception cref="AmbiguousMetadataException">More than one registered registry provides the metadata.</exception>
    public static Attribute Get(Type implementationType, Type attributeType)
        => s_registries.TryGet(implementationType, attributeType, out var metadata)
            ? metadata
            : throw new MetadataNotFoundException(implementationType, attributeType);

    /// <summary>
    /// Attempts to return a new snapshot of the <paramref name="attributeType"/> metadata declared on
    /// <paramref name="implementationType"/>.
    /// </summary>
    /// <param name="implementationType">The exact implementation type.</param>
    /// <param name="attributeType">The exact attribute type.</param>
    /// <param name="metadata">A new attribute instance, or <see langword="null"/> when no registry provides it.</param>
    /// <returns><see langword="true"/> if the metadata was found.</returns>
    /// <exception cref="AmbiguousMetadataException">More than one registered registry provides the metadata.</exception>
    public static bool TryGet(Type implementationType, Type attributeType, [NotNullWhen(true)] out Attribute? metadata)
        => s_registries.TryGet(implementationType, attributeType, out metadata);
}
