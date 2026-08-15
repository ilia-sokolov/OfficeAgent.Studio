# OfficeAgent.Studio

A demo agent that writes .pptx decks and .docx documents which look like a design studio made
them - built on [OfficeAgent.NET](https://github.com/ilia-sokolov/OfficeAgent.NET) and the
[Microsoft Agent Framework](https://github.com/microsoft/agent-framework).

```bash
dotnet run --project src/OfficeAgent.Studio -- both
```

Output lands in `./output`. Two files, roughly a minute.

## The idea

Most "AI made me a deck" output looks the same, and it looks bad for the same reason: the model
is asked to choose the words *and* the formatting, so it picks a font size per slide and nothing
lines up with anything.

This splits the job in two.

**The model decides what the document says.** It returns content as JSON - a slide's role, its
title, its bullets, its number - and it is told explicitly that anything it says about fonts,
colours, sizes or positions is ignored.

**`DesignSystem` decides how it looks.** One ink, one accent, one wash. Two faces: Georgia to
display, Calibri to read. A type scale of six sizes and a margin, and every measure on the page
derived from those. Nothing picks a colour at the point of use, which is what stops the ninth
slide drifting from the first.

That division is the whole demo. It is also the reason the output holds together: a fixed
vocabulary of seven slide roles and six block kinds is a smaller thing to get right than an
open-ended "make it beautiful", and a model is good at choosing among seven roles.

## What it produces

A deck of 8–10 slides, composed from these roles:

| Role | What it is |
| --- | --- |
| `cover` | Full-bleed ink, accent rule, the deck's title |
| `section` | A divider that announces what follows |
| `statement` | One sentence that has to land |
| `bullets` | Three or four lines, centred in the content area |
| `stat` | One number on a wash card, set large |
| `table` | Numbers, no rules, alignment doing the separating |
| `closing` | The ask |

And a document of 12–18 blocks: a title page of its own, then headings, paragraphs, hanging
bullets, one pull quote with an accent rule down its left edge, and one table ruled only
under its header row. A running head carries the title, the footer carries the client and a
page number at opposite edges, and a wash sits behind every page at 35% - all of it kept off
the cover, which has its own blank header.

Every slide shares one vertical rhythm - eyebrow, accent rule, title, content - derived from a
single anchor per slide kind, so the eyebrow and the rule cannot collide however long a title
turns out to be.

## Layout

```
src/OfficeAgent.Studio/
  Program.cs               DI wiring and the CLI
  Brief.cs                 The request, and the JSON shapes the model returns
  StudioAgent.cs           The two agents, and their instructions
  ClaudeCodeChatClient.cs  IChatClient over the Claude Code CLI
  DesignSystem.cs          Every colour, face, size and measure
  Backdrop.cs              Draws the cover gradients, as PNGs, in code
  DeckComposer.cs          DeckPlan  → .pptx
  DocumentComposer.cs      DocumentPlanned → .docx
```

`Backdrop.cs` is there so the repository carries no binary assets: a demo that shipped a
stock photograph would be demonstrating the photograph. It writes a PNG by hand - header,
one deflated IDAT, CRCs - and the gradient it draws comes from `DesignSystem`, so changing
the accent changes the cover too.

`DesignSystem.cs` is the file to edit first. Change `Accent`, `DisplayFont` and `Margin` and the
whole deck follows.

## Using a different model

`ClaudeCodeChatClient` is the only Claude-specific file. It shells out to the `claude` CLI, so
the demo runs on a Claude subscription with no API key configured. Swap it for any
`IChatClient` and nothing else changes:

```csharp
var agent = new StudioAgent(
    new AzureOpenAIClient(endpoint, credential)
        .GetChatClient("gpt-4o")
        .AsIChatClient());
```

## Requirements

- .NET 8 SDK
- OfficeAgent.NET 0.6.0 or later - this uses slide backgrounds, shape fills, vertical text
  anchoring, `backgroundImage`, Word headers and footers, page breaks, hanging indents and
  single-edge borders
- For the default model: the [Claude Code](https://claude.com/claude-code) CLI, signed in

The project references the OfficeAgent working tree when it finds one beside this repo, and
falls back to the NuGet packages otherwise. See the comments in
`src/OfficeAgent.Studio/OfficeAgent.Studio.csproj`.
