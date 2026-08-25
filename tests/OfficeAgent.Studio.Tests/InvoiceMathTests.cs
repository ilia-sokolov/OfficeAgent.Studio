using OfficeAgent.Studio;
using Xunit;

namespace OfficeAgent.Studio.Tests;

/// <summary>
/// The arithmetic on an invoice.
/// </summary>
/// <remarks>
/// This is the part of the sample where being wrong is invisible. A layout mistake gets
/// noticed; a total that is a cent out gets signed and sent. The model supplies quantities
/// and unit prices and nothing else, so every figure here is the sample's own work.
/// </remarks>
public class InvoiceMathTests
{
    [Fact]
    public void The_printed_lines_add_up_to_the_printed_subtotal()
    {
        // Three lines that each land on a fractional cent. Summing the raw products and
        // rounding once at the end gives 100.00; the column as printed reads 100.01.
        var invoice = Invoice(taxPercent: 0,
            Line(3, 10.005m),
            Line(3, 10.005m),
            Line(3, 10.005m));

        var printed = invoice.Lines.Sum(InvoiceMath.LineTotal);

        Assert.Equal(printed, InvoiceMath.Subtotal(invoice));
        Assert.Equal(90.06m, InvoiceMath.Subtotal(invoice));
    }

    [Fact]
    public void The_total_is_the_subtotal_plus_the_tax_that_was_printed()
    {
        var invoice = Invoice(taxPercent: 21m, Line(1, 100m), Line(2, 33.33m));

        var subtotal = InvoiceMath.Subtotal(invoice);
        var tax = InvoiceMath.Tax(invoice);

        Assert.Equal(166.66m, subtotal);
        Assert.Equal(34.9986m, subtotal * 0.21m);   // the unrounded figure
        Assert.Equal(35.00m, tax);                  // what the page actually shows
        Assert.Equal(subtotal + tax, InvoiceMath.Total(invoice));
    }

    [Fact]
    public void A_negative_tax_rate_is_ignored_rather_than_subtracted()
    {
        // The tax line is only printed when the rate is positive. Applying a negative rate
        // anyway would leave a "Total due" that matches nothing visible on the page.
        var invoice = Invoice(taxPercent: -20m, Line(1, 100m));

        Assert.Equal(0m, InvoiceMath.Tax(invoice));
        Assert.Equal(100m, InvoiceMath.Total(invoice));
        Assert.DoesNotContain(InvoiceMath.Totals(invoice), row => row.Label.Contains("at "));
    }

    [Fact]
    public void A_zero_rate_prints_no_tax_line()
    {
        var invoice = Invoice(taxPercent: 0m, Line(2, 50m));

        var labels = InvoiceMath.Totals(invoice).Select(r => r.Label).ToList();

        Assert.Equal(new[] { "Subtotal", "Total due" }, labels);
        Assert.Equal(100m, InvoiceMath.Total(invoice));
    }

    [Fact]
    public void Only_the_total_is_marked_for_emphasis()
    {
        var invoice = Invoice(taxPercent: 20m, Line(1, 10m));

        var strong = InvoiceMath.Totals(invoice).Where(r => r.Strong).Select(r => r.Label).ToList();

        Assert.Equal(new[] { "Total due" }, strong);
    }

    [Fact]
    public void An_invoice_with_no_lines_totals_zero_rather_than_throwing()
    {
        var invoice = Invoice(taxPercent: 20m);

        Assert.Equal(0m, InvoiceMath.Subtotal(invoice));
        Assert.Equal(0m, InvoiceMath.Total(invoice));
    }

    [Fact]
    public void Halves_round_away_from_zero_the_way_a_reader_expects()
    {
        // Banker's rounding is the .NET default and would give 0.12 here, which looks like
        // an error on an invoice even though it is defensible statistically.
        Assert.Equal(0.13m, InvoiceMath.LineTotal(Line(1, 0.125m)));
        Assert.Equal(0.15m, InvoiceMath.LineTotal(Line(1, 0.145m)));
    }

    [Fact]
    public void Currency_precision_controls_the_printed_arithmetic()
    {
        var yen = Invoice(taxPercent: 10m, Line(1, 100.5m));
        yen = yen with { Currency = "JPY" };
        Assert.Equal(101m, InvoiceMath.Subtotal(yen));
        Assert.Equal(10m, InvoiceMath.Tax(yen));

        var dinar = Invoice(taxPercent: 0m, Line(1, 1.2345m));
        dinar = dinar with { Currency = "KWD" };
        Assert.Equal(1.235m, InvoiceMath.Subtotal(dinar));
    }

    [Theory]
    [InlineData("EUR", "€1,234.50")]
    [InlineData("JPY", "¥1,235")]
    [InlineData("KWD", "KWD 1,234.500")]
    public void ISO_currency_codes_have_stable_display_rules(string currency, string expected)
    {
        Assert.Equal(expected, InvoiceCurrency.Format(currency, 1234.5m));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static LineItemPlanned Line(decimal quantity, decimal unitPrice) =>
        new() { Description = "Work", Quantity = quantity, UnitPrice = unitPrice };

    private static InvoicePlanned Invoice(decimal taxPercent, params LineItemPlanned[] lines) => new()
    {
        From = new PartyPlanned { Name = "Studio" },
        To = new PartyPlanned { Name = "Client" },
        InvoiceNumber = "INV-1",
        Issued = "1 January 2026",
        Due = "31 January 2026",
        TaxRatePercent = taxPercent,
        Lines = lines
    };
}
