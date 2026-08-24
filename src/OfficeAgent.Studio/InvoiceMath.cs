namespace OfficeAgent.Studio;

/// <summary>
/// The money on an invoice: line amounts, subtotal, tax, total.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the composer because this is the part that has to be right. A layout
/// mistake is visible; an arithmetic one is not, and it is signed off by whoever trusted
/// the figure. Keeping it here means it can be tested without composing a document.
/// </para>
/// <para>
/// The model never supplies an amount. It supplies quantities and unit prices, and every
/// figure printed on the invoice is derived from those by this class.
/// </para>
/// </remarks>
public static class InvoiceMath
{
    /// <summary>
    /// The amount for one line, rounded to the currency's smallest unit.
    /// </summary>
    /// <remarks>
    /// Rounding here rather than only at display time is what makes the printed lines add
    /// up to the printed subtotal. Summing unrounded products and rounding once at the end
    /// gives a subtotal a cent away from the column above it whenever a line lands on a
    /// fractional unit - which fractional quantities and hourly rates do routinely, and
    /// which a reader reads as an arithmetic error.
    /// </remarks>
    public static decimal LineTotal(LineItemPlanned line) =>
        decimal.Round(line.Quantity * line.UnitPrice, 2, MidpointRounding.AwayFromZero);

    /// <summary>The tax rate actually applied: never negative, whatever the model said.</summary>
    /// <remarks>
    /// A negative rate would otherwise be subtracted from the total while its line stayed
    /// hidden - the tax line is only printed when the rate is positive - leaving a
    /// "Total due" that matches nothing visible on the page.
    /// </remarks>
    public static decimal Rate(InvoicePlanned invoice) =>
        invoice.TaxRatePercent > 0 ? invoice.TaxRatePercent : 0m;

    public static decimal Subtotal(InvoicePlanned invoice) =>
        invoice.Lines is null ? 0m : invoice.Lines.Sum(LineTotal);

    public static decimal Tax(InvoicePlanned invoice) =>
        decimal.Round(Subtotal(invoice) * (Rate(invoice) / 100m), 2, MidpointRounding.AwayFromZero);

    public static decimal Total(InvoicePlanned invoice) => Subtotal(invoice) + Tax(invoice);

    /// <summary>
    /// The rows printed under the table, in order, with the one that gets set large.
    /// </summary>
    public static IEnumerable<(string Label, decimal Amount, bool Strong)> Totals(InvoicePlanned invoice)
    {
        yield return ("Subtotal", Subtotal(invoice), false);

        var rate = Rate(invoice);
        if (rate > 0)
            yield return ($"{invoice.TaxLabel} at {rate:0.##}%", Tax(invoice), false);

        yield return ("Total due", Total(invoice), true);
    }
}
