using System.Text.Json;
using Microsoft.Extensions.AI;
using OfficeAgent.Studio;
using Xunit;

namespace OfficeAgent.Studio.Tests;

public class PlanValidatorTests
{
    [Fact]
    public async Task The_agent_retries_a_semantically_invalid_plan()
    {
        var valid = TestPlans.Deck();
        var tooShort = valid with { Slides = valid.Slides[..7] };
        using var chat = new ReplyChatClient(
            JsonSerializer.Serialize(tooShort),
            JsonSerializer.Serialize(valid));
        var agent = new StudioAgent(chat);

        var result = await agent.PlanDeckAsync(new Brief { Client = "Client", Request = "Quarterly review" });

        Assert.Equal(2, chat.Calls);
        Assert.Equal(8, result.Slides.Length);
    }

    [Fact]
    public void Slide_roles_are_normalized_before_the_composer_sees_them()
    {
        var source = TestPlans.Deck();
        var slides = source.Slides.ToArray();
        slides[0] = slides[0] with { Kind = " COVER " };
        slides[2] = slides[2] with { Kind = "STAT" };

        var normalized = PlanValidator.NormalizeAndValidate(source with { Slides = slides });

        Assert.Equal("cover", normalized.Slides[0].Kind);
        Assert.Equal("stat", normalized.Slides[2].Kind);
    }

    [Fact]
    public void An_explicitly_null_nested_array_is_retried_instead_of_crashing_a_composer()
    {
        var source = TestPlans.Deck();
        var slides = source.Slides.ToArray();
        slides[3] = slides[3] with { Bullets = null! };

        var error = Assert.Throws<InvalidOperationException>(
            () => PlanValidator.NormalizeAndValidate(source with { Slides = slides }));

        Assert.Contains("bullets is null", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_table_must_be_rectangular()
    {
        var source = TestPlans.Deck();
        var slides = source.Slides.ToArray();
        slides[5] = slides[5] with { TableRows = new[] { new[] { "Only one cell" }, new[] { "A", "B" }, new[] { "C", "D" } } };

        var error = Assert.Throws<InvalidOperationException>(
            () => PlanValidator.NormalizeAndValidate(source with { Slides = slides }));

        Assert.Contains("expected 2", error.Message);
    }

    [Fact]
    public void Unknown_document_blocks_are_rejected()
    {
        var source = TestPlans.Document();
        var blocks = source.Blocks.ToArray();
        blocks[4] = blocks[4] with { Kind = "checklist" };

        var error = Assert.Throws<InvalidOperationException>(
            () => PlanValidator.NormalizeAndValidate(source with { Blocks = blocks }));

        Assert.Contains("Unknown document block", error.Message);
    }

    [Fact]
    public void Invoice_dates_and_nonnegative_values_are_validated()
    {
        var source = TestPlans.Invoice();
        var reversedDates = source with { Due = "1 August 2026" };
        Assert.Contains("after the issue date", Assert.Throws<InvalidOperationException>(
            () => PlanValidator.NormalizeAndValidate(reversedDates)).Message);

        var lines = source.Lines.ToArray();
        lines[0] = lines[0] with { UnitPrice = -1m };
        Assert.Contains("must not be negative", Assert.Throws<InvalidOperationException>(
            () => PlanValidator.NormalizeAndValidate(source with { Lines = lines })).Message);
    }

    [Fact]
    public void Manual_text_must_not_duplicate_Words_numbering()
    {
        var source = TestPlans.Manual();
        var sections = source.Sections.ToArray();
        sections[0] = sections[0] with { Heading = "1. Prepare the scanner" };

        var error = Assert.Throws<InvalidOperationException>(
            () => PlanValidator.NormalizeAndValidate(source with { Sections = sections }));

        Assert.Contains("must not begin with a number", error.Message);
    }

    [Fact]
    public void Json_extraction_skips_braces_in_prose_and_returns_the_valid_object()
    {
        var json = StudioAgent.ExtractJson("I considered {not json}. Result: {\"title\":\"Ready\"} trailing text.");

        Assert.Equal("{\"title\":\"Ready\"}", json);
    }

    private sealed class ReplyChatClient(params string[] replies) : IChatClient
    {
        private readonly Queue<string> _replies = new(replies);

        internal int Calls { get; private set; }

        public ChatClientMetadata Metadata { get; } = new("test");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, _replies.Dequeue())));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var message in response.Messages)
                yield return new ChatResponseUpdate(message.Role, message.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }
}
