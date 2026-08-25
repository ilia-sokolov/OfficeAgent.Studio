using System.Globalization;
using System.Text.RegularExpressions;

namespace OfficeAgent.Studio;

/// <summary>
/// Normalizes and validates the model boundary before a composer is allowed to create a file.
/// </summary>
/// <remarks>
/// JSON deserialization proves that a property had the right CLR shape. It does not prove
/// that a discriminator is canonical, that an explicitly-null array is usable, or that a
/// table is rectangular. Model output is untrusted input, so those guarantees live here
/// rather than being rediscovered as null references halfway through composition.
/// </remarks>
internal static partial class PlanValidator
{
    private static readonly string[] SlideKinds =
        { "cover", "section", "statement", "bullets", "stat", "table", "closing" };

    private static readonly string[] BlockKinds =
        { "heading", "subheading", "paragraph", "bullets", "quote", "table" };

    private static readonly string[] CalloutKinds = { "note", "tip", "warning" };

    internal static T NormalizeAndValidate<T>(T plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        object normalized = plan switch
        {
            DeckPlan deck => Deck(deck),
            DocumentPlanned document => Document(document),
            InvoicePlanned invoice => Invoice(invoice),
            ManualPlanned manual => Manual(manual),
            _ => throw new InvalidOperationException($"No plan validator is registered for {typeof(T).Name}.")
        };

        return (T)normalized;
    }

    private static DeckPlan Deck(DeckPlan deck)
    {
        var slides = RequireArray(deck.Slides, "The deck plan has no slides.");
        RequireCount(slides, 8, 10, "The deck must contain 8 to 10 slides.");

        var normalized = slides.Select((slide, index) =>
        {
            if (slide is null) Fail($"Slide {index + 1} is null.");

            var kind = Canonical(slide!.Kind, SlideKinds, $"slide {index + 1} role");
            var title = Required(slide.Title, $"Slide {index + 1} title", 60);
            var bullets = NonNull(slide.Bullets, $"Slide {index + 1} bullets");
            var headers = NonNull(slide.TableHeaders, $"Slide {index + 1} table headers");
            var rows = NonNull(slide.TableRows, $"Slide {index + 1} table rows");

            switch (kind)
            {
                case "bullets":
                    RequireCount(bullets, 3, 4, $"Slide {index + 1} must contain 3 or 4 bullets.");
                    ValidateTextItems(bullets, $"Slide {index + 1} bullet", 90);
                    break;
                case "stat":
                    Required(slide.Stat, $"Slide {index + 1} statistic", 32);
                    Required(slide.StatCaption, $"Slide {index + 1} statistic caption", 160);
                    break;
                case "table":
                    ValidateTable(headers, rows, $"Slide {index + 1}", minimumRows: 3, maximumRows: 5);
                    break;
                case "statement":
                case "closing":
                    Required(slide.Subtitle, $"Slide {index + 1} subtitle", 180);
                    break;
            }

            Required(slide.Notes, $"Slide {index + 1} presenter notes", 2_000);

            return slide with
            {
                Kind = kind,
                Title = title,
                Eyebrow = Optional(slide.Eyebrow),
                Subtitle = Optional(slide.Subtitle),
                Bullets = bullets,
                Stat = Optional(slide.Stat),
                StatCaption = Optional(slide.StatCaption),
                TableHeaders = headers,
                TableRows = rows,
                Notes = Optional(slide.Notes)
            };
        }).ToArray();

        if (normalized[0].Kind != "cover")
            Fail("The first slide must have the 'cover' role.");
        if (normalized[^1].Kind != "closing")
            Fail("The last slide must have the 'closing' role.");
        if (normalized.Count(slide => slide.Kind == "cover") != 1)
            Fail("The deck must contain exactly one cover slide.");
        if (normalized.Count(slide => slide.Kind == "closing") != 1)
            Fail("The deck must contain exactly one closing slide.");

        var sections = normalized.Count(slide => slide.Kind == "section");
        if (sections is < 2 or > 3)
            Fail("The deck must contain 2 or 3 section slides.");

        return deck with
        {
            Title = Required(deck.Title, "Deck title", 120),
            Subtitle = Optional(deck.Subtitle),
            Slides = normalized
        };
    }

