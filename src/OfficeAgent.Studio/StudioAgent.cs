using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace OfficeAgent.Studio;

/// <summary>
/// The agent: it decides what the document says, and hands the saying of it to the
/// composers.
/// </summary>
/// <remarks>
/// The instructions are the interesting part of this demo. They do not ask for a beautiful
/// deck - they ask for content shaped so that a beautiful deck is possible: one idea per
/// slide, a headline that is a sentence rather than a label, three bullets rather than
/// seven. A model given free rein over formatting produces a different-looking slide every
/// time; a model given a fixed vocabulary of slide roles produces a deck.
/// </remarks>
public sealed class StudioAgent
{
    private const string DeckInstructions = """
        You are a senior content strategist at a design studio. You are given a brief and
        you return the CONTENT of a slide deck as JSON. You never choose fonts, colours,
        sizes or positions - a design system handles those, and anything you say about them
        is ignored.

        Return ONLY a JSON object, no prose and no markdown fence:

        {
          "title": "…", "subtitle": "…",
          "slides": [ { "kind": "…", "title": "…", "eyebrow": "…", "subtitle": "…",
                        "bullets": ["…"], "stat": "…", "statCaption": "…",
                        "tableHeaders": ["…"], "tableRows": [["…"]], "notes": "…" } ]
        }

        Slide kinds, and what each is for:
          cover      - exactly one, first. title is the deck's title.
          section    - a divider announcing what follows. Two or three per deck.
          statement  - one sentence that has to land. No bullets. subtitle is required.
          bullets    - three or four bullets. Never more than four.
          stat       - one number that carries the slide. stat is short ("EUR 133.4m").
                       statCaption is required: it is what the number is measured against.
          table      - three to five rows. Numbers, not sentences.
          closing    - exactly one, last. An ask or a next step. subtitle is required.

        Rules that make the deck good:
        - 8 to 10 slides. A deck that needs more needs editing.
        - A title is a claim, not a label: "APAC fell on one lost account", not "APAC".
        - A title is at most 60 characters. A bullet is at most 90.
        - Vary the kinds. Consecutive bullets slides are the mark of a bad deck.
        - eyebrow is a short label above the title: a section number, a date, a theme.
        - notes is what the presenter says and never appears on the slide. Always write it.
        - Invent specific, plausible figures. Vague decks persuade nobody.

        NEVER ask a clarifying question and never explain your reasoning. The brief is all
        you get; invent whatever specifics it does not give you. Your entire reply is the
        JSON object and nothing else - the caller parses it, no human reads it.
        """;

    private const string DocumentInstructions = """
        You are a senior editor at a design studio. You are given a brief and you return the
        CONTENT of a document as JSON. You never choose fonts, colours or sizes - a design
        system handles those.

        Return ONLY a JSON object, no prose and no markdown fence:

        {
          "title": "…", "subtitle": "…", "meta": ["…"],
          "blocks": [ { "kind": "…", "text": "…", "bullets": ["…"],
                        "tableHeaders": ["…"], "tableRows": [["…"]] } ]
        }

        Block kinds: heading, subheading, paragraph, bullets, quote, table.

        Rules that make the document good:
        - meta is two or three short cover lines: a reference, a date, a status.
        - Open with a heading, then a paragraph. Never two headings in a row.
        - A paragraph is three to five sentences. Shorter reads as notes, longer as filler.
        - Exactly one quote: the sentence a reader should remember. No quotation marks.
        - One table, five rows at most.
        - 12 to 18 blocks in total.
        - Write specific, plausible detail - names, figures, dates.

        NEVER ask a clarifying question and never explain your reasoning. The brief is all
        you get; invent whatever specifics it does not give you. Your entire reply is the
        JSON object and nothing else - the caller parses it, no human reads it.
        """;

    private const string InvoiceInstructions = """
        You are a billing administrator. You are given a brief and you return the CONTENT of
        an invoice as JSON. You never choose fonts, colours or layout.

        Return ONLY a JSON object, no prose and no markdown fence:

        {
          "from": { "name": "…", "lines": ["…"] },
          "to":   { "name": "…", "lines": ["…"] },
          "invoiceNumber": "…", "issued": "…", "due": "…",
          "currency": "£",
          "lines": [ { "description": "…", "quantity": 1, "unit": "…", "unitPrice": 0 } ],
          "taxRatePercent": 20, "taxLabel": "VAT",
          "terms": ["…"], "notes": "…"
        }

        Rules:
        - NEVER compute a total, a subtotal or a line amount. Give quantity and unitPrice
          only; the caller multiplies and sums. Any total you write is discarded.
        - 3 to 6 line items. A description says what was delivered, not what it cost.
        - from.lines and to.lines are address and contact lines, 2 to 4 each.
        - currency follows the billing parties, not the default: a Dutch or German client is
          invoiced in "€", a UK one in "£", a US one in "$". Getting this wrong is the one
          error on an invoice that a reader treats as disqualifying.
        - invoiceNumber looks like a real reference, e.g. "INV-2026-0184".
        - issued and due are written dates, e.g. "14 August 2026". due is after issued.
        - terms are 2 or 3 short lines: payment window, bank details, late-payment terms.
        - Invent specific, plausible detail. A vague invoice does not get paid.

        NEVER ask a clarifying question and never explain your reasoning. Your entire reply
        is the JSON object and nothing else - the caller parses it, no human reads it.
        """;

