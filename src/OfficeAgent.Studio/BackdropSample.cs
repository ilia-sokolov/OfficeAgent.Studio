using OfficeAgent.Abstractions;
using OfficeAgent.Core;

namespace OfficeAgent.Studio;

/// <summary>
/// A deck and a document that exist to show what <c>backgroundImage</c> and its
/// <c>opacity</c> actually do.
/// </summary>
/// <remarks>
/// The rest of the demo puts a gradient behind its covers, which proves the plumbing works
/// and demonstrates nothing: text stays readable over a two-stop gradient at any strength.
/// A photographic backdrop is the case the control exists for - at full strength it eats
/// dark text alive, and the same image at 15% is a texture you can set a report on. These
/// samples put the identical image behind the identical words at a range of strengths so
/// the difference is a thing you look at rather than a number you take on trust.
/// </remarks>
public sealed class BackdropSample
{
    /// <summary>
    /// The strengths worth seeing. Below 10% the image stops being visible at all; above
    /// 40% no dark text survives it.
    /// </summary>
    private static readonly double[] Strengths = { 1.0, 0.6, 0.4, 0.25, 0.15, 0.08 };

    private readonly OfficeAgentClient _client;
    private readonly DesignSystem _design;
    private readonly string _connection;

    public BackdropSample(OfficeAgentClient client, DesignSystem design, string connection = "output")
    {
        _client = client;
        _design = design;
        _connection = connection;
    }

    /// <summary>
    /// One slide per strength, each with the same photograph behind the same two lines of
    /// text, plus a control slide with no background at all.
    /// </summary>
    public Task<string> ComposeDeckAsync(string fileName, CancellationToken ct = default) =>
        ComposerSession.RunAsync(
            _client, _connection, fileName, id => ComposeDeckCreatedAsync(id, ct), ct);

    private async Task ComposeDeckCreatedAsync(string id, CancellationToken ct)
    {
        var photograph = Convert.ToBase64String(
            Backdrop.Photograph(_design.Ink, _design.Accent, width: 1280, height: 720));

        // The label is set as the slide's title at insert time. Inserting with no title at
        // all leaves the layout's placeholder unrealised, and there is then no paragraph to
        // address - so the anchor a later changeText would use does not exist yet.
        var inserts = new List<PlanOperation>();
        foreach (var strength in Strengths)
            inserts.Add(new InsertSlideOp
            {
                Slide = new SlideData { Layout = "titleOnly", Title = $"opacity {strength:P0}" }
            });
        await ApplyAsync(id, inserts, ct);

        var inspect = await _client.InspectAsync(_connection, id, cancellationToken: ct);
        var slides = inspect.Nodes.Where(n => n.Kind == "slide").ToList();

        for (var i = 0; i < slides.Count; i++)
        {
            var slideId = uint.Parse(slides[i].Path.Substring("slide#".Length));

            // The blank deck's own first slide is the control: the same words, nothing behind
            // them. Its placeholder is empty, so it is filled rather than replaced.
            var strength = i == 0 ? (double?)null : Strengths[i - 1];

            if (strength is null)
                await ApplyAsync(id, new PlanOperation[]
                {
                    new ChangeTextOp
                    {
                        Target = new TextSpanAnchor { ParaId = $"slide{slideId}/shape2/p0", Expect = string.Empty },
                        With = "No background",
                        Mode = ChangeMode.Direct
                    }
                }, ct);

            if (strength is { } opacity)
                await ApplyAsync(id, new PlanOperation[]
                {
                    new BackgroundImageOp
                    {
                        Target = new NodeAnchor { Kind = "slide", Path = $"slide#{slideId}" },
                        Base64Bytes = photograph,
                        ImageType = "png",
                        Opacity = opacity
                    }
                }, ct);
            else
                await ApplyAsync(id, new PlanOperation[]
                {
                    new FormatOp
                    {
                        Target = new NodeAnchor { Kind = "slide", Path = $"slide#{slideId}" },
                        FillColor = _design.Paper
                    }
                }, ct);

            await ApplyAsync(id, new PlanOperation[]
            {
                new FormatOp
                {
                    Target = new NodeAnchor { Kind = "shape", Path = $"shape#{slideId}/2" },
                    XPx = _design.Margin, YPx = 72,
                    WidthPx = 260, HeightPx = 52,
                    // The label rides on its own chip. Set straight onto the image it is
                    // unreadable at exactly the strength the reader most needs to be told
                    // what they are looking at.
                    FillColor = _design.Paper,
                    LineColor = "none",
                    VerticalAlignment = "middle"
                },
                new FormatOp
                {
                    Target = new TextSpanAnchor { ParaId = $"slide{slideId}/shape2/p0", Expect = string.Empty },
                    FontFamily = _design.TextFont,
                    SizeHalfPoints = _design.CaptionSize,
                    Color = _design.Ink,
                    Alignment = "center"
                }
            }, ct);

            // Dark text and light text, both on the same ground, because which one survives
            // is exactly what the strength decides.
            await AddSampleTextAsync(id, slideId, "Dark text set over the image", 300, _design.Ink, ct);
            await AddSampleTextAsync(id, slideId, "Light text set over the image", 420, _design.Reverse, ct);
        }

    }

