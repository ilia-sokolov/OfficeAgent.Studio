using OfficeAgent.Abstractions;
using OfficeAgent.Core;

namespace OfficeAgent.Studio;

/// <summary>
/// Turns a <see cref="DocumentPlanned"/> into a .docx by applying the design system to it.
/// </summary>
/// <remarks>
/// A Word document has no shapes to paint, so everything here is typography: one display
/// face for headings, one text face for body, a scale that never varies, and space used
/// deliberately. That is enough - most documents that look bad look bad because the
/// hierarchy is flat and the spacing is accidental, not because they lack colour.
/// </remarks>
public sealed class DocumentComposer
{
    private readonly OfficeAgentClient _client;
    private readonly DesignSystem _design;
    private readonly string _connection;

    public DocumentComposer(OfficeAgentClient client, DesignSystem design, string connection = "output")
    {
        _client = client;
        _design = design;
        _connection = connection;
    }

    public Task<string> ComposeAsync(
        DocumentPlanned plan, string fileName, string? client = null, CancellationToken ct = default) =>
        ComposerSession.RunAsync(
            _client, _connection, fileName, id => ComposeCreatedAsync(plan, id, client, ct), ct);

    private async Task ComposeCreatedAsync(
        DocumentPlanned plan, string id, string? client, CancellationToken ct)
    {
        // Every paragraph is remembered by id and by the role it plays, as it is written.
        // Walking the finished document by position instead looks simpler and is wrong: a
        // table leaves a paragraph behind it, a bullets block writes several, and one such
        // surprise shifts every style after it onto the wrong paragraph.
        var lines = new List<(string ParaId, string Role)>
        {
            // A blank document opens with one empty paragraph. The title is written into it
            // rather than after it, or the document begins with an empty line.
            (await FillFirstAsync(id, plan.Title, ct), "title")
        };

        if (plan.Subtitle is { Length: > 0 })
            lines.Add((await AppendAsync(id, plan.Subtitle, ct), "subtitle"));

        foreach (var line in plan.Meta)
            lines.Add((await AppendAsync(id, line, ct), "meta"));

        foreach (var block in plan.Blocks)
        {
            switch (block.Kind)
            {
                case "bullets":
                    foreach (var bullet in block.Bullets)
                        // No dash typed into the text: the bullet is a real list item, so
                        // Word draws the glyph and owns the hanging indent.
                        lines.Add((await AppendAsync(id, bullet, ct), "bullets"));
                    break;
                case "table":
                    await AppendTableAsync(id, block, ct);
                    break;
                default:
                    if (block.Text is { Length: > 0 })
                        lines.Add((await AppendAsync(id, block.Text, ct), block.Kind));
                    break;
            }
        }

        if (_design.Logo is { } logo)
            await ApplyAsync(id, new PlanOperation[]
            {
                logo.InsertBefore(lines[0].ParaId, maximumWidth: 170, maximumHeight: 72)
            }, ct);

        await StyleAsync(id, lines, ct);
        await DressAsync(id, plan, client, ct);

        var metadata = await _client.CommitAsync(_connection, id, new DocumentPlan
        {
            Operations = new PlanOperation[]
            {
                new SetPropertyOp { Target = new NodeAnchor { Kind = "docProperty", Path = "core/title" }, Value = plan.Title }
            }
        }, cancellationToken: ct);
        if (!metadata.Committed)
            throw ComposerSession.ReportFailure("set document metadata", metadata.Report);
    }