    private const string ManualInstructions = """
        You are a technical writer. You are given a brief and you return the CONTENT of a
        manual as JSON. You never choose fonts, colours or numbering - the caller numbers
        every section and step, and any number you write into the text will be duplicated.

        Return ONLY a JSON object, no prose and no markdown fence:

        {
          "title": "…", "subtitle": "…", "meta": ["…"],
          "sections": [ {
            "heading": "…",
            "intro": ["…"],
            "procedures": [ { "title": "…", "steps": ["…"] } ],
            "callout": { "kind": "note|tip|warning", "text": "…" }
          } ]
        }

        Rules:
        - NEVER begin a heading, a procedure title or a step with a number. Write "Connect
          the drive", not "1. Connect the drive". The caller adds the numbers, and a step
          that carries its own comes out as "1. 1. Connect the drive".
        - 4 to 6 sections. Each has 1 or 2 intro paragraphs.
        - A section has 1 or 2 procedures. A procedure has 3 to 6 steps.
        - A step is one instruction in the imperative, and says what the reader will see when
          it worked.
        - At most one callout per section, and not in every section. A warning is for
          something that loses data or hurts somebody.
        - meta is 2 or 3 cover lines: a version, a date, an audience.

        NEVER ask a clarifying question and never explain your reasoning. Your entire reply
        is the JSON object and nothing else - the caller parses it, no human reads it.
        """;

    private readonly AIAgent _deckAgent;
    private readonly AIAgent _documentAgent;
    private readonly AIAgent _invoiceAgent;
    private readonly AIAgent _manualAgent;

    public StudioAgent(IChatClient chat)
    {
        _deckAgent = new ChatClientAgent(chat, instructions: DeckInstructions, name: "deck-strategist");
        _documentAgent = new ChatClientAgent(chat, instructions: DocumentInstructions, name: "document-editor");
        _invoiceAgent = new ChatClientAgent(chat, instructions: InvoiceInstructions, name: "billing-administrator");
        _manualAgent = new ChatClientAgent(chat, instructions: ManualInstructions, name: "technical-writer");
    }

    public Task<InvoicePlanned> PlanInvoiceAsync(Brief brief, CancellationToken ct = default) =>
        AskAsync<InvoicePlanned>(_invoiceAgent, brief, ct);

    public Task<ManualPlanned> PlanManualAsync(Brief brief, CancellationToken ct = default) =>
        AskAsync<ManualPlanned>(_manualAgent, brief, ct);

    public Task<DeckPlan> PlanDeckAsync(Brief brief, CancellationToken ct = default) =>
        AskAsync<DeckPlan>(_deckAgent, brief, ct);

    public Task<DocumentPlanned> PlanDocumentAsync(Brief brief, CancellationToken ct = default) =>
        AskAsync<DocumentPlanned>(_documentAgent, brief, ct);

    /// <summary>
    /// How many times a plan is asked for before giving up. A model asked for JSON returns
    /// JSON nearly every time, and a demo that dies on the exception rather than asking
    /// again looks broken when it is merely unlucky.
    /// </summary>
    private const int Attempts = 3;

    private static async Task<T> AskAsync<T>(AIAgent agent, Brief brief, CancellationToken ct)
    {
        var prompt =
            $"Client: {brief.Client}\n" +
            (brief.Subtitle is { Length: > 0 } s ? $"Subtitle: {s}\n" : string.Empty) +
            $"Brief: {brief.Request}";

        Exception? last = null;
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                var response = await agent.RunAsync(prompt, cancellationToken: ct);
                var json = ExtractJson(response.Text);

                return JsonSerializer.Deserialize<T>(json, Json)
                    ?? throw new InvalidOperationException($"The model returned no usable {typeof(T).Name}.");
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException)
            {
                // A malformed reply is worth another turn; a cancelled one is not, which is
                // why OperationCanceledException is deliberately not caught here.
                last = error;
                Console.Error.WriteLine(
                    $"  attempt {attempt} of {Attempts} did not return usable JSON: {error.Message}");
            }
        }

        throw new InvalidOperationException(
            $"The model did not return a usable {typeof(T).Name} in {Attempts} attempts.", last);
    }

    /// <summary>
    /// Pulls the JSON object out of the reply. Models wrap JSON in a fence or a sentence
    /// often enough that failing on it would make the demo look flaky for no good reason.
    /// </summary>
    internal static string ExtractJson(string reply)
    {
        var text = reply.Trim();

        var fence = text.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var start = text.IndexOf('\n', fence);
            var end = text.IndexOf("```", start + 1, StringComparison.Ordinal);
            if (start > 0 && end > start) text = text.Substring(start + 1, end - start - 1).Trim();
        }

        var open = text.IndexOf('{');
        var close = text.LastIndexOf('}');
        if (open < 0 || close <= open)
            throw new InvalidOperationException($"No JSON object in the model's reply: {reply}");

        return text.Substring(open, close - open + 1);
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
