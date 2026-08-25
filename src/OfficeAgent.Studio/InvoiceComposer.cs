using OfficeAgent.Abstractions;
using OfficeAgent.Core;

namespace OfficeAgent.Studio;

/// <summary>
/// Turns an <see cref="InvoicePlanned"/> into a .docx.
/// </summary>
/// <remarks>
/// An invoice is the one document here where the layout is not a matter of taste: the
/// reader is looking for four things - who owes, how much, by when, and where to send it -
/// and everything else is in the way. So the type is quieter than the report's, the totals
/// are the only thing set large, and the arithmetic is done here rather than trusted to the
/// model.
/// </remarks>
public sealed class InvoiceComposer
{
    /// <summary>
    /// The text column of a Letter page with Word's default one-inch margins, in pixels at
    /// 96 DPI: 8.5in less two margins, times 96.
    /// </summary>
    private const int TextWidthPx = 624;

    /// <summary>
    /// Description, quantity, unit price, amount - summing to exactly the text column.
    /// </summary>
    /// <remarks>
    /// A description needs room to breathe and a quantity does not, so equal columns are
    /// what make a generated table look generated. The sum matters as much as the ratio:
    /// widths totalling more than the page pushes the last column off the right edge, where
    /// it is not narrow or wrapped but simply absent - and an invoice missing its amounts
    /// still validates and still opens.
    /// </remarks>
    private static readonly int[] Columns = { TextWidthPx - 290, 60, 115, 115 };

    private readonly OfficeAgentClient _client;
    private readonly DesignSystem _design;
    private readonly string _connection;

    public InvoiceComposer(OfficeAgentClient client, DesignSystem design, string connection = "output")
    {
        _client = client;
        _design = design;
        _connection = connection;
    }

    public Task<string> ComposeAsync(
        InvoicePlanned invoice, string fileName, CancellationToken ct = default) =>
        ComposerSession.RunAsync(
            _client, _connection, fileName, id => ComposeCreatedAsync(invoice, id, ct), ct);

    private async Task ComposeCreatedAsync(
        InvoicePlanned invoice, string id, CancellationToken ct)
    {
        var lines = new List<(string ParaId, string Role)>();

        // The letterhead goes into the paragraph a blank document already has, so the
        // wordmark opens the page rather than following an empty line.
        string? wordmark = null;
        if (_design.Logo is null && _design.Wordmark is { Length: > 0 } mark)
        {
            wordmark = await FillFirstAsync(id, $"{_design.WordmarkDot} {mark}", ct);
            lines.Add((wordmark, "wordmark"));
            lines.Add((await AppendAsync(id, "Invoice", ct), "title"));
        }
        else
        {
            lines.Add((await FillFirstAsync(id, "Invoice", ct), "title"));
        }
        lines.Add((await AppendAsync(id, invoice.InvoiceNumber, ct), "reference"));

        lines.Add((await AppendAsync(id, "From", ct), "label"));
        lines.Add((await AppendAsync(id, invoice.From.Name, ct), "party"));
        foreach (var line in invoice.From.Lines)
            lines.Add((await AppendAsync(id, line, ct), "partyLine"));

        lines.Add((await AppendAsync(id, "Billed to", ct), "label"));
        lines.Add((await AppendAsync(id, invoice.To.Name, ct), "party"));
        foreach (var line in invoice.To.Lines)
            lines.Add((await AppendAsync(id, line, ct), "partyLine"));

        lines.Add((await AppendAsync(id, $"Issued {invoice.Issued}    Due {invoice.Due}", ct), "dates"));

        // The table of billable lines, then the totals stacked under it.
        await AppendTableAsync(id, invoice, ct);

        foreach (var (label, amount, strong) in InvoiceMath.Totals(invoice))
            lines.Add((await AppendAsync(id, $"{label}    {Money(invoice.Currency, amount)}", ct),
                strong ? "total" : "subtotal"));

        if (invoice.Terms.Length > 0)
        {
            lines.Add((await AppendAsync(id, "Payment terms", ct), "label"));
            foreach (var term in invoice.Terms)
                lines.Add((await AppendAsync(id, term, ct), "term"));
        }

        if (invoice.Notes is { Length: > 0 } notes)
            lines.Add((await AppendAsync(id, notes, ct), "notes"));

        if (_design.Logo is { } logo)
            await ApplyAsync(id, new PlanOperation[]
            {
                logo.InsertBefore(lines[0].ParaId, maximumWidth: 160, maximumHeight: 64)
            }, ct);

        await StyleAsync(id, lines, invoice, ct);

        // The dot is coloured last and on its own: a span format, so it wins over the
        // whole-paragraph pass that set the rest of the wordmark in ink.
        if (wordmark is not null)
            await ApplyAsync(id, new PlanOperation[]
            {
                new FormatOp
                {
                    Target = new TextSpanAnchor { ParaId = wordmark, Expect = _design.WordmarkDot },
                    Color = _design.Accent
                }
            }, ct);

    }

