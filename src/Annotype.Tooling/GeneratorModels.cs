using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Annotype.Tooling;

/// <summary>
/// An immutable array with value equality, so that incremental pipeline outputs can be cached.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _items;

    public EquatableArray(ImmutableArray<T> items) => _items = items;

    public ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public int Length => Items.Length;

    public bool Equals(EquatableArray<T> other)
    {
        var left = Items;
        var right = other.Items;

        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;

        foreach (var item in Items)
        {
            hash = unchecked((hash * 31) + item.GetHashCode());
        }

        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// A location that does not hold on to syntax trees or compilations.
/// </summary>
internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    public static LocationInfo? From(Location location)
    {
        if (location.SourceTree is null)
        {
            return null;
        }

        return new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }
}

internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> Arguments)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location location, params string[] arguments)
        => new(descriptor, LocationInfo.From(location), new EquatableArray<string>(ImmutableArray.Create(arguments)));

    public Diagnostic ToDiagnostic()
    {
        var arguments = new object[Arguments.Length];

        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = Arguments.Items[i];
        }

        return Diagnostic.Create(Descriptor, Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None, arguments);
    }
}

/// <summary>
/// The code needed to recreate one attribute for one implementation type.
/// </summary>
internal sealed record MetadataEntry(string AttributeType, string Expression);

/// <summary>
/// The generator's view of one implementation type: its entries and the diagnostics to report.
/// </summary>
internal sealed record ImplementationModel(
    string SortKey,
    string ImplementationType,
    EquatableArray<MetadataEntry> Entries,
    EquatableArray<DiagnosticInfo> Diagnostics);
