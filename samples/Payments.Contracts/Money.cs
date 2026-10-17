using System.Globalization;

namespace Payments.Contracts;

/// <summary>
/// A custom class used as an attribute property. Attribute arguments cannot be <see cref="decimal"/> or
/// custom types, so <see cref="PaymentLimitsAttribute"/> receives strings and builds instances in its constructor.
/// </summary>
public sealed class Money
{
    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    /// <summary>
    /// Parses values such as <c>"10.50 EUR"</c>.
    /// </summary>
    public static Money Parse(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2 || !decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            throw new FormatException($"'{value}' is not a valid amount such as '10.50 EUR'.");
        }

        return new Money(amount, parts[1]);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Amount:0.00} {Currency}");
}