    private static string Money(string currency, decimal amount) =>
        InvoiceCurrency.Format(currency, amount);

    private async Task AppendTableAsync(string id, InvoicePlanned invoice, CancellationToken ct)
    {
        var rows = invoice.Lines
            .Select(l => (IReadOnlyList<string>)new[]
            {
                l.Unit is { Length: > 0 } unit ? $"{l.Description} ({unit})" : l.Description,
                l.Quantity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                Money(invoice.Currency, l.UnitPrice),
                Money(invoice.Currency, InvoiceMath.LineTotal(l, InvoiceCurrency.DecimalPlaces(invoice.Currency)))
            })
            .ToList();

        if (rows.Count == 0) return;

        var inspect = await _client.InspectAsync(_connection, id, cancellationToken: ct);
        var last = inspect.Paragraphs.Where(p => p.In is null).Last().ParaId;

        await ApplyAsync(id, new PlanOperation[]
        {
            new InsertTableOp
            {
                Target = new TextSpanAnchor { ParaId = last, Expect = string.Empty },
                Position = InsertPosition.After,
                Table = new TableData
                {
                    Headers = new[] { "Description", "Qty", "Unit price", "Amount" },
                    Rows = rows
                }
            }
        }, ct);

        var after = await _client.InspectAsync(_connection, id, cancellationToken: ct);
        var table = after.Nodes.LastOrDefault(n => n.Kind == "table");
        if (table is null) return;

        var ops = new List<PlanOperation>
        {
            // insertTable applies the TableGrid style, which boxes every cell. Clearing the
            // borders first leaves the one rule below worth drawing.
            new FormatOp
            {
                Target = new NodeAnchor { Kind = "table", Path = table.Path },
                BorderStyle = "none",
                ColumnWidthsPx = Columns
            },
            // A rule under the header row and nothing else.
            new FormatOp
            {
                Target = new NodeAnchor { Kind = "tableRow", Path = table.Path + "/row#0" },
                Bold = true,
                Color = _design.Ink,
                BorderStyle = "single",
                BorderColor = _design.Ink,
                BorderSizeEighths = 8,
                BorderEdges = "bottom"
            }
        };

        // Cells are addressed by position. A Word paragraph is named by its w14 id, which
        // says nothing about which column it is in - so the column has to come from the
        // cell node, not from the paragraph.
        for (var row = 0; row <= rows.Count; row++)
            for (var column = 0; column < 4; column++)
                ops.Add(new FormatOp
                {
                    Target = new NodeAnchor { Kind = "tableCell", Path = $"{table.Path}/cell#{row}/{column}" },
                    FontFamily = _design.TextFont,
                    SizeHalfPoints = _design.DocumentBodySize,
                    Color = row == 0 ? _design.Ink : _design.Body,
                    // Figures right, prose left. A column of money aligned left is the
                    // fastest way to make a total look wrong.
                    Alignment = column == 0 ? "left" : "right"
                });

        await ApplyAsync(id, ops, ct);
    }

    private async Task StyleAsync(
        string id, IReadOnlyList<(string ParaId, string Role)> lines,
        InvoicePlanned invoice, CancellationToken ct)
    {
        var ops = lines
            .Select(line => (PlanOperation)StyleFor(line.ParaId, line.Role))
            .ToList();

        await ApplyAsync(id, ops, ct);

        await ApplyAsync(id, new PlanOperation[]
        {
            new HeaderFooterOp
            {
                Footer = $"{invoice.From.Name} — invoice {invoice.InvoiceNumber}",
                ShowPageNumber = true,
                Alignment = "edges"
            }
        }, ct);

        var inspect = await _client.InspectAsync(_connection, id, cancellationToken: ct);
        var furniture = inspect.Paragraphs
            .Where(p => p.Location is "header" or "footer" && p.Text.Length > 0)
            .Select(p => (PlanOperation)new FormatOp
            {
                Target = new TextSpanAnchor { ParaId = p.ParaId, Expect = string.Empty },
                FontFamily = _design.TextFont,
                SizeHalfPoints = _design.DocumentCaptionSize,
                Color = _design.Muted
            })
            .ToList();

        await ApplyAsync(id, furniture, ct);
    }

