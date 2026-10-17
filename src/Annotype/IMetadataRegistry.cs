using System;
using System.Diagnostics.CodeAnalysis;

namespace Annotype;

/// <summary>
/// Provides type-level metadata for the implementation types of a single assembly.
/// </summary>
/// <remarks>
/// The Annotype source generator emits one implementation per assembly, named
/// <c>Annotype.Generated.&lt;AssemblyName&gt;.MetadataRegistry</c>. Register it with
/// <see cref="Metadata.Register(IMetadataRegistry)"/>.
/// </remarks>
public interface IMetadataRegistry
{
    /// <summary>
    /// Attempts to create the metadata of type <paramref name="attributeType"/> declared on
    /// <paramref name="implementationType"/>.
    /// </summary>
    /// <param name="implementationType">The exact implementation type.</param>
    /// <param name="attributeType">The exact attribute type.</param>
    /// <param name="metadata">A new attribute snapshot when this registry knows the pair; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if this registry provides metadata for the pair.</returns>
    bool TryGet(Type implementationType, Type attributeType, [NotNullWhen(true)] out Attribute? metadata);
}
