using System.Text.Json.Serialization;

namespace OfficeAgent.Studio;

/// <summary>
/// What the model is asked to produce: the words, not the formatting.
/// </summary>
/// <remarks>
/// The split is the whole point of the demo. The model is good at deciding that a slide
/// should carry one statistic rather than five bullets, and bad at remembering that the
/// accent is <c>C8632B</c> and the left margin is 88px on every one of nine slides. So it
/// returns a structured outline and the composer applies the design system to it - which
/// also means the output is reproducible, and a bad slide is a content bug rather than a
/// formatting one.
/// </remarks>
public sealed record Brief
{
    /// <summary>What the deck or document is for, in the requester's own words.</summary>
    public required string Request { get; init; }

    /// <summary>The organisation the work is for, used on the cover and in footers.</summary>
    public required string Client { get; init; }

    /// <summary>Line under the title on the cover.</summary>
    public string? Subtitle { get; init; }
}

/// <summary>One slide the model asked for, before the design system is applied to it.</summary>
public sealed record SlidePlan
{
    /// <summary>
    /// The slide's role, which decides its whole appearance: <c>cover</c>, <c>section</c>,
    /// <c>statement</c>, <c>bullets</c>, <c>stat</c>, <c>table</c>, or <c>closing</c>.
    /// </summary>
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    /// <summary>The one line that has to land. Kept short by the prompt.</summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>A small line above the title - a section number, a date, a label.</summary>
    [JsonPropertyName("eyebrow")]
    public string? Eyebrow { get; init; }

    /// <summary>Supporting line under the title, for cover and statement slides.</summary>
    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    /// <summary>Bullets, for a <c>bullets</c> slide. Three or four, per the prompt.</summary>
    [JsonPropertyName("bullets")]
    public string[] Bullets { get; init; } = Array.Empty<string>();

    /// <summary>The figure a <c>stat</c> slide exists to show, such as <c>EUR 133.4m</c>.</summary>
    [JsonPropertyName("stat")]
    public string? Stat { get; init; }

    /// <summary>What the statistic means, in one line.</summary>
    [JsonPropertyName("statCaption")]
    public string? StatCaption { get; init; }

    /// <summary>Header row for a <c>table</c> slide.</summary>
    [JsonPropertyName("tableHeaders")]
    public string[] TableHeaders { get; init; } = Array.Empty<string>();

    /// <summary>Body rows for a <c>table</c> slide.</summary>
    [JsonPropertyName("tableRows")]
    public string[][] TableRows { get; init; } = Array.Empty<string[]>();

    /// <summary>What the presenter says. Never shown on the slide.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}

/// <summary>The model's whole answer for a deck.</summary>
public sealed record DeckPlan
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    [JsonPropertyName("slides")]
    public required SlidePlan[] Slides { get; init; }
}

/// <summary>One block of a document, before the design system is applied.</summary>
public sealed record BlockPlan
{
    /// <summary>
    /// <c>heading</c>, <c>subheading</c>, <c>paragraph</c>, <c>bullets</c>,
    /// <c>quote</c>, or <c>table</c>.
    /// </summary>
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("bullets")]
    public string[] Bullets { get; init; } = Array.Empty<string>();

    [JsonPropertyName("tableHeaders")]
    public string[] TableHeaders { get; init; } = Array.Empty<string>();

    [JsonPropertyName("tableRows")]
    public string[][] TableRows { get; init; } = Array.Empty<string[]>();
}

/// <summary>The model's whole answer for a document.</summary>
public sealed record DocumentPlanned
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    [JsonPropertyName("meta")]
    public string[] Meta { get; init; } = Array.Empty<string>();

    [JsonPropertyName("blocks")]
    public required BlockPlan[] Blocks { get; init; }
}
