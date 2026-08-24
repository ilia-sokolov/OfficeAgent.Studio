using Microsoft.Extensions.DependencyInjection;
using OfficeAgent.Core;
using OfficeAgent.Core.DocumentProviders;
using OfficeAgent.PowerPoint;
using OfficeAgent.Studio;
using OfficeAgent.Word;

// OfficeAgent.Studio - a design-led document agent over OfficeAgent.NET.
//
//   dotnet run --project src/OfficeAgent.Studio -- deck  "…brief…"
//   dotnet run --project src/OfficeAgent.Studio -- doc   "…brief…"
//   dotnet run --project src/OfficeAgent.Studio -- both
//
// The model decides what the document says; DesignSystem decides how it looks.

var kind = args.Length > 0 ? args[0].ToLowerInvariant() : "both";

// A name this program does not know is a typo, not a request for the default. Running the
// wrong thing silently - or worse, printing "Done." having done nothing - is the least
// helpful thing a CLI can do.
string[] known =
{
    "deck", "doc", "document", "both", "invoice", "manual", "backdrop", "background"
};

if (kind is "-h" or "--help" or "help" || !known.Contains(kind))
{
    var unknown = kind is not ("-h" or "--help" or "help");
    if (unknown) Console.Error.WriteLine($"Unknown command '{args[0]}'.\n");

    Console.WriteLine("""
        OfficeAgent.Studio - designed .pptx and .docx from a one-line brief.

          dotnet run --project src/OfficeAgent.Studio -- <command> ["brief"]

        Commands
          both              a deck and a report from the same brief (default)
          deck              a PowerPoint deck, 8-10 slides
          doc, document     a Word report, 12-18 blocks
          invoice           a Word invoice; totals are computed, not by the model
          manual            a Word manual with numbered sections and steps
          backdrop          background-opacity samples; makes NO model call,
          (background)      so it is the quickest way to prove the build works

        The brief is optional. Without one, a sample quarterly-review brief is used.

        Environment
          OFFICEAGENT_STUDIO_OUTPUT   where files are written (default ./output)
          OFFICEAGENT_STUDIO_BRAND    palette: default, meridian
          OFFICEAGENT_STUDIO_CLIENT   name on the cover and in the footer

        Examples
          dotnet run --project src/OfficeAgent.Studio -- deck "Series B narrative for a robotics company"
          dotnet run --project src/OfficeAgent.Studio -- backdrop
        """);

    return unknown ? 2 : 0;
}

var request = args.Length > 1
    ? string.Join(' ', args.Skip(1))
    : "A quarterly business review for the board: how the year is tracking against plan, "
      + "where the two misses are, what we are doing about them, and what we need decided.";

var output = Path.GetFullPath(
    Environment.GetEnvironmentVariable("OFFICEAGENT_STUDIO_OUTPUT") ?? "output");
Directory.CreateDirectory(output);

var services = new ServiceCollection()
    .AddWordFormat()
    .AddPowerPointFormat()
    .AddFileSystemDocumentProvider("output", output, o =>
    {
        o.AllowedExtensions = new[] { ".docx", ".pptx" };
        // A deck has no redline vocabulary, and this connection only ever authors new
        // files, so tracked changes would refuse every edit.
        o.DefaultChangeMode = OfficeAgent.Abstractions.ChangeMode.Direct;
    })
    .AddOfficeAgent()
    .BuildServiceProvider();

var client = services.GetRequiredService<OfficeAgentClient>();

// OFFICEAGENT_STUDIO_BRAND picks a palette. Everything downstream reads the design system,
// so one environment variable rebrands every deck and document the run produces. An
// unknown name stops the run rather than quietly producing the default brand.
DesignSystem design;
try
{
    design = DesignSystem.ByName(Environment.GetEnvironmentVariable("OFFICEAGENT_STUDIO_BRAND"));
}
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}

