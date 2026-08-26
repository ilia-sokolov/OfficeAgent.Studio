# OfficeAgent.Studio

Create designed PowerPoint presentations and Word documents from a short brief. A model
plans the content. Deterministic composers apply one validated design system to every slide
and page.

![A generated deck with a dark cover and a statistic slide](docs/images/deck.png)

OfficeAgent.Studio is a C# sample built with
[OfficeAgent.NET](https://github.com/ilia-sokolov/OfficeAgent.NET) and the
[Microsoft Agent Framework](https://github.com/microsoft/agent-framework).

## Get started

### Prerequisites

- Install the .NET 8 SDK. A later SDK can build the project, but the .NET 8 runtime must
  also be installed.
- For model-backed commands, use either:
  - a signed-in [Claude Code](https://claude.com/claude-code) CLI with a paid Claude plan, or
  - an Azure OpenAI deployment in Microsoft Foundry.

Microsoft Office isn't required to generate files. Use PowerPoint or Word to open them.

### Verify document generation

Run the offline smoke test first. It makes no model call and requires no sign-in.

```bash
git clone https://github.com/ilia-sokolov/OfficeAgent.Studio
cd OfficeAgent.Studio
dotnet run --project src/OfficeAgent.Studio -- backdrop
```

The command writes a `.pptx` and `.docx` to `./output`. A successful smoke test confirms
the OfficeAgent document stack and output directory. It doesn't validate model access.

### Generate a deck

Claude Code is the default model provider. If `claude` is installed and signed in, run:

```bash
dotnet run --project src/OfficeAgent.Studio -- deck \
  "Series B narrative for a warehouse robotics company"
```

Use `--help` to list commands and configuration.

## Commands

The brief is optional. Without one, content commands use a sample quarterly-review brief,
and `design-system` uses a sample brand brief.

| Command | Output | Description |
| --- | --- | --- |
| `both` | `.pptx` and `.docx` | Default. Plans a deck and report independently. |
| `deck` | `.pptx` | Creates 8–10 slides from seven slide roles. |
| `doc`, `document` | `.docx` | Creates a 12–18-block report. |
| `invoice` | `.docx` | Creates an invoice with totals calculated in C#. |
| `manual` | `.docx` | Creates a manual with editable Word numbering. |
| `design-system`, `brand` | `.json`, `.pptx`, `.docx` | Generates a validated reusable design system and previews. |
| `backdrop`, `background` | `.pptx` and `.docx` | Creates offline background and cover samples. |

## Configure a model provider

### Use Claude Code

No configuration is required when `claude` is on `PATH` and signed in.

| Variable | Default | Description |
| --- | --- | --- |
| `OFFICEAGENT_STUDIO_MODEL_PROVIDER` | `claude` | Selects `claude` or `azure-foundry`. |
| `OFFICEAGENT_STUDIO_CLAUDE_EXECUTABLE` | `claude` | Sets the executable name or absolute path. |
| `OFFICEAGENT_STUDIO_CLAUDE_TIMEOUT_SECONDS` | `300` | Sets a positive process timeout in seconds. |

### Use Microsoft Foundry

Set the provider, Azure OpenAI endpoint, and deployment name. If you don't set an API key,
the client uses `DefaultAzureCredential` and Microsoft Entra ID.

```powershell
$env:OFFICEAGENT_STUDIO_MODEL_PROVIDER = "azure-foundry"
$env:AZURE_OPENAI_ENDPOINT = "https://your-resource.openai.azure.com/"
$env:AZURE_OPENAI_DEPLOYMENT = "your-chat-deployment"
az login

dotnet run --project src/OfficeAgent.Studio -- deck "Series B narrative"
```

For key-based authentication, also set:

```powershell
$env:AZURE_OPENAI_API_KEY = "your-key"
```

`AZURE_OPENAI_MODEL` is an alias for `AZURE_OPENAI_DEPLOYMENT`. Use the deployment name,
not the catalog model name. The selected identity must have permission to invoke the
deployment.

For implementation guidance, see [Build an AI chat app with .NET](https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/build-chat-app)
and [Foundry tools authentication and authorization using .NET](https://learn.microsoft.com/en-us/dotnet/ai/azure-ai-services-authentication).

All planning code depends on `IChatClient`. Add another provider in `ModelClientFactory`
without changing validation or composition.

## Configure output and branding

| Variable | Default | Description |
| --- | --- | --- |
| `OFFICEAGENT_STUDIO_OUTPUT` | `./output` | Sets the output directory. |
| `OFFICEAGENT_STUDIO_CLIENT` | `Northwind Traders` | Passes the client name to the model and uses it in the report footer. |
| `OFFICEAGENT_STUDIO_BRAND` | `default` | Selects the registered `default` or `meridian` system. |
| `OFFICEAGENT_STUDIO_BRAND_FILE` | - | Loads generated design-system JSON instead of a registered system. |
| `OFFICEAGENT_STUDIO_COVER_MODE` | Design system | Overrides the deck and report cover with `dark` or `light`. |
| `OFFICEAGENT_STUDIO_LOGO` | - | Sets a non-interlaced PNG for covers and invoice letterheads. |
| `OFFICEAGENT_STUDIO_LOGO_ALT` | Wordmark or `Brand logo` | Sets alt text for Word logo images. |

Don't set `OFFICEAGENT_STUDIO_BRAND` and `OFFICEAGENT_STUDIO_BRAND_FILE` together.

### Generate a design system

Generate a reusable JSON artifact and PowerPoint and Word previews:

```powershell
dotnet run --project src/OfficeAgent.Studio -- design-system `
  "Restrained Dutch technology consultancy; precise, modern, and credible"
```

Reuse the artifact without regenerating it:

```powershell
$env:OFFICEAGENT_STUDIO_BRAND_FILE = "C:\brand\design-system.json"
dotnet run --project src/OfficeAgent.Studio -- both "Quarterly review"
```

Generated systems can select a bounded palette, portable fonts, type scales, layout
measures, backdrop strengths, and a light or dark cover. The runtime rejects invalid
colors, unreadable contrast, unsupported fonts, invalid geometry, unknown JSON fields, and
unsupported schema versions.

### Add a logo or override the cover

Keep logo bytes outside the portable design-system JSON and supply them for each run:

```powershell
$env:OFFICEAGENT_STUDIO_LOGO = "C:\brand\acme-logo.png"
$env:OFFICEAGENT_STUDIO_LOGO_ALT = "Acme logo"
$env:OFFICEAGENT_STUDIO_COVER_MODE = "light"

dotnet run --project src/OfficeAgent.Studio -- both "Quarterly review"
```

The logo loader accepts bounded, non-interlaced RGB, RGBA, grayscale, and indexed PNGs.
Word outputs contain an image with alt text. PowerPoint composites the transparent logo
into the cover background because OfficeAgent.NET 0.6 can resize, but not position, an
inserted slide image. Without an image, invoices use the design system's text wordmark.

For design properties, contrast rules, and fixed layout decisions, see
[The design system](docs/design-system.md).

## How it works

```text
Brief -> StudioAgent -> JSON plan -> PlanValidator -> Composer -> OfficeAgent.NET -> Office file
                                      trust boundary     + DesignSystem
```

1. `StudioAgent` asks the selected model for content-only JSON.
2. `PlanValidator` normalizes roles and rejects malformed or unsafe plans.
3. A composer applies the design system through OfficeAgent.NET operations.
4. `OutputTransaction` publishes the requested file only after composition succeeds.

Content plans can't set fonts, colors, sizes, or positions. The model gets up to three
attempts to return a valid plan. Failed or canceled composition doesn't publish a
success-looking partial file.

## Known limitations

`[sample]` identifies behavior you can change in this repository. `[library]` requires an
OfficeAgent.NET capability.

| Area | Limitation |
| --- | --- |
| Content | The model invents all facts and figures. Review every output. |
| `both` | The deck and report are planned independently, so their facts can differ. |
| Domain rules | Invoice arithmetic is deterministic, but tax treatment and factual claims aren't validated. |
| Office theme `[library]` | Colors and fonts use direct formatting over the stock Office theme. Resetting a slide can remove the brand formatting. |
| Word page setup `[library]` | The files don't set page size or margins. Layout calculations assume Letter size and 1-inch margins. |
| Heading semantics `[sample]` | Report headings use direct formatting instead of Word heading styles. Navigation, bookmarks, and heading announcements are unavailable. |
| Fonts `[library]` | Fonts are referenced, not embedded. Substitution can change wrapping or overflow fixed slide boxes. |
| Rich content | Charts aren't supported `[library]`. Content images aren't composed `[sample]`; only configured logos and generated backdrops are used. |
| Navigation `[library]` | Table-of-contents fields and cross-references aren't supported. |
| Overflow `[sample]` | Long text can overflow because the layout constrains content but doesn't reflow it. |
| Accessibility | Contrast and Word logo alt text are implemented. Full reading-order, language, heading, and visual accessibility audits aren't. |

## Build and test

The project restores OfficeAgent.NET 0.6.0 from NuGet by default.

```bash
dotnet test
```

To test against a sibling OfficeAgent.NET checkout, opt in:

```bash
dotnet test -p:UseOfficeAgentSource=true
```

If the checkout isn't beside this repository, also pass
`-p:OfficeAgentSource=/absolute/path/to/OfficeAgent.NET/src`. The build reports which
dependency source it selected.

The suite covers plan validation, model-client configuration, process cancellation,
contrast, invoice arithmetic, generated design systems, atomic publication, Open XML
schema validation, and key output content. It doesn't call a paid model or judge visual
quality.

## Contribute

Issues and pull requests are welcome, particularly for new brands and slide roles.

This project is licensed under the [MIT License](LICENSE).
