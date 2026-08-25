using OfficeAgent.Abstractions;
using OfficeAgent.Core;

namespace OfficeAgent.Studio;

/// <summary>
/// Turns a <see cref="ManualPlanned"/> into a .docx: numbered sections, numbered procedures,
/// and callouts.
/// </summary>
/// <remarks>
/// The numbering is the reason this is a separate composer rather than a variant of the
/// report. A manual is referred to rather than read - "see 3.2", "go back to step 4" - so
/// the numbers have to be Word's own. Every section is one <c>clause</c> list, and every
/// procedure gets a <c>decimal</c> list of its own so its steps start at 1 rather than
/// continuing from the procedure before it.
/// </remarks>
public sealed class ManualComposer
{
    private readonly OfficeAgentClient _client;
    private readonly DesignSystem _design;
    private readonly string _connection;

    /// <summary>
    /// Every section shares this list, so they number 1, 2, 3 down the document and a
    /// section inserted in the middle renumbers the rest.
    /// </summary>
    private const int SectionList = 0;

    public ManualComposer(OfficeAgentClient client, DesignSystem design, string connection = "output")
    {
        _client = client;
        _design = design;
        _connection = connection;
    }

    public Task<string> ComposeAsync(
        ManualPlanned manual, string fileName, CancellationToken ct = default) =>
        ComposerSession.RunAsync(
            _client, _connection, fileName, id => ComposeCreatedAsync(manual, id, ct), ct);

    private async Task ComposeCreatedAsync(
        ManualPlanned manual, string id, CancellationToken ct)
    {
        var lines = new List<Line>();

        lines.Add(new Line(await FillFirstAsync(id, manual.Title, ct), "title"));

        if (manual.Subtitle is { Length: > 0 } subtitle)
            lines.Add(new Line(await AppendAsync(id, subtitle, ct), "subtitle"));

        foreach (var meta in manual.Meta)
            lines.Add(new Line(await AppendAsync(id, meta, ct), "meta"));

        // Each procedure needs its own numbering instance so its steps restart at 1. List 0
        // belongs to the sections, so procedures start at 1.
        var procedureList = 1;

        foreach (var section in manual.Sections)
        {
            lines.Add(new Line(await AppendAsync(id, section.Heading, ct), "section", Level: 0));

            foreach (var paragraph in section.Intro)
                lines.Add(new Line(await AppendAsync(id, paragraph, ct), "body"));

            foreach (var procedure in section.Procedures)
            {
                lines.Add(new Line(await AppendAsync(id, procedure.Title, ct), "procedure", Level: 1));

                foreach (var step in procedure.Steps)
                    lines.Add(new Line(await AppendAsync(id, step, ct), "step", ListId: procedureList));

                procedureList++;
            }

            if (section.Callout is { } callout)
                lines.Add(new Line(
                    await AppendAsync(id, $"{Label(callout.Kind)}  {callout.Text}", ct),
                    "callout", Kind: callout.Kind));
        }

        await StyleAsync(id, lines, manual, ct);
    }

    /// <summary>One written paragraph and everything the styling pass needs to know about it.</summary>
    private readonly record struct Line(
        string ParaId, string Role, int Level = 0, int ListId = 0, string? Kind = null);

    private static string Label(string kind) => kind.ToLowerInvariant() switch
    {
        "warning" => "WARNING",
        "tip" => "TIP",
        _ => "NOTE"
    };