    private static DocumentPlanned Document(DocumentPlanned document)
    {
        var meta = NonNull(document.Meta, "Document cover metadata");
        RequireCount(meta, 2, 3, "Document cover metadata must contain 2 or 3 lines.");
        ValidateTextItems(meta, "Document metadata line", 160);

        var blocks = RequireArray(document.Blocks, "The document plan has no blocks.");
        RequireCount(blocks, 12, 18, "The document must contain 12 to 18 blocks.");

        var normalized = blocks.Select((block, index) =>
        {
            if (block is null) Fail($"Document block {index + 1} is null.");

            var kind = Canonical(block!.Kind, BlockKinds, $"document block {index + 1} kind");
            var bullets = NonNull(block.Bullets, $"Document block {index + 1} bullets");
            var headers = NonNull(block.TableHeaders, $"Document block {index + 1} table headers");
            var rows = NonNull(block.TableRows, $"Document block {index + 1} table rows");

            switch (kind)
            {
                case "bullets":
                    RequireCount(bullets, 1, 8, $"Document block {index + 1} must contain 1 to 8 bullets.");
                    ValidateTextItems(bullets, $"Document block {index + 1} bullet", 300);
                    break;
                case "table":
                    ValidateTable(headers, rows, $"Document block {index + 1}", minimumRows: 1, maximumRows: 5);
                    break;
                default:
                    Required(block.Text, $"Document block {index + 1} text", 4_000);
                    break;
            }

            return block with
            {
                Kind = kind,
                Text = Optional(block.Text),
                Bullets = bullets,
                TableHeaders = headers,
                TableRows = rows
            };
        }).ToArray();

        if (normalized[0].Kind != "heading" || normalized[1].Kind != "paragraph")
            Fail("The document must open with a heading followed by a paragraph.");

        for (var i = 1; i < normalized.Length; i++)
        {
            if (IsHeading(normalized[i - 1].Kind) && IsHeading(normalized[i].Kind))
                Fail($"Document blocks {i} and {i + 1} are consecutive headings.");
        }

        if (normalized.Count(block => block.Kind == "quote") != 1)
            Fail("The document must contain exactly one quote block.");
        if (normalized.Count(block => block.Kind == "table") != 1)
            Fail("The document must contain exactly one table block.");

        return document with
        {
            Title = Required(document.Title, "Document title", 160),
            Subtitle = Optional(document.Subtitle),
            Meta = meta,
            Blocks = normalized
        };
    }

    private static InvoicePlanned Invoice(InvoicePlanned invoice)
    {
        if (invoice.From is null) Fail("The invoice has no billing party.");
        if (invoice.To is null) Fail("The invoice has no billed party.");

        var from = Party(invoice.From!, "Billing party");
        var to = Party(invoice.To!, "Billed party");
        var lines = RequireArray(invoice.Lines, "The invoice plan has no line items.");
        RequireCount(lines, 3, 6, "The invoice must contain 3 to 6 line items.");

        var normalizedLines = lines.Select((line, index) =>
        {
            if (line is null) Fail($"Invoice line {index + 1} is null.");
            if (line!.Quantity <= 0) Fail($"Invoice line {index + 1} quantity must be greater than zero.");
            if (line.UnitPrice < 0) Fail($"Invoice line {index + 1} unit price must not be negative.");

            return line with
            {
                Description = Required(line.Description, $"Invoice line {index + 1} description", 240),
                Unit = Optional(line.Unit)
            };
        }).ToArray();

        if (invoice.TaxRatePercent is < 0 or > 100)
            Fail("The invoice tax rate must be between 0 and 100 percent.");

        var issuedText = Required(invoice.Issued, "Invoice issue date", 80);
        var dueText = Required(invoice.Due, "Invoice due date", 80);
        if (!DateTime.TryParse(issuedText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var issued))
            Fail($"The invoice issue date '{issuedText}' is not a readable date.");
        if (!DateTime.TryParse(dueText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var due))
            Fail($"The invoice due date '{dueText}' is not a readable date.");
        if (due.Date <= issued.Date)
            Fail("The invoice due date must be after the issue date.");

        var terms = NonNull(invoice.Terms, "Invoice payment terms");
        RequireCount(terms, 2, 3, "The invoice must contain 2 or 3 payment terms.");
        ValidateTextItems(terms, "Invoice payment term", 300);

        return invoice with
        {
            From = from,
            To = to,
            InvoiceNumber = Required(invoice.InvoiceNumber, "Invoice number", 80),
            Issued = issuedText,
            Due = dueText,
            Currency = InvoiceCurrency.Normalize(invoice.Currency),
            Lines = normalizedLines,
            TaxLabel = invoice.TaxRatePercent > 0
                ? Required(invoice.TaxLabel, "Invoice tax label", 40)
                : Optional(invoice.TaxLabel) ?? "Tax",
            Terms = terms,
            Notes = Optional(invoice.Notes)
        };
    }

