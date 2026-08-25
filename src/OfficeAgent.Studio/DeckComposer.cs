using OfficeAgent.Abstractions;
using OfficeAgent.Core;

namespace OfficeAgent.Studio;

/// <summary>
/// Turns a <see cref="DeckPlan"/> into a .pptx by applying the design system to it.
/// </summary>
/// <remarks>
/// Every slide is built the same way: place the content through OfficeAgent's verbs, then
/// paint and position it from <see cref="DesignSystem"/>. Nothing here chooses a colour or
/// a size at the point of use - that is what stops the ninth slide drifting from the first.
/// <para>
/// A slide is composed over several plans rather than one, because a shape's id does not
/// exist until the plan that created it has been applied. The pattern - insert, re-inspect,
/// then style what came back - is the same one any agent driving OfficeAgent will use.
/// </para>
/// </remarks>
public sealed class DeckComposer
{
    private readonly OfficeAgentClient _client;
    private readonly DesignSystem _design;
    private readonly string _connection;

    public DeckComposer(OfficeAgentClient client, DesignSystem design, string connection = "output")
    {
        _client = client;
        _design = design;
        _connection = connection;
    }

    public Task<string> ComposeAsync(DeckPlan plan, string fileName, CancellationToken ct = default) =>
        ComposerSession.RunAsync(
            _client, _connection, fileName, id => ComposeCreatedAsync(plan, id, ct), ct);

    private async Task ComposeCreatedAsync(DeckPlan plan, string id, CancellationToken ct)
    {
        // One insertSlide per plan slide. The blank deck already has slide 1, so the cover
        // reuses it and everything else is appended.
        var inserts = new List<PlanOperation>();
        foreach (var slide in plan.Slides.Skip(1))
            inserts.Add(new InsertSlideOp { Slide = LayoutFor(slide) });
        if (inserts.Count > 0) await ApplyAsync(id, inserts, ct);

        // The cover's own placeholder is empty, so it is filled rather than inserted.
        await FillCoverAsync(id, plan.Slides[0], ct);

        for (var i = 1; i < plan.Slides.Length; i++)
            await StyleSlideAsync(id, i, plan.Slides[i], ct);

        await ApplyAsync(id, new PlanOperation[]
        {
            // A single quiet transition throughout. Varying it per slide is the classic
            // tell of a deck nobody designed.
            new TransitionOp { Effect = "fade", DurationMs = 400 }
        }, ct);

    }

    /// <summary>
    /// Where the parts of a slide sit vertically. One anchor decides all of them, so the
    /// eyebrow, the rule and the title cannot drift into each other the way independent
    /// hand-picked coordinates do - and every slide shares one rhythm rather than five.
    /// </summary>
    private readonly record struct Frame(int Eyebrow, int Rule, int Title, int TitleHeight, int Content)
    {
        public static Frame From(int top, int titleHeight) => new(
            Eyebrow: top,
            Rule: top + 38,
            Title: top + 66,
            TitleHeight: titleHeight,
            Content: top + 66 + titleHeight + 30);
    }

    /// <summary>An eyebrow in the case the brand asks for.</summary>
    private string Eyebrow(string text) =>
        _design.EyebrowUppercase ? text.ToUpperInvariant() : text;

    /// <summary>The eyebrow's own box. Kept under the gap to the rule so it cannot cross it.</summary>
    private const int EyebrowHeight = 30;

    /// <summary>A slide whose title carries the whole slide is set mid-height, not at the top.</summary>
    private static bool IsCentred(SlidePlan slide) =>
        slide.Kind is "section" or "statement" or "closing";

    private static Frame FrameFor(SlidePlan slide) =>
        IsCentred(slide) ? Frame.From(250, 200) : Frame.From(84, 120);

    /// <summary>
    /// Display size, unless the sentence is long enough that display size would wrap it into
    /// a wall. A statement that runs to four lines has stopped being a statement.
    /// </summary>
    private int TitleSizeFor(SlidePlan slide) =>
        IsCentred(slide) && slide.Title.Length <= 46 ? _design.DisplaySize : _design.TitleSize;

    /// <summary>The layout each slide role is built on, and the text it starts with.</summary>
    private static SlideData LayoutFor(SlidePlan slide) => slide.Kind switch
    {
        "section" => new SlideData { Layout = "titleOnly", Title = slide.Title, Notes = slide.Notes },
        "statement" => new SlideData { Layout = "titleOnly", Title = slide.Title, Notes = slide.Notes },
        "stat" => new SlideData { Layout = "titleOnly", Title = slide.Title, Notes = slide.Notes },
        "table" => new SlideData { Layout = "titleOnly", Title = slide.Title, Notes = slide.Notes },
        "closing" => new SlideData { Layout = "titleOnly", Title = slide.Title, Notes = slide.Notes },
        _ => new SlideData
        {
            Layout = "titleAndContent",
            Title = slide.Title,
            Body = slide.Bullets.Length > 0 ? slide.Bullets : new[] { slide.Subtitle ?? string.Empty },
            Notes = slide.Notes
        }
    };