    private FormatOp StyleFor(string paraId, string role) => role switch
    {
        // Set small and tight, the way a letterhead sits above a document rather than
        // competing with its title.
        "wordmark" => Style(paraId, _design.TextFont, _design.DocumentSubheadingSize, _design.Ink,
            spaceAfter: 520, bold: true),

        "title" => Style(paraId, _design.DisplayFont, _design.DocumentTitleSize, _design.Ink,
            spaceAfter: 60),
        "reference" => Style(paraId, _design.TextFont, _design.DocumentQuoteSize, _design.Muted,
            spaceAfter: 420),

        // A label is the smallest thing on the page and the only thing in caps: it names the
        // block under it without competing with it.
        "label" => Style(paraId, _design.TextFont, _design.DocumentCaptionSize, _design.AccentText,
            spaceBefore: 260, spaceAfter: 60, bold: true, caps: true),
        "party" => Style(paraId, _design.TextFont, _design.DocumentSubheadingSize, _design.Ink,
            spaceAfter: 20, bold: true),
        "partyLine" => Style(paraId, _design.TextFont, _design.DocumentBodySize, _design.Body,
            spaceAfter: 20),
        "dates" => Style(paraId, _design.TextFont, _design.DocumentBodySize, _design.Body,
            spaceBefore: 260, spaceAfter: 260),

        "subtotal" => Style(paraId, _design.TextFont, _design.DocumentBodySize, _design.Body,
            spaceAfter: 40, alignment: "right"),

        // The one figure the reader came for.
        "total" => Style(paraId, _design.DisplayFont, _design.DocumentHeadingSize, _design.Ink,
            spaceBefore: 120, spaceAfter: 320, alignment: "right",
            border: _design.Accent, borderEdges: "top"),

        "term" => Style(paraId, _design.TextFont, _design.DocumentBodySize, _design.Body,
            spaceAfter: 60, list: "bullet"),
        "notes" => Style(paraId, _design.TextFont, _design.DocumentBodySize, _design.Muted,
            spaceBefore: 260, italic: true),
        _ => Style(paraId, _design.TextFont, _design.DocumentBodySize, _design.Body, spaceAfter: 120)
    };

    private FormatOp Style(
        string paraId, string font, int size, string color,
        int? spaceBefore = null, int? spaceAfter = null, string? alignment = null,
        bool bold = false, bool italic = false, bool caps = false,
        string? border = null, string? borderEdges = null, string? list = null) => new()
        {
            Target = new TextSpanAnchor { ParaId = paraId, Expect = string.Empty },
            FontFamily = font,
            SizeHalfPoints = caps ? size - 2 : size,
            Color = color,
            Bold = bold ? true : null,
            Italic = italic ? true : null,
            Alignment = alignment,
            SpacingBeforeTwips = spaceBefore,
            SpacingAfterTwips = spaceAfter,
            ListStyle = list,
            ListLevel = list is null ? null : 0,
            BorderStyle = border is null ? null : "single",
            BorderColor = border,
            BorderSizeEighths = border is null ? null : 8,
            BorderEdges = borderEdges
        };

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<string> FillFirstAsync(string id, string text, CancellationToken ct)
    {
        var inspect = await _client.InspectAsync(_connection, id, cancellationToken: ct);
        var first = inspect.Paragraphs.First(p => p.In is null).ParaId;

        await ApplyAsync(id, new PlanOperation[]
        {
            new ChangeTextOp
            {
                Target = new TextSpanAnchor { ParaId = first, Expect = string.Empty },
                With = text,
                Mode = ChangeMode.Direct
            }
        }, ct);

        var after = await _client.InspectAsync(_connection, id, cancellationToken: ct);
        return after.Paragraphs.First(p => p.In is null).ParaId;
    }

    private async Task<string> AppendAsync(string id, string text, CancellationToken ct)
    {
        var inspect = await _client.InspectAsync(_connection, id, cancellationToken: ct);
        var last = inspect.Paragraphs.Where(p => p.In is null).Last().ParaId;

        await ApplyAsync(id, new PlanOperation[]
        {
            new InsertOp
            {
                Target = new TextSpanAnchor { ParaId = last, Expect = string.Empty },
                Position = InsertPosition.After,
                Text = text
            }
        }, ct);

        var after = await _client.InspectAsync(_connection, id, cancellationToken: ct);
        return after.Paragraphs.Where(p => p.In is null).Last().ParaId;
    }

    private async Task ApplyAsync(string id, IReadOnlyList<PlanOperation> operations, CancellationToken ct)
    {
        if (operations.Count == 0) return;

        var result = await _client.CommitAsync(
            _connection, id, new DocumentPlan { Operations = operations }, cancellationToken: ct);

        if (!result.Committed)
            throw ComposerSession.ReportFailure("apply invoice operations", result.Report);
    }
}