    private static ManualPlanned Manual(ManualPlanned manual)
    {
        var meta = NonNull(manual.Meta, "Manual cover metadata");
        RequireCount(meta, 2, 3, "Manual cover metadata must contain 2 or 3 lines.");
        ValidateTextItems(meta, "Manual metadata line", 160);

        var sections = RequireArray(manual.Sections, "The manual plan has no sections.");
        RequireCount(sections, 4, 6, "The manual must contain 4 to 6 sections.");

        var normalized = sections.Select((section, sectionIndex) =>
        {
            if (section is null) Fail($"Manual section {sectionIndex + 1} is null.");
            var heading = Unnumbered(section!.Heading, $"Manual section {sectionIndex + 1} heading", 160);

            var intro = NonNull(section.Intro, $"Manual section {sectionIndex + 1} introduction");
            RequireCount(intro, 1, 2, $"Manual section {sectionIndex + 1} must have 1 or 2 introduction paragraphs.");
            ValidateTextItems(intro, $"Manual section {sectionIndex + 1} introduction", 4_000);

            var procedures = NonNull(section.Procedures, $"Manual section {sectionIndex + 1} procedures");
            RequireCount(procedures, 1, 2, $"Manual section {sectionIndex + 1} must have 1 or 2 procedures.");
            var normalizedProcedures = procedures.Select((procedure, procedureIndex) =>
            {
                if (procedure is null)
                    Fail($"Manual section {sectionIndex + 1}, procedure {procedureIndex + 1} is null.");

                var steps = NonNull(
                    procedure!.Steps,
                    $"Manual section {sectionIndex + 1}, procedure {procedureIndex + 1} steps");
                RequireCount(
                    steps, 3, 6,
                    $"Manual section {sectionIndex + 1}, procedure {procedureIndex + 1} must have 3 to 6 steps.");

                var normalizedSteps = steps.Select((step, stepIndex) => Unnumbered(
                    step,
                    $"Manual section {sectionIndex + 1}, procedure {procedureIndex + 1}, step {stepIndex + 1}",
                    600)).ToArray();

                return procedure with
                {
                    Title = Unnumbered(
                        procedure.Title,
                        $"Manual section {sectionIndex + 1}, procedure {procedureIndex + 1} title",
                        200),
                    Steps = normalizedSteps
                };
            }).ToArray();

            CalloutPlanned? callout = null;
            if (section.Callout is { } supplied)
            {
                callout = supplied with
                {
                    Kind = Canonical(supplied.Kind, CalloutKinds, $"Manual section {sectionIndex + 1} callout kind"),
                    Text = Required(supplied.Text, $"Manual section {sectionIndex + 1} callout text", 800)
                };
            }

            return section with
            {
                Heading = heading,
                Intro = intro,
                Procedures = normalizedProcedures,
                Callout = callout
            };
        }).ToArray();

        if (normalized.All(section => section.Callout is not null))
            Fail("At least one manual section must remain free of callouts.");

        return manual with
        {
            Title = Required(manual.Title, "Manual title", 160),
            Subtitle = Optional(manual.Subtitle),
            Meta = meta,
            Sections = normalized
        };
    }

    private static PartyPlanned Party(PartyPlanned party, string name)
    {
        var lines = NonNull(party.Lines, $"{name} address lines");
        RequireCount(lines, 2, 4, $"{name} must contain 2 to 4 address or contact lines.");
        ValidateTextItems(lines, $"{name} line", 200);
        return party with { Name = Required(party.Name, $"{name} name", 160), Lines = lines };
    }

    private static void ValidateTable(
        string[] headers, string[][] rows, string name, int minimumRows, int maximumRows)
    {
        RequireCount(headers, 1, 12, $"{name} table must contain 1 to 12 headers.");
        RequireCount(rows, minimumRows, maximumRows, $"{name} table must contain {minimumRows} to {maximumRows} rows.");
        ValidateTextItems(headers, $"{name} table header", 160);

        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row is null) Fail($"{name} table row {rowIndex + 1} is null.");
            row = row!.ToArray();
            rows[rowIndex] = row;
            if (row.Length != headers.Length)
                Fail($"{name} table row {rowIndex + 1} has {row.Length} cells; expected {headers.Length}.");
            ValidateTextItems(row, $"{name} table row {rowIndex + 1} cell", 400);
        }
    }

    private static string Canonical(string? value, string[] known, string name)
    {
        var normalized = Required(value, name, 40).ToLowerInvariant();
        if (!known.Contains(normalized, StringComparer.Ordinal))
            Fail($"Unknown {name} '{value}'. Expected one of: {string.Join(", ", known)}.");
        return normalized;
    }

    private static string Unnumbered(string? value, string name, int maximumLength)
    {
        var text = Required(value, name, maximumLength);
        if (LeadingNumber().IsMatch(text))
            Fail($"{name} must not begin with a number; numbering is added by Word.");
        return text;
    }

    private static string Required(string? value, string name, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) Fail($"{name} is empty.");
        var text = value!.Trim();
        if (text.Length > maximumLength) Fail($"{name} is longer than {maximumLength} characters.");
        return text;
    }

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static T[] RequireArray<T>(T[]? values, string message)
    {
        if (values is null or { Length: 0 }) Fail(message);
        return values!.ToArray();
    }

    private static T[] NonNull<T>(T[]? values, string name)
    {
        if (values is null) Fail($"{name} is null.");
        return values!.ToArray();
    }

    private static void ValidateTextItems(string[] values, string name, int maximumLength)
    {
        for (var index = 0; index < values.Length; index++)
            values[index] = Required(values[index], $"{name} {index + 1}", maximumLength);
    }

    private static void RequireCount<T>(T[] values, int minimum, int maximum, string message)
    {
        if (values.Length < minimum || values.Length > maximum) Fail(message);
    }

    private static bool IsHeading(string kind) => kind is "heading" or "subheading";

    private static void Fail(string message) => throw new InvalidOperationException(message);

    [GeneratedRegex(@"^\s*\d+(?:[.)]|\s)", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingNumber();
}