    // ── cover ─────────────────────────────────────────────────────────────────

    private async Task FillCoverAsync(string id, SlidePlan cover, CancellationToken ct)
    {
        var inspect = await _client.InspectAsync(_connection, id, cancellationToken: ct);
        var slideId = SlideIdAt(inspect, 0);

        await ApplyAsync(id, new PlanOperation[]
        {
            new ChangeTextOp
            {
                Target = new TextSpanAnchor { ParaId = $"slide{slideId}/shape2/p0", Expect = string.Empty },
                With = cover.Title,
                Mode = ChangeMode.Direct
            },
        }, ct);

        // A gradient rather than a flat fill. The cover is the one slide a reader looks at
        // rather than reads, and a single unbroken rectangle of ink looks unfinished at
        // full screen where every imperfection has room to show.
        await ApplyAsync(id, new PlanOperation[]
        {
            new BackgroundImageOp
            {
                Target = Slide(slideId),
                Base64Bytes = Convert.ToBase64String(
                    Backdrop.Gradient(
                        _design.CoverBackgroundStart,
                        _design.CoverBackgroundEnd,
                        lift: _design.CoverLift,
                        logo: _design.Logo)),
                ImageType = "png"
            }
        }, ct);

        // The title box is moved off the layout's centred position into the lower-left,
        // which is where a cover reads from.
        var frame = Frame.From(250, 200);
        var titleShape = $"shape#{slideId}/2";
        await ApplyAsync(id, new PlanOperation[]
        {
            new FormatOp
            {
                Target = Shape(titleShape),
                XPx = _design.Margin, YPx = frame.Title,
                WidthPx = _design.ContentWidth - 160, HeightPx = frame.TitleHeight
            },
            new FormatOp
            {
                Target = new TextSpanAnchor { ParaId = $"slide{slideId}/shape2/p0", Expect = string.Empty },
                FontFamily = _design.DisplayFont,
                SizeHalfPoints = _design.DisplaySize,
                Color = _design.CoverTitleColor,
                Alignment = "left"
            }
        }, ct);

        await AddRuleAsync(id, slideId, frame.Rule, _design.Accent, ct);

        if (cover.Subtitle is { Length: > 0 } subtitle)
            await AddTextAsync(id, slideId, subtitle, frame.Content, _design.SubtitleSize,
                _design.TextFont, _design.CoverMutedColor, ct);

        if (cover.Eyebrow is { Length: > 0 } eyebrow)
            await AddTextAsync(id, slideId, Eyebrow(eyebrow), frame.Eyebrow,
                _design.CaptionSize, _design.TextFont, _design.CoverEyebrowColor, ct, EyebrowHeight);
    }

    // ── the rest ──────────────────────────────────────────────────────────────

