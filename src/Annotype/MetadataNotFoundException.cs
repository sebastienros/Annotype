using System;

namespace Annotype;

/// <summary>
/// The exception thrown by <see cref="Metadata.Get{TImplementation, TAttribute}"/> when no registered
/// <see cref="IMetadataRegistry"/> provides the requested metadata.
/// </summary>
public sealed class MetadataNotFoundException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataNotFoundException"/> class.
    /// </summary>
    /// <param name="implementationType">The requested implementation type.</param>
    /// <param name="attributeType">The requested attribute type.</param>
    public MetadataNotFoundException(Type implementationType, Type attributeType)
        : base($"No metadata of type '{attributeType}' is registered for type '{implementationType}'. " +
               $"Make sure the assembly '{implementationType.Assembly.GetName().Name}' references the Annotype package directly, " +
               "that the attribute is a required metadata attribute of one of the type's contracts, " +
               $"and that its generated registry is registered with {nameof(Metadata)}.{nameof(Metadata.Register)}().")
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
