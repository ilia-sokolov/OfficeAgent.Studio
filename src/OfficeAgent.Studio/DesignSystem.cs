namespace OfficeAgent.Studio;

/// <summary>The tonal treatment used only by the first cover surface.</summary>
public enum CoverMode
{
    Dark,
    Light
}

/// <summary>
/// The rules a document obeys, stated once.
/// </summary>
/// <remarks>
/// A language model asked to "make it beautiful" produces something different on every
/// slide: a new colour it liked, a heading two points larger because the words were
/// longer. What reads as designed is not richness but <em>consistency</em> - one palette,
/// one type scale, one margin, applied without exception. So the palette and the scale are
/// data here rather than prose in a prompt, the model is told to pick <em>from</em> them,
/// and <see cref="DeckComposer"/> and <see cref="DocumentComposer"/> do the placing.
/// </remarks>
public sealed record DesignSystem
{
    /// <summary>A restrained studio palette: one dark, one accent, and a grey ramp.</summary>
    public static readonly DesignSystem Default = new();

    /// <summary>
    /// A second, invented brand: a worked example of what a real one looks like.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It exists so the registry has more than one entry. A contrast suite driven by
    /// <see cref="Brands"/> proves nothing about your palette if the only palette it has
    /// ever measured is the one it was tuned against, and a rebranding claim backed by a
    /// single brand is a claim with no evidence behind it.
    /// </para>
    /// <para>
    /// It is deliberately unlike <see cref="Default"/> in every dimension the system
    /// controls - a cooler ink, a blue rather than an orange accent, sans display type
    /// instead of serif - so a run under this name is visibly a different document rather
    /// than the same one in a slightly different colour.
    /// </para>
    /// <para>
    /// Note what the accent pair does here. <c>Accent</c> reaches 6.9:1 on paper, which is
    /// readable as small text, so <c>AccentText</c> is set to the same value: the pair
    /// exists so a bright mark <em>can</em> have a darker twin, not so that every brand
    /// must own two blues.
    /// </para>
    /// </remarks>
    public static readonly DesignSystem Meridian = Default with
    {
        Ink = "10151F",
        InkDeep = "1E2635",       // a cooler deep ink, so the cover gradient has somewhere to go
        Paper = "FFFFFF",
        Wash = "EFF2F6",
        WashDeep = "DFE5EE",
        Body = "1A2030",
        Muted = "5A6478",         // 5.6:1 on paper
        MutedReverse = "AEB8C9",  // 8.9:1 on ink
        Accent = "0057B8",        // 6.9:1 on paper: a mark that also reads as a word
        AccentText = "0057B8",    // no darker twin needed - see the remarks
        AccentReverse = "6FA8FF", // lightened, for text on the ink cover
        Reverse = "FFFFFF",
        DisplayFont = "Verdana",
        TextFont = "Verdana",
        Wordmark = "meridian"
    };

    /// <summary>
    /// Every brand this build knows, by the name the CLI accepts. Add yours here and two
    /// things follow: <see cref="ByName"/> can find it, and the contrast tests check it.
    /// </summary>
    /// <remarks>
    /// A registry rather than a switch, because a switch is invisible to a test. The whole
    /// promise of the contrast suite - that it will tell you your palette is unreadable
    /// before your reader does - only holds if adding a brand is enough to get it measured.
    /// </remarks>
    public static IReadOnlyDictionary<string, DesignSystem> Brands { get; } =
        new Dictionary<string, DesignSystem>(StringComparer.OrdinalIgnoreCase)
        {
            ["default"] = Default,
            ["meridian"] = Meridian
        };

    /// <summary>
    /// Looks a brand up by name. An empty name is the default; an unknown one throws.
    /// </summary>
    /// <remarks>
    /// Falling back silently would mean a typo in <c>OFFICEAGENT_STUDIO_BRAND</c> produced a
    /// correct-looking run in the wrong brand, which is the one failure nobody checks for.
    /// </remarks>
    /// <exception cref="ArgumentException">The name is not one of <see cref="Brands"/>.</exception>
    public static DesignSystem ByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Default;

        if (Brands.TryGetValue(name!.Trim(), out var brand)) return brand;

