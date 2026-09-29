# ADR 0033 — A playful visual design, and a one-page reader below desktop

- **Status:** Accepted
- **Date:** 2026-09-29
- **Deciders:** Luca Martinelli

## Context

The UI was styled as a grown-up book: a warm cream and terracotta palette, the Fraunces serif for
titles and story text, `rounded-xl` corners, and hairline borders. The people it is for are small
children and the parents reading to them, and it did not look like it. The reference we want to be
close to is the children's audio player site myfaba.it: bright colours over pastel backgrounds,
rounded shapes with no corners, rounded type.

The reader had its own problems:

- The story text sat over the bottom of the illustration on a pane of frosted glass, hiding part of
  the picture the child is looking at.
- From 768px up it showed two facing pages. On a tablet that halves the size of each picture and of
  each line of text, on the device a child is most likely to hold.
- Below desktop its controls faded out after three seconds and came back on a tap: a hidden
  affordance that a small child does not find.

## Decision

We will give the whole UI one playful design system, defined once in `apps/ui/src/app/globals.css`:

- **Colours.** A cream background, a plum text colour and a bright pink accent, plus five playful
  colours — sun, sky, mint, lilac, peach — each with a soft tint for backgrounds. Status colours come
  from the same set, not from Tailwind's `red`/`amber`/`emerald` scales. A night palette (deep blue
  with the same pastels) follows the system dark-mode preference.
- **Type.** Two rounded fonts: **Fredoka** for titles and the story text, **Nunito** for the rest of
  the interface. Fraunces goes.
- **Shapes.** Nothing square: pill buttons, cards and dialogs with large radii, images clipped inside
  rounded frames. A handful of shared component classes (`.btn-*`, `.card`, `.field`, `.chip`)
  carry the shapes so the pages do not each restate them.

And the reader becomes:

- **One page at a time** on every device except a desktop, meaning a screen at least 1024px wide
  with a mouse (`hover: hover` and `pointer: fine`). A tablet shows one page in either orientation.
  A desktop keeps two facing pages.
- **A card per page**: the illustration on top, the story text below it, inside the same card. No
  text over the artwork.
- **Minimal chrome** on phones and tablets: a round back button and the title above the card, two
  large round page-turn buttons with a row of dots below it. They are always visible. Swiping and the
  arrow keys still turn pages.

## Consequences

### Positive
- The UI looks like it is for the people who use it, and every page shares the same colours, type
  and shapes.
- The illustration is never covered, and on a tablet each page is twice the size it was.
- A child can see how to turn the page.

### Negative / trade-offs accepted
- The page-turn controls take room from the card on a phone; the text scrolls inside the card when a
  page is long.
- The illustration stays square but no taller than about half the screen below desktop, so on a
  phone in landscape the picture is small.
- The device test uses the primary pointer: a laptop with a touch screen and a mouse reads as a
  desktop, and a tablet with a trackpad attached may too.

### Things this decision obliges us to build or watch
- New UI uses the tokens and component classes, not raw Tailwind colours or small radii.
- Contrast: body text and white on the accent stay at or above 4.5:1 in both palettes.

## Alternatives considered

### Keep two pages on a tablet in landscape
Closer to a printed book, and the screen is wide enough. Not chosen: the child holding the tablet
gets the smaller pictures, and the layout would change when the tablet is turned.

### Keep the text over the illustration
It keeps the page as large as possible, but the text always hides part of the picture, and the glass
behind it changes colour with the artwork, which makes the text harder to read for a beginner.

### A third font for the story text (Andika, designed for early readers)
Considered and set aside: Fredoka is legible at the story's size, and a single display face keeps
the titles and the story in the same voice.
