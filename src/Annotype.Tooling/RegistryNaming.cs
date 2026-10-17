using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Annotype.Tooling;

/// <summary>
/// Derives the deterministic namespace of the generated registry from the assembly name.
/// </summary>
/// <remarks>
/// Assembly names made of letters, digits and dots map to an identifier by replacing dots with underscores
/// (<c>Payments.Plugins</c> becomes <c>Payments_Plugins</c>). This mapping is reversible, so it cannot collide.
/// Any other name (containing underscores, dashes, ...) is sanitized and suffixed with a stable hash of the
/// original name, so <c>Payments_Plugins</c> and <c>Payments-Plugins</c> do not collide with the above.
/// </remarks>
internal static class RegistryNaming
{
    public const string RootNamespace = "Annotype.Generated";

    public static string GetNamespace(string assemblyName) => RootNamespace + "." + GetIdentifier(assemblyName);

    public static string GetIdentifier(string assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
        {
            return "_";
        }

        var builder = new StringBuilder(assemblyName.Length + 10);
        var lossy = false;

        foreach (var c in assemblyName)
        {
            if (c == '.')
            {
                builder.Append('_');
            }
            else if (c < 128 && char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('_');
                lossy = true;
            }
        }

        if (char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        if (lossy)
        {
            builder.Append('_').Append(Fnv1a(assemblyName).ToString("x8", CultureInfo.InvariantCulture));
        }

        var identifier = builder.ToString();

        return SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ? "@" + identifier : identifier;
    }

    private static uint Fnv1a(string value)
    {
        var hash = 2166136261;

        foreach (var c in value)
        {
            hash = unchecked((hash ^ c) * 16777619);
        }

        return hash;
    }
}