    private async Task StyleSlideAsync(string id, int index, SlidePlan slide, CancellationToken ct)
    {
        var inspect = await _client.InspectAsync(_connection, id, cancellationToken: ct);
        var slideId = SlideIdAt(inspect, index);
        var reverse = slide.Kind is "section" or "closing";
        var frame = FrameFor(slide);

        var ops = new List<PlanOperation>
        {
            new FormatOp { Target = Slide(slideId), FillColor = reverse ? _design.Ink : _design.Paper },
            new FormatOp
            {
                Target = new TextSpanAnchor { ParaId = $"slide{slideId}/shape2/p0", Expect = string.Empty },
                FontFamily = _design.DisplayFont,
                SizeHalfPoints = TitleSizeFor(slide),
                Color = reverse ? _design.Reverse : _design.Ink,
                Alignment = "left"
            }
        };

        // The body placeholder only exists on the bullets layout.
        if (slide.Kind is "bullets" && slide.Bullets.Length > 0)
            for (var i = 0; i < slide.Bullets.Length; i++)
                ops.Add(new FormatOp
                {
                    Target = new TextSpanAnchor { ParaId = $"slide{slideId}/shape3/p{i}", Expect = string.Empty },
                    FontFamily = _design.TextFont,
                    SizeHalfPoints = _design.BodySize,
                    Color = _design.Body
                });

        await ApplyAsync(id, ops, ct);

        await ApplyAsync(id, new PlanOperation[]
        {
            new FormatOp
            {
                Target = Shape($"shape#{slideId}/2"),
                XPx = _design.Margin,
                YPx = frame.Title,
                WidthPx = _design.ContentWidth,
                HeightPx = frame.TitleHeight
            }
        }, ct);

        if (slide.Kind is "bullets")
            await ApplyAsync(id, new PlanOperation[]
            {
                new FormatOp
                {
                    Target = Shape($"shape#{slideId}/3"),
                    XPx = _design.Margin, YPx = frame.Content,
                    WidthPx = _design.ContentWidth,
                    HeightPx = _design.SlideHeight - _design.Margin - frame.Content,
                    // Three bullets top-anchored in the content area leave the bottom third
                    // of the slide empty. Centring them in it balances the slide without
                    // needing per-slide arithmetic over how many bullets there happen to be.
                    VerticalAlignment = "middle"
                }
            }, ct);

        // The rule sits between the eyebrow and the title on every slide, which is the one
        // mark that makes the deck read as a set.
        await AddRuleAsync(id, slideId, frame.Rule, _design.Accent, ct);

        if (slide.Eyebrow is { Length: > 0 } eyebrow)
            await AddTextAsync(id, slideId, Eyebrow(eyebrow), frame.Eyebrow,
                _design.CaptionSize, _design.TextFont,
                reverse ? _design.AccentReverse : _design.Muted, ct, EyebrowHeight);

        switch (slide.Kind)
        {
            case "stat" when slide.Stat is { Length: > 0 }:
                await AddStatAsync(id, slideId, slide, frame, ct);
                break;
            case "table" when slide.TableHeaders.Length > 0:
                await AddTableAsync(id, slideId, slide, frame, ct);
                break;
            case "statement" or "closing" when slide.Subtitle is { Length: > 0 } line:
                await AddTextAsync(id, slideId, line, frame.Content, _design.SubtitleSize,
                    _design.TextFont, reverse ? _design.MutedReverse : _design.Body, ct);
                break;
        }
    }

    /// <summary>A statistic on a wash card - the one element allowed to shout.</summary>
    private async Task AddStatAsync(string id, uint slideId, SlidePlan slide, Frame frame, CancellationToken ct)
    {
        // The card runs the full content width. A narrow card on a wide slide reads as a
        // placeholder somebody forgot to finish.
        const int cardHeight = 230;

        await ApplyAsync(id, new PlanOperation[]
        {
            new InsertShapeOp
            {
                Target = Slide(slideId),
                Text = new[] { slide.Stat! },
                XPx = _design.Margin, YPx = frame.Content,
                WidthPx = _design.ContentWidth, HeightPx = cardHeight
            }
        }, ct);

        var card = await NewestShapeAsync(id, slideId, ct);
        await ApplyAsync(id, new PlanOperation[]
        {
            new FormatOp
            {
                Target = Shape(card.Path),
                FillColor = _design.Wash,
                LineColor = "none",
                // Without this the number sits against the top edge and the card looks
                // like a box that failed to fill rather than a card.
                VerticalAlignment = "middle"
            },
            new FormatOp
            {
                Target = new TextSpanAnchor { ParaId = card.FirstParagraph, Expect = string.Empty },
                FontFamily = _design.DisplayFont,
                SizeHalfPoints = _design.StatSize,
                Color = _design.Accent,
                Alignment = "center"
            }
        }, ct);

        if (slide.StatCaption is { Length: > 0 } caption)
            await AddTextAsync(id, slideId, caption, frame.Content + cardHeight + 24,
                _design.BodySize, _design.TextFont, _design.Muted, ct);
    }

