using System.Globalization;

namespace OfficeAgent.Studio;

/// <summary>ISO-aware display and precision rules for invoice money.</summary>
internal static class InvoiceCurrency
{
    private static readonly IReadOnlyDictionary<string, (string Symbol, int Digits)> Known =
        new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase)
        {
            ["EUR"] = ("€", 2),
            ["GBP"] = ("£", 2),
            ["USD"] = ("$", 2),
            ["JPY"] = ("¥", 0),
            ["KWD"] = ("KWD ", 3),
            ["BHD"] = ("BHD ", 3),
            ["JOD"] = ("JOD ", 3),
            ["OMR"] = ("OMR ", 3),
            ["TND"] = ("TND ", 3)
        };

    private static readonly IReadOnlyDictionary<string, string> LegacySymbols =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["€"] = "EUR",
            ["£"] = "GBP",
            ["$"] = "USD",
            ["¥"] = "JPY"
        };

    internal static string Normalize(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new InvalidOperationException("Invoice currency is empty; use an ISO 4217 code such as EUR.");

        var value = currency.Trim();
        if (LegacySymbols.TryGetValue(value, out var code)) return code;

        if (value.Length != 3 || !value.All(char.IsAsciiLetter))
            throw new InvalidOperationException(
                $"Invoice currency '{currency}' is not an ISO 4217 code such as EUR, GBP, USD or JPY.");

        return value.ToUpperInvariant();
    }

    internal static int DecimalPlaces(string? currency)
    {
        var normalized = Normalize(currency);
        return Known.TryGetValue(normalized, out var specification) ? specification.Digits : 2;
    }

    internal static string Format(string? currency, decimal amount)
    {
        var normalized = Normalize(currency);
        var specification = Known.TryGetValue(normalized, out var known)
            ? known
            : (normalized + " ", 2);

        return specification.Item1 + amount.ToString($"N{specification.Item2}", CultureInfo.InvariantCulture);
    }
}