    /// <summary>
    /// The same comparison as a document: a full-strength cover and a low-opacity body page,
    /// so the two strengths that matter can be judged at reading size rather than across a room.
    /// </summary>
    public Task<string> ComposeDocumentAsync(string fileName, CancellationToken ct = default) =>
        ComposerSession.RunAsync(
            _client, _connection, fileName, id => ComposeDocumentCreatedAsync(id, ct), ct);

    private async Task ComposeDocumentCreatedAsync(string id, CancellationToken ct)
    {

        // A page background belongs to the section, so a document cannot show six strengths
        // at once the way a deck can. It shows the two that matter: the cover at full
        // strength, and the body at the strength body copy actually survives.
        var lines = new List<(string ParaId, bool Heading)>
        {
            (await FillFirstAsync(id, "Backgrounds at reading size", ct), true)
        };

        foreach (var text in Body)
            lines.Add((await AppendAsync(id, text, ct), false));

        var ops = new List<PlanOperation>();
        for (var i = 0; i < lines.Count; i++)
            ops.Add(new FormatOp
            {
                Target = new TextSpanAnchor { ParaId = lines[i].ParaId, Expect = string.Empty },
                FontFamily = lines[i].Heading ? _design.DisplayFont : _design.TextFont,
                SizeHalfPoints = lines[i].Heading ? _design.DocumentHeadingSize : _design.DocumentBodySize,
                Color = lines[i].Heading ? _design.Reverse : _design.Body,
                SpacingBeforeTwips = lines[i].Heading ? 2400 : null,
                SpacingAfterTwips = lines[i].Heading ? 200 : 180,
                IndentRightTwips = _design.DocumentMeasureInset,
                PageBreakBefore = i == 1
            });

        await ApplyAsync(id, ops, ct);

        var photograph = Convert.ToBase64String(
            Backdrop.Photograph(_design.Ink, _design.Accent, width: 850, height: 1100));

        await ApplyAsync(id, new PlanOperation[]
        {
            new HeaderFooterOp { DifferentFirstPage = true, Header = "Background sample", ShowPageNumber = true, Alignment = "edges" },
            new HeaderFooterOp { Scope = "firstPage", Header = string.Empty, Footer = string.Empty }
        }, ct);

        await ApplyAsync(id, new PlanOperation[]
        {
            new BackgroundImageOp
            {
                Scope = "firstPage",
                Base64Bytes = photograph,
                ImageType = "png"
            },
            new BackgroundImageOp
            {
                Scope = "default",
                Base64Bytes = photograph,
                ImageType = "png",
                Opacity = 0.12
            }
        }, ct);

    }

    private static readonly string[] Body =
    {
        "The page behind this text carries the same photograph as the cover, at 12%. The " +
        "cover carries it at full strength. Nothing else about the two pages differs.",

        "At full strength the image is the subject and text has to get out of its way - " +
        "which is why a cover works and a body page does not. Bring it to a tenth and the " +
        "image stops competing: it becomes a texture, the kind of thing a reader registers " +
        "as quality without noticing it at all.",

        "The number that matters is contrast, not taste. Body copy needs 4.5:1 against the " +
        "lightest place the background reaches, and a photograph reaches much lighter in " +
        "places than a flat colour ever does - which is the whole reason the opacity control " +
        "exists rather than being left to whoever supplies the image.",

        "Everything on this page is arithmetic on pixel coordinates: layered value noise for " +
        "the ridges, a vignette, and film grain. No photograph was shipped to produce it."
    };

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task AddSampleTextAsync(
        string id, uint slideId, string text, int y, string color, CancellationToken ct)
    {
        await ApplyAsync(id, new PlanOperation[]
        {
            new InsertShapeOp
            {
                Target = new NodeAnchor { Kind = "slide", Path = $"slide#{slideId}" },
                Text = new[] { text },
                XPx = _design.Margin, YPx = y,
                WidthPx = _design.ContentWidth, HeightPx = 80
            }
        }, ct);

        var inspect = await _client.InspectAsync(_connection, id, cancellationToken: ct);
        var shape = inspect.Nodes
            .Where(n => n.Kind == "shape" && n.Path.StartsWith($"shape#{slideId}/", StringComparison.Ordinal))
            .OrderBy(n => int.Parse(n.Path.Split('/')[^1]))
            .Last();
        var shapeId = shape.Path.Split('/')[^1];

        await ApplyAsync(id, new PlanOperation[]
        {
            new FormatOp
            {
                Target = new NodeAnchor { Kind = "shape", Path = shape.Path },
                FillColor = "none",
                LineColor = "none"
            },
            new FormatOp
            {
                Target = new TextSpanAnchor { ParaId = $"slide{slideId}/shape{shapeId}/p0", Expect = string.Empty },
                FontFamily = _design.DisplayFont,
                SizeHalfPoints = _design.SubtitleSize,
                Color = color,
                Alignment = "left"
            }
        }, ct);
    }

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
            throw ComposerSession.ReportFailure("apply backdrop sample operations", result.Report);
    }
}