        // No paramName: this message is printed to a person running the CLI, and
        // ArgumentException appends "(Parameter 'name')" to anything that carries one.
        throw new ArgumentException(
            $"'{name}' is not a brand this build knows. Available: {string.Join(", ", Brands.Keys)}. " +
            "Add yours to DesignSystem.Brands.");
    }

    /// <summary>Near-black used for full-bleed covers and section dividers.</summary>
    public string Ink { get; init; } = "12161C";

    /// <summary>
    /// The single accent. One is a decision; three is a lack of one. This is the value for
    /// rules, fills and display sizes - the places where it is a shape rather than a word.
    /// </summary>
    public string Accent { get; init; } = "C8632B";

    /// <summary>
    /// The accent as body-sized text. The bright accent reaches only 4.0:1 on paper, which
    /// clears the bar for large text and misses it for anything smaller - so a subheading
    /// set in it gets the deeper burnt tone instead of the one used for the rules.
    /// </summary>
    public string AccentText { get; init; } = "A34E1F";

    /// <summary>
    /// The accent on a dark ground. The same problem as the muted pair, one step further:
    /// an accent chosen to read on paper is too dark to read on ink, and darker still
    /// against the corner where the cover gradient lifts toward white. The eyebrow on a
    /// cover or a section slide is set in this.
    /// </summary>
    public string AccentReverse { get; init; } = "E8834A";

    /// <summary>Page and slide background.</summary>
    public string Paper { get; init; } = "FFFFFF";

    /// <summary>Body text on paper.</summary>
    public string Body { get; init; } = "2B3138";

    /// <summary>
    /// Secondary text on paper: eyebrows, captions, meta lines.
    /// </summary>
    /// <remarks>
    /// This is a grey chosen against a threshold, not by eye. The obvious mid-grey for the
    /// job - <c>8A9199</c> - reaches only 3.2:1 on white, and a caption set in it at 9pt is
    /// the first thing a reader gives up on. <see cref="ContrastOnPaper"/> holds it to the
    /// 4.5:1 that normal text needs.
    /// </remarks>
    public string Muted { get; init; } = "676C71";

    /// <summary>
    /// Secondary text on ink. Muted the other way round: a grey dark enough to read on
    /// paper is far too dark to read on a near-black ground, so the reverse side of the
    /// ramp is a separate decision rather than the same value reused.
    /// </summary>
    public string MutedReverse { get; init; } = "A8AEB5";

    /// <summary>A wash for table headers, stat cards, and the page behind body copy.</summary>
    public string Wash { get; init; } = "F4F1ED";

    /// <summary>Text on ink.</summary>
    public string Reverse { get; init; } = "FFFFFF";

    /// <summary>
    /// The wordmark, set as a letterhead above the document's own title. Null leaves the
    /// document unbranded.
    /// </summary>
    /// <remarks>
    /// Drawn as a coloured disc followed by the name. A run of text rather than an image,
    /// so it stays crisp at any zoom and needs no asset in the package - and so this repo
    /// carries no logo it would have to have permission to ship.
    /// </remarks>
    public string? Wordmark { get; init; }

    /// <summary>The disc that opens the wordmark.</summary>
    public string WordmarkDot { get; init; } = "●";

    /// <summary>
    /// Whether eyebrows are set in capitals. Case is a brand decision, not a typographic
    /// default: a wordmark that is deliberately lowercase should not have its eyebrows
    /// shouting, and forcing capitals mangles names with meaningful internal casing.
    /// </summary>
    public bool EyebrowUppercase { get; init; } = true;

    /// <summary>
    /// Whether the cover uses the dark ink ramp or the light paper ramp. Section and
    /// closing slides deliberately remain reverse surfaces in either mode.
    /// </summary>
    public CoverMode CoverMode { get; init; } = CoverMode.Dark;

    /// <summary>
    /// Optional runtime logo. The binary asset is intentionally not stored in a generated
    /// design-system JSON file; it is supplied per run so that artifact stays portable.
    /// </summary>
    public LogoAsset? Logo { get; init; }

    /// <summary>Display face, for titles. A serif against a sans body reads as considered.</summary>
    public string DisplayFont { get; init; } = "Georgia";

    /// <summary>Text face, for body and data.</summary>
    public string TextFont { get; init; } = "Calibri";

    // ── type scale, in half-points, because that is what the format verb takes ──

    /// <summary>Cover title. Big enough to be the only thing on the slide.</summary>
    public int DisplaySize { get; init; } = 108;   // 54pt

    /// <summary>Slide and section titles.</summary>
    public int TitleSize { get; init; } = 72;      // 36pt

    /// <summary>Sub-headings and pull quotes.</summary>
    public int SubtitleSize { get; init; } = 40;   // 20pt

    /// <summary>Body copy.</summary>
    public int BodySize { get; init; } = 28;       // 14pt

    /// <summary>Eyebrows, captions, footers. Small, wide, muted.</summary>
    public int CaptionSize { get; init; } = 20;    // 10pt

    /// <summary>A statistic that has to carry a slide on its own.</summary>
    public int StatSize { get; init; } = 96;       // 48pt

    // ── slide geometry, in pixels at 96 DPI on a 1280x720 slide ────────────────

    /// <summary>The left and right margin every element aligns to.</summary>
    public int Margin { get; init; } = 88;

    // ── the page's own scale ───────────────────────────────────────────────────
    //
    // A page is read at arm's length and a slide across a room, so a document needs its
    // own scale rather than the deck's. Set a slide's 14pt body on a page and it reads as
    // large-print; set a slide's 36pt heading on a page and it takes three lines.

    /// <summary>Cover title. One line if the title is short, never more than three.</summary>
    public int DocumentTitleSize { get; init; } = 72;   // 36pt

    /// <summary>Section headings.</summary>
    public int DocumentHeadingSize { get; init; } = 40; // 20pt

    /// <summary>Sub-headings, in the accent.</summary>
    public int DocumentSubheadingSize { get; init; } = 24; // 12pt

    /// <summary>Body copy. The size a report is actually read at.</summary>
    public int DocumentBodySize { get; init; } = 21;   // 10.5pt

    /// <summary>The pull quote - larger than body, smaller than a heading.</summary>
    public int DocumentQuoteSize { get; init; } = 28;  // 14pt

    /// <summary>Cover meta lines: reference, date, status.</summary>
    // 8.5pt was small enough that the grey behind it did the rest of the damage. Contrast
    // and size are the same problem: both decide whether a line gets read.
    public int DocumentCaptionSize { get; init; } = 19; // 9.5pt

    /// <summary>
    /// How far the text stops short of the right margin, in twips. Word's default page
    /// gives a 6.5 inch line, which at 10.5pt runs to about 95 characters - far past the
    /// 60-75 a reader tracks comfortably. Holding the right edge back is what turns a
    /// full-width wall of text into something with a measure.
    /// </summary>
    public int DocumentMeasureInset { get; init; } = 2200;  // ~1.5 inch

    /// <summary>Indent for bullets and the pull quote, in twips.</summary>
    public int DocumentIndent { get; init; } = 340;

    // ── backdrops ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The far corner of the cover's gradient - a warmer, lighter ink, so a dark cover has
    /// depth rather than being one flat rectangle.
    /// </summary>
    public string InkDeep { get; init; } = "23282F";

    /// <summary>
    /// The far corner of the body pages' wash. Deep enough to see - a gradient that ends at
    /// <see cref="Paper"/> is a gradient from white to white, which prints as nothing and
    /// leaves the reader wondering what the background was supposed to be.
    /// </summary>
    public string WashDeep { get; init; } = "EAE4DA";

    /// <summary>
    /// How strongly the wash behind body copy prints. Body text keeps 10:1 against the
    /// darkest corner at this strength, so the page reads as paper rather than as a tint
    /// the text has to fight.
    /// </summary>
    public double PageBackdropOpacity { get; init; } = 0.55;

    // ── contrast ───────────────────────────────────────────────────────────────

    /// <summary>Contrast of <see cref="Muted"/> against the lightest paper.</summary>
    public double ContrastOnPaper => Contrast(Muted, Paper);

    /// <summary>
    /// Contrast of <see cref="Muted"/> against the wash where it is deepest - the corner of
    /// the page where a caption is hardest to read, and the number that actually decides
    /// whether the grey is safe.
    /// </summary>
    public double ContrastOnWash => Contrast(Muted, RenderedWash);

    /// <summary>Contrast of <see cref="MutedReverse"/> against the cover's ink.</summary>
    public double ContrastOnInk => Contrast(MutedReverse, Ink);

    /// <summary>
    /// The wash as the reader actually sees it: <see cref="WashDeep"/> composited over
    /// <see cref="Paper"/> at <see cref="PageBackdropOpacity"/>. Checking type against the
    /// raw wash colour would fail a page that never prints that dark.
    /// </summary>
    public string RenderedWash => Blend(WashDeep, Paper, PageBackdropOpacity);

    /// <summary>
    /// The lightest tone the selected cover backdrop reaches. <c>Backdrop.Gradient</c>
    /// lifts one corner toward white, so text is measured against that rendered tone rather
    /// than only against the unmodified dark or light start colour.
    /// </summary>
    public string CoverLightest => Blend("FFFFFF", CoverBackgroundStart, CoverLift);

    /// <summary>The first cover gradient stop for the selected tonal mode.</summary>
    public string CoverBackgroundStart => CoverMode == CoverMode.Dark ? Ink : Paper;

    /// <summary>The second cover gradient stop for the selected tonal mode.</summary>
    public string CoverBackgroundEnd => CoverMode == CoverMode.Dark ? InkDeep : WashDeep;

    /// <summary>Primary cover text selected as a contrast-safe palette role.</summary>
    public string CoverTitleColor => CoverMode == CoverMode.Dark ? Reverse : Ink;

    /// <summary>Secondary cover text selected as a contrast-safe palette role.</summary>
    public string CoverMutedColor => CoverMode == CoverMode.Dark ? MutedReverse : Body;

    /// <summary>Small accent text selected as a contrast-safe palette role.</summary>
    public string CoverEyebrowColor => CoverMode == CoverMode.Dark ? AccentReverse : AccentText;

    /// <summary>How far the cover gradient lifts toward white at its lightest corner.</summary>
    public double CoverLift { get; init; } = 0.10;

    /// <summary>Composites a colour over a background at the given alpha.</summary>
    public static string Blend(string foreground, string background, double alpha)
    {
        var f = Rgb(foreground);
        var b = Rgb(background);
        var r = (int)Math.Round((f.R * alpha) + (b.R * (1 - alpha)));
        var g = (int)Math.Round((f.G * alpha) + (b.G * (1 - alpha)));
        var l = (int)Math.Round((f.B * alpha) + (b.B * (1 - alpha)));
        return $"{r:X2}{g:X2}{l:X2}";
    }

    /// <summary>
    /// The WCAG contrast ratio between two hex colours, from 1 (identical) to 21
    /// (black on white). Normal text wants 4.5 and large text 3.
    /// </summary>
    /// <remarks>
    /// Worth having in the design system rather than in a reviewer's head: "is this grey
    /// readable" is a question with an arithmetic answer, and the greys that fail are
    /// exactly the ones that look fine to whoever picked them on a bright monitor.
    /// </remarks>
    public static double Contrast(string foreground, string background)
    {
        var a = Luminance(foreground);
        var b = Luminance(background);
        var (lighter, darker) = a > b ? (a, b) : (b, a);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static (int R, int G, int B) Rgb(string hex)
    {
        var value = hex.TrimStart('#');
        return (Convert.ToInt32(value.Substring(0, 2), 16),
                Convert.ToInt32(value.Substring(2, 2), 16),
                Convert.ToInt32(value.Substring(4, 2), 16));
    }

    private static double Luminance(string hex)
    {
        var (red, green, blue) = Rgb(hex);
        var r = Channel(red);
        var g = Channel(green);
        var b = Channel(blue);
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);

        static double Channel(int component)
        {
            var c = component / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
    }

    /// <summary>Slide width.</summary>
    public int SlideWidth { get; init; } = 1280;

    /// <summary>Slide height.</summary>
    public int SlideHeight { get; init; } = 720;

    /// <summary>The width available between the margins.</summary>
    public int ContentWidth => SlideWidth - (Margin * 2);

    /// <summary>The accent rule under a cover title - a thin bar, not a line of text.</summary>
    public int RuleHeight { get; init; } = 6;
}