    private async Task AddTableAsync(string id, uint slideId, SlidePlan slide, Frame frame, CancellationToken ct)
    {
        await ApplyAsync(id, new PlanOperation[]
        {
            new InsertTableOp
            {
                Target = Slide(slideId),
                Table = new TableData
                {
                    Headers = slide.TableHeaders,
                    Rows = slide.TableRows.Select(r => (IReadOnlyList<string>)r).ToList(),
                    // No rules at all. The default boxed grid is the single thing that most
                    // makes a slide look like a spreadsheet somebody pasted in; alignment
                    // and a bold header row do the separating instead.
                    StyleId = "none"
                }
            }
        }, ct);

        var inspect = await _client.InspectAsync(_connection, id, cancellationToken: ct);
        var table = inspect.Nodes.LastOrDefault(n => n.Kind == "table" && n.Path.Contains($"#{slideId}/"));
        if (table is null) return;

        var graphicFrame = "shape#" + table.Path.Substring("table#".Length);
        await ApplyAsync(id, new PlanOperation[]
        {
            new FormatOp
            {
                Target = Shape(graphicFrame),
                XPx = _design.Margin, YPx = frame.Content,
                WidthPx = _design.ContentWidth,
                HeightPx = _design.SlideHeight - _design.Margin - frame.Content
            }
        }, ct);

        // Style every cell: header row in ink, body in the text face.
        var cells = inspect.Paragraphs
            .Where(p => p.ParaId.StartsWith($"slide{slideId}/", StringComparison.Ordinal) && p.ParaId.Contains("/r"))
            .ToList();

        var ops = cells.Select(cell => (PlanOperation)new FormatOp
        {
            Target = new TextSpanAnchor { ParaId = cell.ParaId, Expect = string.Empty },
            FontFamily = _design.TextFont,
            SizeHalfPoints = _design.BodySize,
            Bold = cell.ParaId.Contains("/r0c"),
            Color = cell.ParaId.Contains("/r0c") ? _design.Ink : _design.Body
        }).ToList();

        if (ops.Count > 0) await ApplyAsync(id, ops, ct);
    }

    // ── primitives ────────────────────────────────────────────────────────────

    /// <summary>The accent rule: a short filled bar, the deck's one recurring mark.</summary>
    private async Task AddRuleAsync(string id, uint slideId, int y, string color, CancellationToken ct)
    {
        await ApplyAsync(id, new PlanOperation[]
        {
            new InsertShapeOp
            {
                Target = Slide(slideId),
                Text = Array.Empty<string>(),
                XPx = _design.Margin, YPx = y, WidthPx = 96, HeightPx = _design.RuleHeight
            }
        }, ct);

        var rule = await NewestShapeAsync(id, slideId, ct);
        await ApplyAsync(id, new PlanOperation[]
        {
            new FormatOp { Target = Shape(rule.Path), FillColor = color, LineColor = "none" }
        }, ct);
    }

    private async Task AddTextAsync(
        string id, uint slideId, string text, int y, int size, string font, string color,
        CancellationToken ct, int? height = null)
    {
        await ApplyAsync(id, new PlanOperation[]
        {
            new InsertShapeOp
            {
                Target = Slide(slideId),
                Text = new[] { text },
                XPx = _design.Margin, YPx = y,
                WidthPx = _design.ContentWidth, HeightPx = height ?? 72
            }
        }, ct);

        var box = await NewestShapeAsync(id, slideId, ct);
        await ApplyAsync(id, new PlanOperation[]
        {
            new FormatOp { Target = Shape(box.Path), FillColor = "none", LineColor = "none" },
            new FormatOp
            {
                Target = new TextSpanAnchor { ParaId = box.FirstParagraph, Expect = string.Empty },
                FontFamily = font, SizeHalfPoints = size, Color = color, Alignment = "left"
            }
        }, ct);
    }

    /// <summary>
    /// The shape just inserted. Its id is only knowable after the plan was applied, which
    /// is why a slide takes several round trips to compose.
    /// </summary>
    private async Task<(string Path, string FirstParagraph)> NewestShapeAsync(
        string id, uint slideId, CancellationToken ct)
    {
        var inspect = await _client.InspectAsync(_connection, id, cancellationToken: ct);
        var shape = inspect.Nodes
            .Where(n => n.Kind == "shape" && n.Path.StartsWith($"shape#{slideId}/", StringComparison.Ordinal))
            .OrderBy(n => int.Parse(n.Path.Split('/')[^1]))
            .Last();

        var shapeId = shape.Path.Split('/')[^1];
        return (shape.Path, $"slide{slideId}/shape{shapeId}/p0");
    }

    private static NodeAnchor Slide(uint slideId) => new() { Kind = "slide", Path = $"slide#{slideId}" };
    private static NodeAnchor Shape(string path) => new() { Kind = "shape", Path = path };

    private static uint SlideIdAt(InspectResult inspect, int index)
    {
        var node = inspect.Nodes.Where(n => n.Kind == "slide").ElementAt(index);
        return uint.Parse(node.Path.Substring("slide#".Length));
    }

    private async Task ApplyAsync(string id, IReadOnlyList<PlanOperation> operations, CancellationToken ct)
    {
        if (operations.Count == 0) return;

        var result = await _client.CommitAsync(
            _connection, id, new DocumentPlan { Operations = operations }, cancellationToken: ct);

        if (!result.Committed)
            throw ComposerSession.ReportFailure("apply deck operations", result.Report);
    }
}
