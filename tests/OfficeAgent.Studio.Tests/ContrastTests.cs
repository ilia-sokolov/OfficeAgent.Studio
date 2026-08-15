using OfficeAgent.Studio;
using Xunit;

namespace OfficeAgent.Studio.Tests;

/// <summary>
/// Every pairing of text colour and ground the composers actually produce, held to the
/// contrast normal text needs.
/// </summary>
/// <remarks>
/// This exists because the failure it catches is invisible to the person making it. A grey
/// that is too light looks refined on the monitor it was chosen on and disappears on paper,
/// in a projector, and for anyone whose eyes are older than the designer's. The palette that
/// shipped before this test reached 3.2:1 on its caption text, which is well under the bar
/// and was not obvious to anybody until it was printed.
/// </remarks>
public class ContrastTests
{
    /// <summary>WCAG AA for normal text. Large text may go to 3.0; nothing here relies on that.</summary>
    private const double Readable = 4.5;

    private static readonly DesignSystem Design = DesignSystem.Default;

    [Theory]
    // Body pages: the wash is checked as rendered, not as the raw colour, because the page
    // never actually prints as dark as the gradient's far corner.
    [InlineData("body on paper", nameof(DesignSystem.Body), nameof(DesignSystem.Paper))]
    [InlineData("caption on paper", nameof(DesignSystem.Muted), nameof(DesignSystem.Paper))]
    [InlineData("subheading on paper", nameof(DesignSystem.AccentText), nameof(DesignSystem.Paper))]
    // The cover, which is the other way up.
    [InlineData("cover title on ink", nameof(DesignSystem.Reverse), nameof(DesignSystem.Ink))]
    [InlineData("cover subtitle on ink", nameof(DesignSystem.MutedReverse), nameof(DesignSystem.Ink))]
    [InlineData("cover subtitle on deep ink", nameof(DesignSystem.MutedReverse), nameof(DesignSystem.InkDeep))]
    // The stat card on a slide.
    [InlineData("body on wash", nameof(DesignSystem.Body), nameof(DesignSystem.Wash))]
    public void Text_is_readable_against_the_ground_it_sits_on(string label, string foreground, string background)
    {
        var ratio = DesignSystem.Contrast(Value(foreground), Value(background));

        Assert.True(ratio >= Readable,
            $"{label}: {Value(foreground)} on {Value(background)} is {ratio:F2}:1, under {Readable}:1.");
    }

    [Fact]
    public void Small_text_is_readable_where_the_page_wash_is_deepest()
    {
        // The corner of the page where a caption is hardest to read is the only place worth
        // measuring: pass there and the rest of the page follows.
        var ratio = Design.ContrastOnWash;

        Assert.True(ratio >= Readable,
            $"Muted on the rendered wash ({Design.RenderedWash}) is {ratio:F2}:1, under {Readable}:1.");
    }

    [Fact]
    public void The_muted_greys_are_not_interchangeable()
    {
        // Using one grey for both grounds is the mistake this pair exists to prevent: the
        // paper-side grey is unreadable on ink, and vice versa.
        Assert.True(DesignSystem.Contrast(Design.Muted, Design.Ink) < Readable,
            "The paper-side grey should NOT be readable on ink - if it is, one grey would do " +
            "and the pair is pointless.");

        Assert.True(DesignSystem.Contrast(Design.MutedReverse, Design.Paper) < Readable,
            "The ink-side grey should NOT be readable on paper.");
    }

    [Fact]
    public void The_accent_used_for_rules_is_kept_apart_from_the_accent_used_for_words()
    {
        // The bright accent is fine as a shape and marginal as small text. Keeping the two
        // named separately is what stops a subheading quietly picking the wrong one.
        Assert.True(DesignSystem.Contrast(Design.Accent, Design.Paper) < Readable);
        Assert.True(DesignSystem.Contrast(Design.AccentText, Design.Paper) >= Readable);
    }

    [Theory]
    [InlineData(nameof(DesignSystem.Body), nameof(DesignSystem.Paper))]
    [InlineData(nameof(DesignSystem.Body), nameof(DesignSystem.Wash))]
    [InlineData(nameof(DesignSystem.Muted), nameof(DesignSystem.Paper))]
    [InlineData(nameof(DesignSystem.Muted), nameof(DesignSystem.Wash))]
    [InlineData(nameof(DesignSystem.AccentText), nameof(DesignSystem.Paper))]
    [InlineData(nameof(DesignSystem.Reverse), nameof(DesignSystem.Ink))]
    [InlineData(nameof(DesignSystem.MutedReverse), nameof(DesignSystem.Ink))]
    public void A_brand_palette_is_held_to_the_same_bar_as_the_default(string foreground, string background)
    {
        // A brand is a set of colours somebody chose to look right, not a set that was
        // checked. Adopting one without measuring it is how an accent that works as a
        // button ends up as unreadable body text.
        var brand = DesignSystem.Dotaction;
        var fg = Value(foreground, brand);
        var bg = Value(background, brand);
        var ratio = DesignSystem.Contrast(fg, bg);

        Assert.True(ratio >= Readable,
            $"Dotaction {foreground} on {background}: {fg} on {bg} is {ratio:F2}:1, under {Readable}:1.");
    }

    [Fact]
    public void The_brands_bright_signal_is_kept_for_marks_rather_than_words()
    {
        var brand = DesignSystem.Dotaction;

        // dotaction.io ships --signal and --signal-dark as separate tokens, and the reason
        // is measurable: the bright one is a dot or a rule, the dark one is a word.
        Assert.True(DesignSystem.Contrast(brand.Accent, brand.Paper) < Readable);
        Assert.True(DesignSystem.Contrast(brand.AccentText, brand.Paper) >= Readable);
    }

    private static string Value(string property) => Value(property, Design);

    private static string Value(string property, DesignSystem design) =>
        (string)typeof(DesignSystem).GetProperty(property)!.GetValue(design)!;
}
