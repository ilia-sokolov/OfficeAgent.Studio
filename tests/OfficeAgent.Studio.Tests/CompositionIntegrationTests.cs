using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OfficeAgent.Core;
using OfficeAgent.Core.DocumentProviders;
using OfficeAgent.PowerPoint;
using OfficeAgent.Studio;
using OfficeAgent.Word;
using System.Text.Json;
using Xunit;

namespace OfficeAgent.Studio.Tests;

public class CompositionIntegrationTests
{
    [Fact]
    public async Task Design_system_command_publishes_reusable_json_and_valid_previews()
    {
        var root = Path.Combine(Path.GetTempPath(), "officeagent-studio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var reply = JsonSerializer.Serialize(TestPlans.DesignSystem());
            var settings = new Dictionary<string, string?>
            {
                ["OFFICEAGENT_STUDIO_OUTPUT"] = root,
                ["OFFICEAGENT_STUDIO_CLIENT"] = "Northwind Traders"
            };

            var exitCode = await StudioProgram.RunAsync(
                new[] { "design-system", "Restrained technology consultancy" },
                () => new ReplyChatClient(reply),
                name => settings.GetValueOrDefault(name));

            Assert.Equal(0, exitCode);
            var artifact = Assert.Single(Directory.EnumerateFiles(root, "design-system-*.json"));
            var deck = Assert.Single(Directory.EnumerateFiles(root, "design-system-preview-*.pptx"));
            var document = Assert.Single(Directory.EnumerateFiles(root, "design-system-preview-*.docx"));
            Assert.Equal("northwind", DesignSystemFiles.Load(artifact).Wordmark);
            AssertSchemaValid(deck);
            AssertSchemaValid(document);
            Assert.Empty(Directory.EnumerateFiles(root, "partial-*", SearchOption.TopDirectoryOnly));

            settings["OFFICEAGENT_STUDIO_BRAND_FILE"] = artifact;
            var modelRequested = false;
            var reuseExitCode = await StudioProgram.RunAsync(
                new[] { "backdrop" },
                () =>
                {
                    modelRequested = true;
                    throw new InvalidOperationException("Backdrop must not create a model client.");
                },
                name => settings.GetValueOrDefault(name));

            Assert.Equal(0, reuseExitCode);
            Assert.False(modelRequested);
            var reusedDeck = Assert.Single(Directory.EnumerateFiles(root, "backgrounds-*.pptx"));
            var reusedDocument = Assert.Single(Directory.EnumerateFiles(root, "backgrounds-*.docx"));
            AssertSchemaValid(reusedDeck);
            AssertSchemaValid(reusedDocument);
            Assert.Empty(Directory.EnumerateFiles(root, "partial-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Every_output_type_is_published_complete_and_schema_valid()
    {
        var root = Path.Combine(Path.GetTempPath(), "officeagent-studio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            using var services = Services(root);
            var client = services.GetRequiredService<OfficeAgentClient>();
            var publisher = new OutputTransaction(client, root);
            var design = DesignSystem.Default;

            await publisher.ComposeAsync(
                "deck.pptx",
                (name, ct) => new DeckComposer(client, design).ComposeAsync(TestPlans.Deck(), name, ct));
            await publisher.ComposeAsync(
                "report.docx",
                (name, ct) => new DocumentComposer(client, design).ComposeAsync(
                    TestPlans.Document(), name, "Northwind Traders", ct));
            await publisher.ComposeAsync(
                "invoice.docx",
                (name, ct) => new InvoiceComposer(client, design).ComposeAsync(TestPlans.Invoice(), name, ct));
            await publisher.ComposeAsync(
                "manual.docx",
                (name, ct) => new ManualComposer(client, design).ComposeAsync(TestPlans.Manual(), name, ct));

            var generatedDesign = DesignSystemPlanValidator.ToDesignSystem(
                DesignSystemPlanValidator.NormalizeAndValidate(TestPlans.DesignSystem()));
            var backdrop = new BackdropSample(client, generatedDesign);
            await publisher.ComposeAsync(
                "backgrounds.pptx",
                (name, ct) => backdrop.ComposeDeckAsync(name, ct));
            await publisher.ComposeAsync(
                "backgrounds.docx",
                (name, ct) => backdrop.ComposeDocumentAsync(name, ct));

            var expected = new[]
            {
                "deck.pptx", "report.docx", "invoice.docx", "manual.docx",
                "backgrounds.pptx", "backgrounds.docx"
            };
            Assert.All(expected, name => Assert.True(File.Exists(Path.Combine(root, name)), $"Missing {name}."));
            Assert.Empty(Directory.EnumerateFiles(root, "partial-*", SearchOption.TopDirectoryOnly));

            foreach (var name in expected) AssertSchemaValid(Path.Combine(root, name));

            var collision = await Assert.ThrowsAsync<StudioException>(() => publisher.ComposeAsync(
                "report.docx",
                (name, ct) => ComposerSession.RunAsync(
                    client, "output", name, _ => Task.CompletedTask, ct)));
            Assert.Contains("already exists", collision.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.EnumerateFiles(root, "partial-*", SearchOption.TopDirectoryOnly));

            using (var deck = PresentationDocument.Open(Path.Combine(root, "deck.pptx"), false))
                Assert.Equal(TestPlans.Deck().Slides.Length, deck.PresentationPart!.SlideParts.Count());

            using (var report = WordprocessingDocument.Open(Path.Combine(root, "report.docx"), false))
                Assert.Contains(TestPlans.Document().Title, DocumentText(report));
            using (var invoice = WordprocessingDocument.Open(Path.Combine(root, "invoice.docx"), false))
                Assert.Contains("€", DocumentText(invoice));
            using (var manual = WordprocessingDocument.Open(Path.Combine(root, "manual.docx"), false))
                Assert.Contains(TestPlans.Manual().Title, DocumentText(manual));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_failed_composition_leaves_no_published_or_partial_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "officeagent-studio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            using var services = Services(root);
            var client = services.GetRequiredService<OfficeAgentClient>();
            var publisher = new OutputTransaction(client, root);

            await Assert.ThrowsAsync<StudioException>(() => publisher.ComposeAsync(
                "never-published.docx",
                async (name, ct) =>
                {
                    var id = await new DocumentComposer(client, DesignSystem.Default)
                        .ComposeAsync(TestPlans.Document(), name, "Northwind Traders", ct);
                    throw new StudioException($"Fail after composing {id}.");
                }));

            Assert.False(File.Exists(Path.Combine(root, "never-published.docx")));
            Assert.Empty(Directory.EnumerateFiles(root, "partial-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ServiceProvider Services(string root) => new ServiceCollection()
        .AddWordFormat()
        .AddPowerPointFormat()
        .AddFileSystemDocumentProvider("output", root, options =>
        {
            options.AllowedExtensions = new[] { ".docx", ".pptx" };
            options.DefaultChangeMode = OfficeAgent.Abstractions.ChangeMode.Direct;
        })
        .AddOfficeAgent()
        .BuildServiceProvider();

    private static void AssertSchemaValid(string path)
    {
        using OpenXmlPackage package = Path.GetExtension(path).Equals(".pptx", StringComparison.OrdinalIgnoreCase)
            ? PresentationDocument.Open(path, false)
            : WordprocessingDocument.Open(path, false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Select(error => error.Description)));
    }

    private static string DocumentText(WordprocessingDocument document) =>
        document.MainDocumentPart?.Document?.InnerText
        ?? throw new InvalidDataException("The generated Word document has no main document part.");

    private sealed class ReplyChatClient(string reply) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