// ── the model ─────────────────────────────────────────────────────────────────
// This is the one line to change to use a different model. Any IChatClient works;
// ClaudeCodeChatClient is the only file in the project that mentions Claude.
//
//   var chat = new AzureOpenAIClient(endpoint, credential)
//       .GetChatClient("gpt-4o").AsIChatClient();
//
var agent = new StudioAgent(new ClaudeCodeChatClient());

var brief = new Brief
{
    Request = request,
    Client = Environment.GetEnvironmentVariable("OFFICEAGENT_STUDIO_CLIENT") ?? "Northwind Traders",
    Subtitle = null
};

// Seconds, not minutes. The provider never overwrites - a name already in use fails - so a
// minute-resolution stamp turned "run it again to see a different deck", which is the first
// thing anyone does, into a stack trace.
var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

try
{

if (kind is "deck" or "both")
{
    Console.WriteLine("Planning the deck…");
    var plan = await agent.PlanDeckAsync(brief);
    Console.WriteLine($"  {plan.Slides.Length} slides: {string.Join(", ", plan.Slides.Select(s => s.Kind))}");

    Console.WriteLine("Composing…");
    await new DeckComposer(client, design).ComposeAsync(plan, $"deck-{stamp}.pptx");
    Console.WriteLine($"  → {Path.Combine(output, $"deck-{stamp}.pptx")}");
}

if (kind is "doc" or "document" or "both")
{
    Console.WriteLine("Planning the document…");
    var plan = await agent.PlanDocumentAsync(brief);
    Console.WriteLine($"  {plan.Blocks.Length} blocks: {string.Join(", ", plan.Blocks.Select(b => b.Kind).Distinct())}");

    Console.WriteLine("Composing…");
    await new DocumentComposer(client, design).ComposeAsync(plan, $"report-{stamp}.docx", brief.Client);
    Console.WriteLine($"  → {Path.Combine(output, $"report-{stamp}.docx")}");
}

if (kind is "invoice")
{
    Console.WriteLine("Planning the invoice…");
    var plan = await agent.PlanInvoiceAsync(brief);
    Console.WriteLine($"  {plan.Lines.Length} line items, {plan.Terms.Length} terms");

    Console.WriteLine("Composing…");
    await new InvoiceComposer(client, design).ComposeAsync(plan, $"invoice-{stamp}.docx");
    Console.WriteLine($"  → {Path.Combine(output, $"invoice-{stamp}.docx")}");
}

if (kind is "manual")
{
    Console.WriteLine("Planning the manual…");
    var plan = await agent.PlanManualAsync(brief);
    Console.WriteLine(
        $"  {plan.Sections.Length} sections, " +
        $"{plan.Sections.Sum(s => s.Procedures.Length)} procedures, " +
        $"{plan.Sections.Sum(s => s.Procedures.Sum(p => p.Steps.Length))} steps");

    Console.WriteLine("Composing…");
    await new ManualComposer(client, design).ComposeAsync(plan, $"manual-{stamp}.docx");
    Console.WriteLine($"  → {Path.Combine(output, $"manual-{stamp}.docx")}");
}

if (kind is "backdrop" or "background")
{
    // No model call: this one is about the backgrounds, not the words.
    Console.WriteLine("Composing the background samples…");

    var sample = new BackdropSample(client, design);
    await sample.ComposeDeckAsync($"backgrounds-{stamp}.pptx");
    Console.WriteLine($"  → {Path.Combine(output, $"backgrounds-{stamp}.pptx")}");

    await sample.ComposeDocumentAsync($"backgrounds-{stamp}.docx");
    Console.WriteLine($"  → {Path.Combine(output, $"backgrounds-{stamp}.docx")}");
}

Console.WriteLine("Done.");
return 0;

}
catch (OfficeAgent.Studio.StudioException error)
{
    // Anything this program recognised and can explain. A stack trace here would say less
    // than the message and would look like a crash rather than a refusal.
    Console.Error.WriteLine();
    Console.Error.WriteLine(error.Message);
    if (error.Hint is { Length: > 0 } hint) Console.Error.WriteLine(hint);
    return 1;
}
