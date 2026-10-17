namespace Payments.Contracts;

/// <summary>
/// Shows that metadata properties are not limited to attribute argument types: the constructor receives
/// constants and exposes <see cref="Money"/> instances. The generated registry replays this constructor, so every
/// snapshot gets its own <see cref="Money"/> objects.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PaymentLimitsAttribute : Attribute
{
    public PaymentLimitsAttribute(string minimum, string maximum)
    {
        Minimum = Money.Parse(minimum);
        Maximum = Money.Parse(maximum);
    }

    public Money Minimum { get; }

    public Money Maximum { get; }
}
