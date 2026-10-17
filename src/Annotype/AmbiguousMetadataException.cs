using System;

namespace Annotype;

/// <summary>
/// The exception thrown when more than one registered <see cref="IMetadataRegistry"/> provides metadata for the
/// same implementation and attribute type pair. Annotype never silently picks one of them.
/// </summary>
public sealed class AmbiguousMetadataException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AmbiguousMetadataException"/> class.
    /// </summary>
    /// <param name="implementationType">The requested implementation type.</param>
    /// <param name="attributeType">The requested attribute type.</param>
    /// <param name="first">The first registry that provided the metadata.</param>
    /// <param name="second">The second registry that provided the metadata.</param>
    public AmbiguousMetadataException(Type implementationType, Type attributeType, IMetadataRegistry first, IMetadataRegistry second)
        : base($"Metadata of type '{attributeType}' for type '{implementationType}' is provided by more than one registered registry: " +
               $"'{first?.GetType()}' and '{second?.GetType()}'.")
    {
        ImplementationType = implementationType;
        AttributeType = attributeType;
    }

    /// <summary>
    /// Gets the requested implementation type.
    /// </summary>
    public Type ImplementationType { get; }

    /// <summary>
    /// Gets the requested attribute type.
    /// </summary>
    public Type AttributeType { get; }
}