    /// <summary>
    /// The furniture: a running head, a numbered footer, and a wash behind the page.
    /// </summary>
    /// <remarks>
    /// Order matters. The distinct first page is asked for first, because it is what makes
    /// the first-page header exist at all - and the backdrop goes last, because it paints
    /// every header the section turns out to use, including the one the cover needs.
    /// </remarks>
    private async Task DressAsync(string id, DocumentPlanned plan, string? client, CancellationToken ct)
    {
        await ApplyAsync(id, new PlanOperation[]
        {
            // The cover carries no running head. A title page with "page 1" on it is the
            // clearest sign nobody looked at the document before sending it.
            new HeaderFooterOp
            {
                DifferentFirstPage = true,
                Header = Running(plan.Title),
                Footer = client ?? string.Empty,
                ShowPageNumber = true,
                Alignment = "edges"
            },
            new HeaderFooterOp { Scope = "firstPage", Header = string.Empty, Footer = string.Empty }
        }, ct);

        await ApplyAsync(id, new PlanOperation[]
        {
            // The cover is a full-bleed ink page, at full strength, like the deck's. A
            // backdrop the reader cannot see is not restraint - it is a background nobody
            // will believe was intended.
            new BackgroundImageOp
            {
                Scope = "firstPage",
                Base64Bytes = Convert.ToBase64String(
                    Backdrop.Gradient(
                        _design.CoverBackgroundStart,
                        _design.CoverBackgroundEnd,
                        width: 850,
                        height: 1100,
                        lift: _design.CoverLift)),
                ImageType = "png"
            },
            // Behind the body, a warm paper wash. It ends at a tone distinctly off white,
            // because a gradient that ends at the paper colour is a gradient from white to
            // white, and prints as nothing at all.
            new BackgroundImageOp
            {
                Scope = "default",
                Base64Bytes = Convert.ToBase64String(
                    Backdrop.Gradient(_design.Paper, _design.WashDeep, width: 850, height: 1100)),
                ImageType = "png",
                Opacity = _design.PageBackdropOpacity
            }
        }, ct);

        // The running head is set in the caption size and the muted grey, like every other
        // small piece of type in the system.
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

    /// <summary>
    /// A running head is a reminder, not the title again. Anything past a phrase is cut at
    /// the last sensible break rather than mid-word.
    /// </summary>
    private static string Running(string title)
    {
        const int limit = 58;
        if (title.Length <= limit) return title;

        var cut = title.LastIndexOfAny(new[] { ' ', '—', '-', ':' }, limit);
        return (cut > 20 ? title.Substring(0, cut) : title.Substring(0, limit)).TrimEnd(' ', '—', '-', ':') + "…";
    }

    /// <summary>
    /// Styles every paragraph in one pass, by the id recorded when it was written.
    /// Formatting after all the text exists means each operation targets a stable id.
    /// </summary>
    private async Task StyleAsync(
        string id, IReadOnlyList<(string ParaId, string Role)> lines, CancellationToken ct)
    {
        var inspect = await _client.InspectAsync(_connection, id, cancellationToken: ct);
        var ops = new List<PlanOperation>();

        // The first line after the cover block starts a new page, which is what makes the
        // cover a cover rather than a large headline the body runs straight into.
        var body = lines.Select(l => l.Role).ToList().FindIndex(r => r is not ("title" or "subtitle" or "meta"));

        for (var i = 0; i < lines.Count; i++)
            ops.Add(Style(lines[i].ParaId, lines[i].Role, pageBreak: i == body));

        // Table cells, which inspect reports with their containing table in `In`.
        foreach (var cell in inspect.Paragraphs.Where(p => p.In is not null))
            ops.Add(new FormatOp
            {
                Target = new TextSpanAnchor { ParaId = cell.ParaId, Expect = string.Empty },
                FontFamily = _design.TextFont,
                SizeHalfPoints = _design.DocumentBodySize,
                Color = _design.Body
            });

        foreach (var table in inspect.Nodes.Where(n => n.Kind == "table"))
        {
            // Word's default table style boxes every cell. Taking the rules out and putting
            // one back under the header is the same table the deck draws, so a figure looks
            // the same whichever document the reader is holding.
            ops.Add(new FormatOp
            {
                Target = new NodeAnchor { Kind = "table", Path = table.Path },
                BorderStyle = "none"
            });

            ops.Add(new FormatOp
            {
                Target = new NodeAnchor { Kind = "tableRow", Path = table.Path + "/row#0" },
                Bold = true,
                Color = _design.Ink,
                BorderStyle = "single",
                BorderColor = _design.Ink,
                BorderSizeEighths = 8,
                BorderEdges = "bottom"
            });
        }

        await ApplyAsync(id, ops, ct);
    }

    /// <summary>
    /// The design system's one statement about what each kind of line looks like. Every
    /// measure a document needs is here, which is what stops the tenth heading being set
    /// slightly differently from the first.
    /// </summary>
    private FormatOp Style(string paraId, string role, bool pageBreak = false)
    {
        var style = StyleFor(paraId, role);
        return pageBreak ? Break(style) : style;
    }

    /// <summary>
    /// Copies a style with the page break set. <c>FormatOp</c> is a class rather than a
    /// record, so there is no <c>with</c> to reach for.
    /// </summary>
    private static FormatOp Break(FormatOp style) => new()
    {
        Target = style.Target,
        FontFamily = style.FontFamily,
        SizeHalfPoints = style.SizeHalfPoints,
        Color = style.Color,
        Bold = style.Bold,
        Italic = style.Italic,
        SpacingBeforeTwips = style.SpacingBeforeTwips,
        SpacingAfterTwips = style.SpacingAfterTwips,
        IndentLeftTwips = style.IndentLeftTwips,
        IndentFirstLineTwips = style.IndentFirstLineTwips,
        IndentRightTwips = style.IndentRightTwips,
        BorderStyle = style.BorderStyle,
        BorderColor = style.BorderColor,
        BorderSizeEighths = style.BorderSizeEighths,
        BorderEdges = style.BorderEdges,
        ListStyle = style.ListStyle,
        ListLevel = style.ListLevel,
        PageBreakBefore = true
    };

    private FormatOp StyleFor(string paraId, string role) => role switch
    {
        // Cover colours switch as a group: reverse roles on ink, paper-side roles on a
        // light cover. A supplied logo already occupies the top of the page, so the title
        // needs less additional space before it.
        "title" => Style(paraId, _design.DisplayFont, _design.DocumentTitleSize, _design.CoverTitleColor,
            spaceBefore: _design.Logo is null ? 2400 : 900, spaceAfter: 160),
        "subtitle" => Style(paraId, _design.TextFont, _design.DocumentQuoteSize, _design.CoverMutedColor,
            spaceAfter: 400),
        "meta" => Style(paraId, _design.TextFont, _design.DocumentCaptionSize, _design.CoverMutedColor,
            spaceAfter: 60),
        "heading" => Style(paraId, _design.DisplayFont, _design.DocumentHeadingSize, _design.Ink,
            spaceBefore: 400, spaceAfter: 120),
        "subheading" => Style(paraId, _design.TextFont, _design.DocumentSubheadingSize, _design.AccentText,
            spaceBefore: 280, spaceAfter: 80, bold: true),
        // The one block with a rule on it, so a pull quote reads as one.
        // A rule down the left edge only. Bordered on all four it is a callout box, which
        // says "warning" rather than "read this line twice".
        "quote" => Style(paraId, _design.DisplayFont, _design.DocumentQuoteSize, _design.Body,
            spaceBefore: 280, spaceAfter: 280, indentLeft: _design.DocumentIndent * 2,
            italic: true, border: _design.Accent, borderEdges: "left"),
        // A real list item. The glyph, the hanging indent and the wrapped-line alignment all
        // come from the numbering definition, so none of them is arithmetic here.
        "bullets" => Style(paraId, _design.TextFont, _design.DocumentBodySize, _design.Body,
            spaceAfter: 100, list: "bullet"),
        _ => Style(paraId, _design.TextFont, _design.DocumentBodySize, _design.Body,
            spaceAfter: 180)
    };

    private FormatOp Style(
        string paraId, string font, int size, string color,
        int? spaceBefore = null, int? spaceAfter = null, int? indentLeft = null,
        int? indentFirstLine = null,
        bool bold = false, bool italic = false, string? border = null,
        string? borderEdges = null, string? list = null)
    {
        return new FormatOp
        {
            Target = new TextSpanAnchor { ParaId = paraId, Expect = string.Empty },
            FontFamily = font,
            SizeHalfPoints = size,
            Color = color,
            Bold = bold ? true : null,
            Italic = italic ? true : null,
            SpacingBeforeTwips = spaceBefore,
            SpacingAfterTwips = spaceAfter,
            IndentLeftTwips = indentLeft,
            IndentFirstLineTwips = indentFirstLine,
            // Every line stops short of the right margin by the same amount, so the measure
            // is constant down the page and the ragged edge reads as one column.
            IndentRightTwips = _design.DocumentMeasureInset,
            BorderStyle = border is null ? null : "single",
            BorderColor = border,
            BorderSizeEighths = border is null ? null : 12,
            BorderEdges = borderEdges,
            ListStyle = list,
            ListLevel = list is null ? null : 0
        };
    }

    /// <summary>
    /// Writes the title into the empty paragraph a blank document already has, and returns
    /// its id. The document is inspected again afterwards because an empty paragraph has no
    /// <c>w14:paraId</c> of its own: it is named synthetically until it carries text, and
    /// the id it had going in is not the id it has coming out.
    /// </summary>
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

    /// <summary>
    /// Appends a paragraph and returns its id. Text is always added at the end, so the last
    /// body paragraph afterwards is the one just written.
    /// </summary>
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

    private async Task AppendTableAsync(string id, BlockPlan block, CancellationToken ct)
    {
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
                    Headers = block.TableHeaders,
                    Rows = block.TableRows.Select(r => (IReadOnlyList<string>)r).ToList()
                }
            }
        }, ct);
    }

    private async Task ApplyAsync(string id, IReadOnlyList<PlanOperation> operations, CancellationToken ct)
    {
        if (operations.Count == 0) return;

        var result = await _client.CommitAsync(
            _connection, id, new DocumentPlan { Operations = operations }, cancellationToken: ct);

        if (!result.Committed)
            throw ComposerSession.ReportFailure("apply document operations", result.Report);
    }
}
