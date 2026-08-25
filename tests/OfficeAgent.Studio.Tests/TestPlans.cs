using OfficeAgent.Studio;

namespace OfficeAgent.Studio.Tests;

internal static class TestPlans
{
    internal static DeckPlan Deck() => new()
    {
        Title = "Quarterly review",
        Subtitle = "Decisions for the next quarter",
        Slides = new[]
        {
            Slide("cover", "Quarterly review", subtitle: "Decisions for the next quarter"),
            Slide("section", "The plan is within reach"),
            Slide("stat", "Revenue remains close to plan", stat: "EUR 94m", statCaption: "94% of plan"),
            Slide("bullets", "Two actions close most of the gap", bullets: new[]
            {
                "Recover the delayed renewal before month end",
                "Move available capacity to the strongest region",
                "Hold discretionary hiring until the forecast clears"
            }),
            Slide("section", "Execution now matters more than diagnosis"),
            Slide("table", "The recovery has named owners", headers: new[] { "Action", "Owner" }, rows: new[]
            {
                new[] { "Renewal", "Sales" },
                new[] { "Capacity", "Operations" },
                new[] { "Hiring", "Finance" }
            }),
            Slide("statement", "The downside is contained", subtitle: "Existing actions cover the identified gap."),
            Slide("closing", "Approve the recovery plan", subtitle: "Release the owners to execute today.")
        }
    };

    internal static DocumentPlanned Document() => new()
    {
        Title = "Quarterly review",
        Subtitle = "Decisions for the next quarter",
        Meta = new[] { "Board paper 2026-08", "24 August 2026" },
        Blocks = new[]
        {
            TextBlock("heading", "Performance is close to plan"),
            TextBlock("paragraph", Body),
            TextBlock("subheading", "The gap has two causes"),
            TextBlock("paragraph", Body),
            new BlockPlan { Kind = "bullets", Bullets = new[] { "Recover the renewal", "Move available capacity" } },
            TextBlock("heading", "The recovery is owned"),
            TextBlock("paragraph", Body),
            new BlockPlan
            {
                Kind = "table",
                TableHeaders = new[] { "Action", "Owner" },
                TableRows = new[] { new[] { "Renewal", "Sales" }, new[] { "Capacity", "Operations" } }
            },
            TextBlock("subheading", "Decisions are time-bound"),
            TextBlock("paragraph", Body),
            TextBlock("quote", "Execution now matters more than diagnosis."),
            TextBlock("paragraph", Body)
        }
    };

    internal static InvoicePlanned Invoice(string currency = "EUR") => new()
    {
        From = new PartyPlanned
        {
            Name = "Northwind Studio",
            Lines = new[] { "Keizersgracht 1", "1015 AA Amsterdam", "billing@example.test" }
        },
        To = new PartyPlanned
        {
            Name = "Fabrikam GmbH",
            Lines = new[] { "Alexanderplatz 1", "10178 Berlin", "accounts@example.test" }
        },
        InvoiceNumber = "INV-2026-0184",
        Issued = "24 August 2026",
        Due = "23 September 2026",
        Currency = currency,
        Lines = new[]
        {
            new LineItemPlanned { Description = "Discovery workshop", Quantity = 1, Unit = "day", UnitPrice = 1200m },
            new LineItemPlanned { Description = "Document design", Quantity = 2.5m, Unit = "days", UnitPrice = 900m },
            new LineItemPlanned { Description = "Delivery review", Quantity = 1, Unit = "session", UnitPrice = 450m }
        },
        TaxRatePercent = 21m,
        TaxLabel = "VAT",
        Terms = new[] { "Pay within 30 days", "IBAN NL00 TEST 0000 0000 00" }
    };

    internal static ManualPlanned Manual() => new()
    {
        Title = "Warehouse scanner manual",
        Subtitle = "Safe setup and daily operation",
        Meta = new[] { "Version 1.0", "Operators and supervisors" },
        Sections = Enumerable.Range(1, 4).Select(index => new ManualSectionPlanned
        {
            Heading = $"Prepare area {index}",
            Intro = new[] { Body },
            Procedures = new[]
            {
                new ProcedurePlanned
                {
                    Title = $"Complete setup {index}",
                    Steps = new[]
                    {
                        "Check the work area and confirm that it is clear.",
                        "Connect the scanner and wait for the ready indicator.",
                        "Scan the test label and confirm that the item appears."
                    }
                }
            },
            Callout = index == 1 ? new CalloutPlanned { Kind = "warning", Text = "Disconnect damaged equipment." } : null
        }).ToArray()
    };

    private const string Body =
        "The quarter remains close to plan and the identified gap has two bounded causes. " +
        "Named owners have accepted each recovery action. The board can therefore focus on execution.";

    private static SlidePlan Slide(
        string kind,
        string title,
        string? subtitle = null,
        string[]? bullets = null,
        string? stat = null,
        string? statCaption = null,
        string[]? headers = null,
        string[][]? rows = null) => new()
        {
            Kind = kind,
            Title = title,
            Eyebrow = "Q3 2026",
            Subtitle = subtitle,
            Bullets = bullets ?? Array.Empty<string>(),
            Stat = stat,
            StatCaption = statCaption,
            TableHeaders = headers ?? Array.Empty<string>(),
            TableRows = rows ?? Array.Empty<string[]>(),
            Notes = "Explain the evidence and the decision this slide supports."
        };

    private static BlockPlan TextBlock(string kind, string text) => new() { Kind = kind, Text = text };
}
