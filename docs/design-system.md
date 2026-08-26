# Design system reference

`DesignSystem` keeps styling separate from model-generated content. Content agents choose
document roles and text. Composers choose fonts, colors, sizes, spacing, and positions from
one validated system.

Use either a registered C# system or a generated JSON artifact.

## Select a design system

Select one of the registered systems:

```powershell
$env:OFFICEAGENT_STUDIO_BRAND = "meridian"
dotnet run --project src/OfficeAgent.Studio -- backdrop
```

Generate and reuse a JSON system:

```powershell
dotnet run --project src/OfficeAgent.Studio -- design-system `
  "Restrained Dutch technology consultancy; precise, modern, and credible"

$env:OFFICEAGENT_STUDIO_BRAND_FILE = "C:\brand\design-system.json"
dotnet run --project src/OfficeAgent.Studio -- both "Quarterly review"
```

Don't set `OFFICEAGENT_STUDIO_BRAND` and `OFFICEAGENT_STUDIO_BRAND_FILE` together.

## Define a registered system

Create a value from `DesignSystem.Default`, then add it to `DesignSystem.Brands`:

```csharp
public static readonly DesignSystem Acme = Default with
{
    Ink = "1A1A1A",
    Paper = "FFFFFF",
    Accent = "0057B8",
    AccentText = "004489",
    AccentReverse = "5FA8FF",
    DisplayFont = "Georgia",
    TextFont = "Arial",
    Wordmark = "acme",
    CoverMode = CoverMode.Light
};
```

Registration makes the system available to the CLI and includes it in the contrast theory
data. Run `dotnet test` after adding or changing a system.

## Properties that affect output

| Group | Properties |
| --- | --- |
| Palette | `Ink`, `InkDeep`, `Paper`, `Wash`, `WashDeep`, `Body`, `Muted`, `MutedReverse`, `Accent`, `AccentText`, `AccentReverse`, `Reverse` |
| Slide type | `DisplayFont`, `TextFont`, `DisplaySize`, `TitleSize`, `SubtitleSize`, `BodySize`, `CaptionSize`, `StatSize` |
| Document type | `DocumentTitleSize`, `DocumentHeadingSize`, `DocumentSubheadingSize`, `DocumentBodySize`, `DocumentQuoteSize`, `DocumentCaptionSize` |
| Measures | `Margin`, `RuleHeight`, `DocumentMeasureInset`, `DocumentIndent`, `PageBackdropOpacity`, `CoverLift` |
| Cover and mark | `CoverMode`, `Wordmark`, `WordmarkDot`, `EyebrowUppercase` |

Slides and pages use separate type scales because they are read at different distances.
For example, the default slide body is 14 pt and the document body is 10.5 pt.

## Configure covers and logos

`CoverMode` controls the first deck and report cover:

- `Dark` uses the ink ramp and reverse text roles.
- `Light` uses the paper ramp and dark text roles.

Section and closing slides remain dark in either mode. Generated JSON stores `coverMode` as
`dark` or `light`. A schema-version-1 artifact without `coverMode` defaults to `dark`.

Override the selected system for one run:

```powershell
$env:OFFICEAGENT_STUDIO_COVER_MODE = "light"
```

Logo bytes aren't stored in generated JSON. Supply a logo at runtime:

```powershell
$env:OFFICEAGENT_STUDIO_LOGO = "C:\brand\acme-logo.png"
$env:OFFICEAGENT_STUDIO_LOGO_ALT = "Acme logo"
```

The loader accepts bounded, non-interlaced RGB, RGBA, grayscale, and indexed PNGs. Word
uses an image with alt text on report and manual covers and invoice letterheads. PowerPoint
composites the logo into the generated cover backdrop. Without a logo, an invoice uses
`Wordmark` as text.

## Meet contrast requirements

`DesignSystem.Contrast` calculates the WCAG contrast ratio for each text and background
pair. Tests require:

- 4.5:1 for normal text.
- 3:1 for large text, currently only the 48 pt statistic.

The palette separates roles that can't safely share a color:

- `Muted` is secondary text on paper. `MutedReverse` is secondary text on ink.
- `Accent` is a rule or fill and can be bright. `AccentText` is the text-safe paper
  variant. `AccentReverse` is the text-safe ink variant.

Tests also measure composited colors:

- `RenderedWash` blends `WashDeep` over `Paper` at `PageBackdropOpacity`.
- `CoverLightest` applies `CoverLift` to the selected cover start color.
- Cover text is checked against both ends of both light and dark gradients so the runtime
  override remains safe.

See [WCAG 2.2 contrast minimum](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html)
for the thresholds and large-text definition.

## Fixed layout decisions

The following values are literals in composers rather than design-system properties.

### Deck

- The vertical grid and the centered and top-aligned layout origins.
- The 96 px rule length, stat-card height, and caption gap.
- The `Wash` stat-card fill.
- Dark `section` and `closing` slides.
- The 400 ms fade transition.
- The title-length threshold that selects the smaller title size.

### Word documents

- Paragraph spacing, border weights, and quote-indent multiplier.
- Running-head truncation.
- Page size and margins. OfficeAgent.Studio doesn't write them; layout calculations assume
  Letter size and 1-inch margins.

### Not supported

- Line height, letter spacing, and a configurable bullet glyph or table style.
- A configurable slide canvas. `SlideWidth` and `SlideHeight` affect positioning
  calculations but don't resize the OfficeAgent.NET blank deck.
- An Office theme or embedded fonts.

## Validate changes

Run the complete suite:

```bash
dotnet test
```

`ContrastTests.Pairings()` is a maintained list of rendered text and background pairs. Add
a case when a composer introduces a new pairing. The test suite also validates generated
JSON, logo input, cover modes, atomic publication, and Open XML output.

These checks don't replace visual review or a complete accessibility audit. They don't
validate reading order, document language, heading semantics, or PowerPoint alt text for a
logo composited into a background.