    private async Task StyleAsync(
        string id, IReadOnlyList<Line> lines, ManualPlanned manual, CancellationToken ct)
    {
        var ops = new List<PlanOperation>();

        // The first section starts a new page, so the cover stays a cover.
        var firstSection = lines.ToList().FindIndex(l => l.Role == "section");

        for (var i = 0; i < lines.Count; i++)
            ops.Add(StyleFor(lines[i], pageBreak: i == firstSection));

        await ApplyAsync(id, ops, ct);

        await ApplyAsync(id, new PlanOperation[]
        {
            new HeaderFooterOp
            {
                DifferentFirstPage = true,
                Header = manual.Title,
                Footer = manual.Meta.FirstOrDefault() ?? manual.Title,
                ShowPageNumber = true,
                Alignment = "edges"
            },
            new HeaderFooterOp { Scope = "firstPage", Header = string.Empty, Footer = string.Empty }
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

    private FormatOp StyleFor(Line line, bool pageBreak)
    {
        var op = line.Role switch
        {
            "title" => Style(line, _design.DisplayFont, _design.DocumentTitleSize, _design.Ink,
                spaceBefore: 2400, spaceAfter: 160),
            "subtitle" => Style(line, _design.TextFont, _design.DocumentQuoteSize, _design.Muted,
                spaceAfter: 400),
            "meta" => Style(line, _design.TextFont, _design.DocumentCaptionSize, _design.Muted,
                spaceAfter: 60),

            // Sections and procedures share one clause list, so a procedure is 2.1 under
            // section 2 - the number a reader can be pointed at.
            "section" => Style(line, _design.DisplayFont, _design.DocumentHeadingSize, _design.Ink,
                spaceBefore: 400, spaceAfter: 140, list: "clause", listLevel: 0, listId: SectionList),
            "procedure" => Style(line, _design.TextFont, _design.DocumentSubheadingSize, _design.AccentText,
                spaceBefore: 280, spaceAfter: 100, bold: true,
                list: "clause", listLevel: 1, listId: SectionList),

            // Each procedure's steps are their own list, restarting at 1.
            "step" => Style(line, _design.TextFont, _design.DocumentBodySize, _design.Body,
                spaceAfter: 80, list: "decimal", listLevel: 0, listId: line.ListId),

            "callout" => Style(line, _design.TextFont, _design.DocumentBodySize, _design.Body,
                spaceBefore: 200, spaceAfter: 200, indentLeft: _design.DocumentIndent * 2,
                border: line.Kind?.ToLowerInvariant() == "warning" ? _design.Accent : _design.Muted,
                borderEdges: "left"),

            _ => Style(line, _design.TextFont, _design.DocumentBodySize, _design.Body, spaceAfter: 180)
        };

        return pageBreak ? WithBreak(op) : op;
    }

    private FormatOp Style(
        Line line, string font, int size, string color,
        int? spaceBefore = null, int? spaceAfter = null, int? indentLeft = null,
        bool bold = false, string? border = null, string? borderEdges = null,
        string? list = null, int listLevel = 0, int listId = 0) => new()
        {
            Target = new TextSpanAnchor { ParaId = line.ParaId, Expect = string.Empty },
            FontFamily = font,
            SizeHalfPoints = size,
            Color = color,
            Bold = bold ? true : null,
            SpacingBeforeTwips = spaceBefore,
            SpacingAfterTwips = spaceAfter,
            IndentLeftTwips = indentLeft,
            IndentRightTwips = _design.DocumentMeasureInset,
            ListStyle = list,
            ListLevel = list is null ? null : listLevel,
            ListId = list is null ? null : listId,
            BorderStyle = border is null ? null : "single",
            BorderColor = border,
            BorderSizeEighths = border is null ? null : 12,
            BorderEdges = borderEdges
        };

    /// <summary><c>FormatOp</c> is a class, so there is no <c>with</c> to reach for.</summary>
    private static FormatOp WithBreak(FormatOp op) => new()
    {
        Target = op.Target,
        FontFamily = op.FontFamily,
        SizeHalfPoints = op.SizeHalfPoints,
        Color = op.Color,
        Bold = op.Bold,
        SpacingBeforeTwips = op.SpacingBeforeTwips,
        SpacingAfterTwips = op.SpacingAfterTwips,
        IndentLeftTwips = op.IndentLeftTwips,
        IndentRightTwips = op.IndentRightTwips,
        ListStyle = op.ListStyle,
        ListLevel = op.ListLevel,
        ListId = op.ListId,
        BorderStyle = op.BorderStyle,
        BorderColor = op.BorderColor,
        BorderSizeEighths = op.BorderSizeEighths,
        BorderEdges = op.BorderEdges,
        PageBreakBefore = true
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
            throw ComposerSession.ReportFailure("apply manual operations", result.Report);
    }
}
