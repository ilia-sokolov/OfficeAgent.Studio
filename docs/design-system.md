# The design system

What `DesignSystem.cs` controls, what it doesn't, and why the palette is shaped the way it
is.

## What a brand can change

Everything in the `DesignSystem` record, by writing `Default with { … }` and registering the
result in `DesignSystem.Brands`.

| Group | Properties |
| --- | --- |
| Palette | `Ink`, `InkDeep`, `Paper`, `Wash`, `WashDeep`, `Body`, `Muted`, `MutedReverse`, `Accent`, `AccentText`, `AccentReverse`, `Reverse` |
| Type | `DisplayFont`, `TextFont`, and eleven sizes — `DisplaySize`, `TitleSize`, `SubtitleSize`, `BodySize`, `CaptionSize`, `StatSize`, and five `Document*` sizes |
| Measures | `Margin`, `RuleHeight`, `DocumentMeasureInset`, `DocumentIndent`, `PageBackdropOpacity`, `CoverLift` |
| Mark | `Wordmark`, `WordmarkDot`, `EyebrowUppercase` |

Two type scales exist on purpose. A slide is read across a room and a page at arm's length,
so `BodySize` (14pt) and `DocumentBodySize` (10.5pt) are different decisions, not an
oversight.

## What a brand cannot change yet

These are literals in the composers rather than properties. If you are adapting this for a
real brand, this is the list to budget for.

**Deck** (`DeckComposer.cs`)

- The vertical grid. `Frame.From` places the eyebrow at `top`, the rule at `top + 38`, the
  title at `top + 66`, and content 30px below the title.
- The two layouts: `top = 250` for centred slides, `top = 84` for the rest.
- The accent rule is 96px long. `RuleHeight` sets its thickness; nothing sets its length.
- Stat card height, caption gap, and the fact that the card is filled `Wash`.
- Which roles invert to an ink ground (`section` and `closing`), and that the cover is
  always full-bleed ink. A light-covered brand is not expressible.
- The transition (`fade`, 400ms) and the 46-character threshold that drops a long title to
  the smaller size.

**Document** (`DocumentComposer.cs`)

- Every vertical space is a literal in `StyleFor` — 2400, 400, 280, 180 twips and so on.
  There is no spacing scale, which makes spacing the least brandable thing in a system whose
  whole subject is consistency.
- Border weights, the quote indent multiplier, and the 58-character running-head truncation.

**Absent entirely**

- Line height, letter-spacing, bullet glyph, table style.
- Page size and margins — see the README's Limits.
- Slide geometry. `SlideWidth` and `SlideHeight` are settable and inert: the real slide size
  is fixed by OfficeAgent.NET's blank deck, so changing them moves content off a canvas that
  did not change.

## Why two greys and three accents

Contrast is arithmetic, not taste, and the palette is shaped by measuring it.

`DesignSystem.Contrast` computes the
[WCAG ratio](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html) between two
colours. Normal text needs 4.5:1; large text — 18pt, or 14pt bold — needs 3.0:1.

The first palette set captions in `#8A9199`. That is **3.2:1** on white. It looked refined
on the monitor it was chosen on and was unreadable in print. Fixing it properly produced
three rules:

**Two muted greys.** A grey dark enough to read on paper is too light to read on ink. The
pair is `Muted` and `MutedReverse`, and a test asserts the reverse one is the lighter of the
two — the mistake worth catching is setting both to the same value, which loses the cover
subtitle.

**Three accents.** `Accent` is a mark: a rule, a fill, the wordmark dot. It *may* fall below
4.5:1, because a 6px rule is not text — but it does not have to. `AccentText` is the tone
used where the accent is a word, and it is the only one required to reach 4.5:1 on paper.
`AccentReverse` is light enough to read on ink.

A brand whose accent already reads well as small text sets `AccentText` to the same value
and is done. The split exists so that a bright mark *can* have a darker twin, not so that
every brand must own two oranges.

**Measure what renders, not what you wrote.** Two grounds are composites:

- The page wash is `WashDeep` over `Paper` at `PageBackdropOpacity`. Checking type against
  the raw `WashDeep` would fail a page that never prints that dark — so `RenderedWash`
  composites it first.
- The cover backdrop lifts toward white in one corner so a large dark area does not look
  dead, and that corner is exactly where the eyebrow sits. `CoverLightest` applies the lift,
  and adding that check is what caught the eyebrow using the mark accent where it needed the
  reverse one.

## The tests

`ContrastTests` generates its cases from `DesignSystem.Brands`, so registering a brand is
enough to have it measured — nobody has to remember to add it. Each case carries the
threshold its size earns, and the one place that claims the large-text exemption (the 48pt
stat number) claims it explicitly rather than by accident.

Two things the suite does **not** do, which matter if you are relying on a green build:

- **The pairings are a hand-written list.** Adding a colour to a composer does not add a
  case. `ContrastTests.Pairings()` is where they live, and a new text-on-ground combination
  has to be added there by hand.
- **It only measures colour.** Heading semantics, reading order, alt text and document
  language are all unaddressed — see the README's Limits. A green build is evidence about
  contrast, while the separate composition integration tests are evidence about Open XML
  validity and key generated content. Neither is a visual or full accessibility audit.
