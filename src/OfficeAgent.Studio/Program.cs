using Microsoft.Extensions.DependencyInjection;
using OfficeAgent.Core;
using OfficeAgent.Core.DocumentProviders;
using OfficeAgent.PowerPoint;
using OfficeAgent.Studio;
using OfficeAgent.Word;

return await StudioProgram.RunAsync(args);

internal static class StudioProgram
{
    private static readonly string[] KnownCommands =
        { "deck", "doc", "document", "both", "invoice", "manual", "backdrop", "background" };

    internal static async Task<int> RunAsync(string[] args)
    {
        var kind = args.Length > 0 ? args[0].ToLowerInvariant() : "both";
        if (kind is "-h" or "--help" or "help" || !KnownCommands.Contains(kind))
        {
            var unknown = kind is not ("-h" or "--help" or "help");
            if (unknown) Console.Error.WriteLine($"Unknown command '{args[0]}'.\n");
            PrintHelp();
            return unknown ? 2 : 0;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var request = args.Length > 1
                ? string.Join(' ', args.Skip(1))
                : "A quarterly business review for the board: how the year is tracking against plan, "
                  + "where the two misses are, what we are doing about them, and what we need decided.";

            var configuredOutput = Environment.GetEnvironmentVariable("OFFICEAGENT_STUDIO_OUTPUT");
            var output = Path.GetFullPath(
                string.IsNullOrWhiteSpace(configuredOutput) ? "output" : configuredOutput);
            Directory.CreateDirectory(output);

            using var services = new ServiceCollection()
                .AddWordFormat()
                .AddPowerPointFormat()
                .AddFileSystemDocumentProvider("output", output, options =>
                {
                    options.AllowedExtensions = new[] { ".docx", ".pptx" };
                    options.DefaultChangeMode = OfficeAgent.Abstractions.ChangeMode.Direct;
                })
                .AddOfficeAgent()
                .BuildServiceProvider();

            var client = services.GetRequiredService<OfficeAgentClient>();
            var design = DesignSystem.ByName(Environment.GetEnvironmentVariable("OFFICEAGENT_STUDIO_BRAND"));
            using var modelClient = kind is "backdrop" or "background"
                ? null
                : ModelClientFactory.CreateFromEnvironment();
            var agent = modelClient is null ? null : new StudioAgent(modelClient);
            var publisher = new OutputTransaction(client, output);

            var configuredClient = Environment.GetEnvironmentVariable("OFFICEAGENT_STUDIO_CLIENT");
            var brief = new Brief
            {
                Request = request,
                Client = string.IsNullOrWhiteSpace(configuredClient) ? "Northwind Traders" : configuredClient.Trim(),
                Subtitle = null
            };

            var stamp = OutputNames.NewStamp();
            var ct = cancellation.Token;

            if (kind is "deck" or "both")
            {
                Console.WriteLine("Planning the deck…");
                var plan = await agent!.PlanDeckAsync(brief, ct);
                Console.WriteLine($"  {plan.Slides.Length} slides: {string.Join(", ", plan.Slides.Select(slide => slide.Kind))}");

                Console.WriteLine("Composing…");
                var name = $"deck-{stamp}.pptx";
                await publisher.ComposeAsync(
                    name,
                    (temporary, token) => new DeckComposer(client, design).ComposeAsync(plan, temporary, token),
                    ct);
                Console.WriteLine($"  → {Path.Combine(output, name)}");
            }

            if (kind is "doc" or "document" or "both")
            {
                Console.WriteLine("Planning the document…");
                var plan = await agent!.PlanDocumentAsync(brief, ct);
                Console.WriteLine(
                    $"  {plan.Blocks.Length} blocks: {string.Join(", ", plan.Blocks.Select(block => block.Kind).Distinct())}");

                Console.WriteLine("Composing…");
                var name = $"report-{stamp}.docx";
                await publisher.ComposeAsync(
                    name,
                    (temporary, token) =>
                        new DocumentComposer(client, design).ComposeAsync(plan, temporary, brief.Client, token),
                    ct);
                Console.WriteLine($"  → {Path.Combine(output, name)}");
            }

            if (kind == "invoice")
            {
                Console.WriteLine("Planning the invoice…");
                var plan = await agent!.PlanInvoiceAsync(brief, ct);
                Console.WriteLine($"  {plan.Lines.Length} line items, {plan.Terms.Length} terms");

                Console.WriteLine("Composing…");
                var name = $"invoice-{stamp}.docx";
                await publisher.ComposeAsync(
                    name,
                    (temporary, token) => new InvoiceComposer(client, design).ComposeAsync(plan, temporary, token),
                    ct);
                Console.WriteLine($"  → {Path.Combine(output, name)}");
            }

            if (kind == "manual")
            {
                Console.WriteLine("Planning the manual…");
                var plan = await agent!.PlanManualAsync(brief, ct);
                Console.WriteLine(
                    $"  {plan.Sections.Length} sections, " +
                    $"{plan.Sections.Sum(section => section.Procedures.Length)} procedures, " +
                    $"{plan.Sections.Sum(section => section.Procedures.Sum(procedure => procedure.Steps.Length))} steps");

                Console.WriteLine("Composing…");
                var name = $"manual-{stamp}.docx";
                await publisher.ComposeAsync(
                    name,
                    (temporary, token) => new ManualComposer(client, design).ComposeAsync(plan, temporary, token),
                    ct);
                Console.WriteLine($"  → {Path.Combine(output, name)}");
            }

            if (kind is "backdrop" or "background")
            {
                Console.WriteLine("Composing the background samples…");
                var sample = new BackdropSample(client, design);

                var deckName = $"backgrounds-{stamp}.pptx";
                await publisher.ComposeAsync(
                    deckName,
                    (temporary, token) => sample.ComposeDeckAsync(temporary, token),
                    ct);
                Console.WriteLine($"  → {Path.Combine(output, deckName)}");

                var documentName = $"backgrounds-{stamp}.docx";
                await publisher.ComposeAsync(
                    documentName,
                    (temporary, token) => sample.ComposeDocumentAsync(temporary, token),
                    ct);
                Console.WriteLine($"  → {Path.Combine(output, documentName)}");
            }

            Console.WriteLine("Done.");
            return 0;
        }
        catch (ArgumentException error)
        {
            Console.Error.WriteLine(error.Message);
            return 2;
        }
        catch (StudioException error)
        {
            PrintKnownFailure(error.Message, error.Hint);
            return 1;
        }
        catch (DocumentProviderException error)
        {
            PrintKnownFailure(
                error.Message,
                "Check OFFICEAGENT_STUDIO_OUTPUT, its permissions, and whether the destination already exists.");
            return 1;
        }
        catch (UnauthorizedAccessException error)
        {
            PrintKnownFailure(error.Message, "The output directory is not writable by this process.");
            return 1;
        }
        catch (IOException error)
        {
            PrintKnownFailure(error.Message, "Check OFFICEAGENT_STUDIO_OUTPUT and available disk space.");
            return 1;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static void PrintKnownFailure(string message, string? hint)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(hint)) Console.Error.WriteLine(hint);
    }

