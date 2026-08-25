using System.Text.Json.Serialization;

namespace OfficeAgent.Studio;

/// <summary>
/// The two document types that are structure rather than prose: an invoice and a manual.
/// </summary>
/// <remarks>
/// A report is a sequence of blocks and the model can shape it freely. These two cannot be
/// shaped freely - an invoice that omits the VAT line is wrong, and a manual whose steps do
/// not renumber is worse than useless the first time somebody inserts a step. So the schemas
/// here are tighter than <see cref="DocumentPlanned"/>: named fields the composer knows how
/// to lay out, rather than a list of blocks it has to interpret.
/// </remarks>
public sealed record InvoicePlanned
{
    /// <summary>Who is billing.</summary>
    [JsonPropertyName("from")]
    public required PartyPlanned From { get; init; }

    /// <summary>Who is being billed.</summary>
    [JsonPropertyName("to")]
    public required PartyPlanned To { get; init; }

    [JsonPropertyName("invoiceNumber")]
    public required string InvoiceNumber { get; init; }

    [JsonPropertyName("issued")]
    public required string Issued { get; init; }

    [JsonPropertyName("due")]
    public required string Due { get; init; }

    /// <summary>The ISO 4217 currency code used to format every amount.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = "GBP";

    [JsonPropertyName("lines")]
    public LineItemPlanned[] Lines { get; init; } = Array.Empty<LineItemPlanned>();

    /// <summary>Tax rate as a percentage, e.g. 20 for 20%. Zero omits the tax line.</summary>
    [JsonPropertyName("taxRatePercent")]
    public decimal TaxRatePercent { get; init; }

    [JsonPropertyName("taxLabel")]
    public string TaxLabel { get; init; } = "VAT";

    /// <summary>Payment terms, bank details, late-payment terms - one line each.</summary>
    [JsonPropertyName("terms")]
    public string[] Terms { get; init; } = Array.Empty<string>();

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}

/// <summary>A billing party: the name, then address and contact lines beneath it.</summary>
public sealed record PartyPlanned
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("lines")]
    public string[] Lines { get; init; } = Array.Empty<string>();
}

/// <summary>One billable line. The amount is computed, never taken from the model.</summary>
/// <remarks>
/// A language model doing arithmetic across a dozen lines is a defect waiting to be signed
/// off by somebody who trusted the total. The composer multiplies and sums.
/// </remarks>
public sealed record LineItemPlanned
{
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; init; } = 1;

    [JsonPropertyName("unit")]
    public string? Unit { get; init; }

    [JsonPropertyName("unitPrice")]
    public decimal UnitPrice { get; init; }
}

// ── manual ────────────────────────────────────────────────────────────────────

/// <summary>A manual: numbered sections, each with prose, procedures and callouts.</summary>
public sealed record ManualPlanned
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    /// <summary>Cover lines: a version, a date, an audience.</summary>
    [JsonPropertyName("meta")]
    public string[] Meta { get; init; } = Array.Empty<string>();

    [JsonPropertyName("sections")]
    public ManualSectionPlanned[] Sections { get; init; } = Array.Empty<ManualSectionPlanned>();
}

/// <summary>One numbered section of a manual.</summary>
public sealed record ManualSectionPlanned
{
    [JsonPropertyName("heading")]
    public required string Heading { get; init; }

    /// <summary>One or two paragraphs introducing the section.</summary>
    [JsonPropertyName("intro")]
    public string[] Intro { get; init; } = Array.Empty<string>();

    /// <summary>Procedures within the section, each a numbered run of steps.</summary>
    [JsonPropertyName("procedures")]
    public ProcedurePlanned[] Procedures { get; init; } = Array.Empty<ProcedurePlanned>();

    /// <summary>An aside the reader must not miss.</summary>
    [JsonPropertyName("callout")]
    public CalloutPlanned? Callout { get; init; }
}

/// <summary>A named procedure: a title and the steps to follow, in order.</summary>
public sealed record ProcedurePlanned
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("steps")]
    public string[] Steps { get; init; } = Array.Empty<string>();
}

/// <summary>A note or a warning. The kind decides the colour, nothing else.</summary>
public sealed record CalloutPlanned
{
    /// <summary><c>note</c>, <c>tip</c>, or <c>warning</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "note";

    [JsonPropertyName("text")]
    public required string Text { get; init; }
}
