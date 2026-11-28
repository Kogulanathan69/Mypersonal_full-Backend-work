# BuildMind design tokens — Dark Navy + Gold

Owner: **F — Foundation**. Source of truth: `src/styles/tokens.css`.

All colours below were sampled from the reference screens in
`BuildMind_Room_Summary_Dashboard.zip` (Room Summary, Dream Home Dashboard, Additional
Spaces, AI Design Method, Gold-Navy Floor Plan Editor, Modern Home Planning Login) by
bucketing every pixel and taking the dominant value of each colour family. Values were
adjusted only where needed to pass WCAG 2.2 AA contrast.

## Rules

1. `tokens.css` is the **only** file allowed to contain raw colour values. `npm run check:tokens` fails otherwise.
2. Components use **semantic** tokens (`bg-surface`, `text-muted`, `border-border`, `bg-accent`), not brand-scale tokens, so the palette can be tuned in one place.
3. Tailwind's default palette is disabled (`--color-*: initial`), so classes like `bg-red-500` do not exist.
4. Every text/background pairing is checked by `npm run check:contrast` (43 pairings, WCAG 2.2 AA).

## Semantic colours

| Token | Tailwind class | Value | Sampled from | Use |
| --- | --- | --- | --- | --- |
| `--color-bg` | `bg-bg` | navy-950 `#04142c` | Dominant page background (#04142c bucket) | Page background |
| `--color-bg-deep` | `bg-bg-deep` | navy-975 `#030c1c` | Top bar / hero shadow areas | Top bars, footer |
| `--color-surface` | `bg-surface` | navy-900 `#0a1e38` | Card fill (#041c34 / #0c1c34 buckets) | Cards, panels |
| `--color-surface-raised` | `bg-surface-raised` | navy-800 `#102843` | Inner rows of cards | Rows, inputs, menus |
| `--color-border` | `border-border` | navy-700 `#1e3858` | Card outlines (#142c44 bucket) | Decorative dividers |
| `--color-border-strong` | `border-border-strong` | navy-500 `#647c9f` | Lightened for 3:1 | Input and control borders |
| `--color-accent` | `bg-accent` / `text-accent` | gold-500 `#fcbc04` | Primary buttons (#fcbc04 bucket) | Primary actions, active step |
| `--color-accent-hover` | `bg-accent-hover` | gold-400 `#fdcb3a` | Lighter gold highlights | Hover state |
| `--color-on-accent` | `text-on-accent` | navy-950 | Button label colour in references | Text on gold |
| `--color-text` | `text-text` | ivory-50 `#f5f5f2` | Headings and body (#f4f4f4 bucket) | Primary text |
| `--color-muted` | `text-muted` | slate-300 `#bcc0cb` | Secondary text (#bcbcc4 bucket) | Secondary text |
| `--color-subtle` | `text-subtle` | slate-400 `#97a0b2` | Captions (#9c9ca4 bucket) | Captions, hints |
| `--color-focus` | outline | gold-300 `#fdd86a` | — | Keyboard focus ring |
| `--color-success` | `text-success` | `#22c17a` | Green ticks (#04b45c, lightened) | Completed, valid |
| `--color-warning` | `text-warning` | `#f59a3c` | Orange (#bc5404, lightened) | Warnings (distinct from brand gold) |
| `--color-danger` | `text-danger` | `#f26b61` | Red (#fc0404, softened for contrast) | Errors, critical issues |
| `--color-info` | `text-info` | `#7ea8f0` | "In progress" badge (#8baae5) | Informational |
| `--color-paper` / `--color-ink` | `bg-paper` / `text-ink` | `#f7f4ee` / `#0b1626` | Light plan drawing area | 2D plan canvas |

Warning is orange rather than gold so a warning is never confused with a primary action.

## Typography

Poppins (400, 500, 600, 700), self-hosted from the `@fontsource/poppins` npm package
(SIL Open Font License 1.1). No request is made to Google Fonts. **Provisional** until the
design owner confirms the brand font.

## Other tokens

Radius `sm 6px · md 10px · lg 14px · xl 20px · pill`; shadows `card`, `glow` (gold outline
for the selected card, as in the references); motion `fast 120ms · base 200ms`, all
disabled under `prefers-reduced-motion`; breakpoints `sm 640 · md 768 · lg 1024 · xl 1280`.