    private static void PrintHelp() => Console.WriteLine("""
        OfficeAgent.Studio - designed .pptx and .docx from a one-line brief.

          dotnet run --project src/OfficeAgent.Studio -- <command> ["brief"]

        Commands
          both              a deck and a report from the same brief (default)
          deck              a PowerPoint deck, 8-10 slides
          doc, document     a Word report, 12-18 blocks
          invoice           a Word invoice; totals are computed, not by the model
          manual            a Word manual with numbered sections and steps
          backdrop          background-opacity samples; makes NO model call,
          (background)      so it is the quickest way to prove document generation

        The brief is optional. Without one, a sample quarterly-review brief is used.

        Environment
          OFFICEAGENT_STUDIO_OUTPUT   where files are written (default ./output)
          OFFICEAGENT_STUDIO_BRAND    palette: default, meridian
          OFFICEAGENT_STUDIO_CLIENT   name on the cover and in the footer
          OFFICEAGENT_STUDIO_MODEL_PROVIDER
                                      claude (default) or azure-foundry

        Azure Foundry
          AZURE_OPENAI_ENDPOINT       Foundry/Azure OpenAI endpoint
          AZURE_OPENAI_DEPLOYMENT     deployed model name (AZURE_OPENAI_MODEL also works)
          AZURE_OPENAI_API_KEY        optional; otherwise DefaultAzureCredential is used

        Examples
          dotnet run --project src/OfficeAgent.Studio -- deck "Series B narrative for a robotics company"
          dotnet run --project src/OfficeAgent.Studio -- backdrop
        """);
}
