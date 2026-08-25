using OfficeAgent.Studio;
using Xunit;

namespace OfficeAgent.Studio.Tests;

/// <summary>
/// Every pairing of text colour and ground the composers actually produce, for every brand
/// this build registers, held to the contrast that size of text needs.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the failure it catches is invisible to the person making it. A grey
/// that is too light looks refined on the monitor it was chosen on and disappears on paper,
/// in a projector, and for anyone whose eyes are older than the designer's. The palette that
/// shipped before this test reached 3.2:1 on its caption text.
/// </para>
/// <para>
/// The cases are generated from <see cref="DesignSystem.Brands"/> rather than written out,
/// so a brand added to the registry is measured without anyone remembering to add it here.
/// A test that only checks the palettes its author thought of is a test that passes for
/// everyone else's.
/// </para>
/// </remarks>
public class ContrastTests
{
    /// <summary>WCAG AA for normal text.</summary>
    private const double Readable = 4.5;

    /// <summary>
    /// WCAG AA for large text - 18pt, or 14pt bold. Claimed explicitly by the few places
    /// that qualify, never relied on by accident.
    /// </summary>
    private const double ReadableLarge = 3.0;

    /// <summary>
    /// Each pairing the composers set, named, with the threshold its size earns.
    /// </summary>
    public static TheoryData<string, string, string, string, double> Pairings()
    {
        var data = new TheoryData<string, string, string, string, double>();

        foreach (var (name, brand) in DesignSystem.Brands)
        {
            void Normal(string what, string fg, string bg) => data.Add(name, what, fg, bg, Readable);
            void Large(string what, string fg, string bg) => data.Add(name, what, fg, bg, ReadableLarge);

            // ── document and slide body, on paper and on the wash as rendered ──
            Normal("body on paper", brand.Body, brand.Paper);
            Normal("body on the wash", brand.Body, brand.RenderedWash);
            Normal("caption on paper", brand.Muted, brand.Paper);
            Normal("caption on the wash", brand.Muted, brand.RenderedWash);
            Normal("subheading on paper", brand.AccentText, brand.Paper);
            Normal("subheading on the wash", brand.AccentText, brand.RenderedWash);

            // ── reverse slides and both selectable cover treatments ──
            Normal("reverse title on ink", brand.Reverse, brand.Ink);
            Normal("reverse subtitle on ink", brand.MutedReverse, brand.Ink);
            Normal("reverse subtitle on deep ink", brand.MutedReverse, brand.InkDeep);
            Normal("reverse eyebrow on ink", brand.AccentReverse, brand.Ink);

            foreach (var mode in Enum.GetValues<CoverMode>())
            {
                var cover = brand with { CoverMode = mode };
                Normal($"{mode} cover title on start", cover.CoverTitleColor, cover.CoverLightest);
                Normal($"{mode} cover title on end", cover.CoverTitleColor, cover.CoverBackgroundEnd);
                Normal($"{mode} cover subtitle on start", cover.CoverMutedColor, cover.CoverLightest);
                Normal($"{mode} cover subtitle on end", cover.CoverMutedColor, cover.CoverBackgroundEnd);
                Normal($"{mode} cover eyebrow on start", cover.CoverEyebrowColor, cover.CoverLightest);
                Normal($"{mode} cover eyebrow on end", cover.CoverEyebrowColor, cover.CoverBackgroundEnd);
            }

            // ── the stat card ──
            // The number is set at 48pt, which is large text by any definition, so the
            // large-text threshold is claimed here on purpose rather than by accident.
            Large("stat number on the card", brand.Accent, brand.Wash);
            Normal("stat caption on paper", brand.Muted, brand.Paper);

            // ── the invoice and the manual ──
            Normal("invoice label", brand.AccentText, brand.Paper);
            Normal("invoice total on paper", brand.Ink, brand.Paper);
            Normal("manual callout text", brand.Body, brand.RenderedWash);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Pairings))]
    public void Text_is_readable_against_the_ground_it_sits_on(
        string brand, string what, string foreground, string background, double threshold)
    {
        var ratio = DesignSystem.Contrast(foreground, background);

        Assert.True(ratio >= threshold,
            $"{brand}: {what} - {foreground} on {background} is {ratio:F2}:1, under {threshold}:1.");
    }

    [Fact]
    public void Every_registered_brand_is_measured()
    {
        // The registry is what ByName reads and what Pairings enumerates. If a brand is
        // added as a field but not registered, the CLI cannot select it and this suite
        // cannot see it - so the omission is worth failing on.
        Assert.Contains("default", DesignSystem.Brands.Keys);
        Assert.Contains("meridian", DesignSystem.Brands.Keys);
        Assert.All(DesignSystem.Brands.Values, Assert.NotNull);
    }

    [Fact]
    public void An_unknown_brand_name_is_refused_rather_than_quietly_defaulted()
    {
        // A typo in OFFICEAGENT_STUDIO_BRAND used to produce a correct-looking run in the
        // wrong brand, which is the one failure nobody thinks to check for.
        var error = Assert.Throws<ArgumentException>(() => DesignSystem.ByName("meridain"));

        Assert.Contains("meridain", error.Message);
        Assert.Contains("default, meridian", error.Message);
    }

    [Fact]
    public void No_brand_name_is_the_default_brand()
    {
        Assert.Equal(DesignSystem.Default, DesignSystem.ByName(null));
        Assert.Equal(DesignSystem.Default, DesignSystem.ByName("  "));
    }

    [Theory]
    [MemberData(nameof(BrandNames))]
    public void The_reverse_greys_are_lighter_than_the_paper_greys(string name)
    {
        var brand = DesignSystem.Brands[name];

        // The mistake worth catching is setting MutedReverse to the paper-side grey - the
        // two are then the same value and the ink cover loses its subtitle. Ordering is the
        // right test for that. Requiring each grey to be *unreadable* on the other's ground
        // would also catch it, and would reject a legitimate mid-grey that happens to work
        // on both, so it is not required here.
        Assert.True(Lightness(brand.MutedReverse) > Lightness(brand.Muted),
            $"{name}: MutedReverse ({brand.MutedReverse}) is not lighter than Muted " +
            $"({brand.Muted}); the reverse ramp has to come out the other way up.");
    }

    [Theory]
    [MemberData(nameof(BrandNames))]
    public void The_text_accent_carries_text_and_is_never_the_lighter_of_the_two(string name)
    {
        var brand = DesignSystem.Brands[name];

        // The only hard requirement: whatever is used for accent *words* must be readable.
        Assert.True(DesignSystem.Contrast(brand.AccentText, brand.Paper) >= Readable,
            $"{name}: AccentText ({brand.AccentText}) is " +
            $"{DesignSystem.Contrast(brand.AccentText, brand.Paper):F2}:1 on paper, under {Readable}:1. " +
            "Darken it, or set it to a tone that carries small text.");

        // And they must not be the wrong way round. A brand whose mark accent is already
        // readable is welcome to set AccentText to the same value - the pair exists so that
        // a bright mark *can* have a darker twin, not so that it must.
        Assert.True(Lightness(brand.AccentText) <= Lightness(brand.Accent),
            $"{name}: AccentText ({brand.AccentText}) is lighter than Accent ({brand.Accent}). " +
            "The text tone is the deeper of the two; these look swapped.");
    }

    /// <summary>
    /// Relative lightness, as a number that only has to be comparable. Contrast against
    /// black rises monotonically with luminance, so it orders colours without the design
    /// system needing to expose its luminance function.
    /// </summary>
    private static double Lightness(string hex) => DesignSystem.Contrast(hex, "000000");

    public static TheoryData<string> BrandNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in DesignSystem.Brands.Keys) data.Add(name);
        return data;
    }
}
