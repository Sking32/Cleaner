---
name: Cleaner — Graphite
description: A compact Windows workspace for reviewing and explicitly applying system changes.
colors:
  accent-dark: "#72B9F0"
  accent-light: "#126AC1"
  accent-soft-dark: "rgba(114, 185, 240, 0.12549019607843137)"
  accent-soft-light: "rgba(18, 106, 193, 0.09411764705882353)"
  accent-teal-dark: "#60D6C0"
  accent-teal-light: "#087C70"
  accent-violet-dark: "#B9A5F4"
  accent-violet-light: "#7052BD"
  primary-foreground-dark: "#102333"
  primary-foreground-light: "#FFFFFF"
  safe-dark: "#75D6A5"
  safe-light: "#14744A"
  caution-dark: "#EFCA70"
  caution-light: "#806000"
  danger-dark: "#FF9CAA"
  danger-light: "#B52135"
  info-dark: "#79BEF4"
  info-light: "#126AC1"
  bg-base-dark: "#1B222A"
  bg-base-light: "#F7F9FC"
  bg-elevated-dark: "#232C36"
  bg-elevated-light: "#FFFFFF"
  bg-overlay-dark: "#2A3541"
  bg-overlay-light: "#EFF3F8"
  bg-hover-dark: "#344455"
  bg-hover-light: "#E1EAF5"
  border-subtle-dark: "#374657"
  border-subtle-light: "#D5DFEB"
  border-strong-dark: "#62778E"
  border-strong-light: "#9DAFC4"
  text-primary-dark: "#F3F6FA"
  text-primary-light: "#172B42"
  text-secondary-dark: "#C2CFDD"
  text-secondary-light: "#3E536C"
  text-muted-dark: "#A6B7C9"
  text-muted-light: "#5C6E84"
  navigation-dark: "#222C37"
  navigation-light: "#EFF3F8"
  badge-dark: "#304155"
  badge-light: "#E4EDF8"
  row-hover-dark: "#2D3C4C"
  row-hover-light: "#ECF2FA"
  category-icon-dark: "#304155"
  category-icon-light: "#E5EDF8"
  gauge-track-dark: "#4A5C70"
  gauge-track-light: "#D9E2ED"
typography:
  headline:
    fontFamily: "Segoe UI Variable Display, Segoe UI"
    fontSize: "24px"
    fontWeight: 600
  title:
    fontFamily: "Segoe UI Variable Text, Segoe UI"
    fontSize: "14px"
    fontWeight: 600
  body:
    fontFamily: "Segoe UI Variable Text, Segoe UI"
    fontSize: "13px"
    fontWeight: 400
  label:
    fontFamily: "Segoe UI Variable Text, Segoe UI"
    fontSize: "12px"
  caption:
    fontFamily: "Segoe UI Variable Text, Segoe UI"
    fontSize: "11px"
  mono:
    fontFamily: "Cascadia Mono, Consolas"
    fontSize: "12px"
rounded:
  table: "4px"
  surface: "6px"
  row: "8px"
  dialog: "10px"
spacing:
  space-1: "4px"
  space-2: "8px"
  space-3: "12px"
  space-4: "16px"
  page-inline: "20px"
  space-6: "24px"
components:
  button-primary-dark:
    backgroundColor: "{colors.accent-dark}"
    textColor: "{colors.primary-foreground-dark}"
    rounded: "{rounded.surface}"
    padding: "8px 14px"
  button-primary-light:
    backgroundColor: "{colors.accent-light}"
    textColor: "{colors.primary-foreground-light}"
    rounded: "{rounded.surface}"
    padding: "8px 14px"
  preset-chip-dark:
    backgroundColor: "{colors.bg-overlay-dark}"
    textColor: "{colors.text-primary-dark}"
    rounded: "{rounded.surface}"
    padding: "6px 12px"
  preset-chip-selected-dark:
    backgroundColor: "{colors.accent-soft-dark}"
    textColor: "{colors.text-primary-dark}"
    rounded: "{rounded.surface}"
    padding: "6px 12px"
  card-dark:
    backgroundColor: "{colors.bg-elevated-dark}"
    textColor: "{colors.text-primary-dark}"
    rounded: "{rounded.surface}"
    padding: "16px"
  card-light:
    backgroundColor: "{colors.bg-elevated-light}"
    textColor: "{colors.text-primary-light}"
    rounded: "{rounded.surface}"
    padding: "16px"
  navigation-item-dark:
    textColor: "{colors.text-secondary-dark}"
    typography: "{typography.label}"
    padding: "12px 10px"
  navigation-selected-dark:
    backgroundColor: "{colors.accent-soft-dark}"
    textColor: "{colors.accent-dark}"
    typography: "{typography.label}"
    padding: "12px 10px"
  operation-row:
    rounded: "{rounded.row}"
    padding: "6px 10px"
  operation-row-comfortable:
    rounded: "{rounded.row}"
    padding: "10px 14px"
  session-item-dark:
    backgroundColor: "{colors.bg-overlay-dark}"
    textColor: "{colors.text-primary-dark}"
    rounded: "{rounded.dialog}"
    padding: "14px"
---

# Design System: Cleaner — Graphite

## Overview

**Creative North Star: "Graphite"**

A compact Windows workspace that makes the next action and its consequences easy to identify. The approved dark and light Graphite references establish matte surfaces, cool blue actions, native Windows typography and horizontal navigation. Selection, analysis, review, confirmation, execution and an honest result define the journey; decoration leaves room for useful data.

Dense tables, restrained corners and explanatory text support people without technical knowledge. Fluent line icons accompany commands and destinations. Both themes share the hierarchy; accent and density remain independent preferences. Preserve real commands, presets, exclusions, cancellation, recovery, safety notices and opt-in automation when extending a surface.

This record describes source, not completed Windows visual certification. Authority: `Cleaner.App/Services/Appearance.cs`, `Themes/Tokens.xaml`, `App.xaml`, `MainWindow.xaml` and all nine page XAML files. Runtime colors supersede older startup values in `Tokens.xaml`. The approved finish still requires actual Russian/English, both-theme, small/normal-window, keyboard, empty/error and 100–200% scaling checks; this record does not establish that those checks passed.

**Key Characteristics:**

- Matte graphite and pale neutral surfaces with semantic theme brushes.
- Compact horizontal navigation and data-focused rows.
- Native Windows type and Fluent line icons.
- Explicit consequences, readable status text and distinct recovery semantics.

## Colors

Cool neutral layers carry the workspace; a selectable accent identifies commands and selection. Frontmatter records runtime blue defaults and supported teal/violet alternatives, with dark/light values separated.

Theme preference stores `dark` or `light`; absent or invalid legacy values resolve to dark. Failed persistence restores the previous choice before appearance is reapplied (`UserSettings` and `SettingsViewModel`). Existing pages, menus and dialogs should resolve their theme-sensitive brushes dynamically so preference changes apply together.

### Primary

- **Cool blue:** `AccentBrush`, `PrimaryFillBrush` and `AccentHoverBrush` share the selected accent; runtime hover does not introduce a second hue.
- **Soft selection:** `AccentSoftBrush` uses the selected accent at alpha 32/255 in dark mode and 24/255 in light mode. Frontmatter records blue defaults; teal/violet soft fills derive through the same rule.
- **Primary foreground:** dark ink on the dark-theme fill; white on the light-theme fill. Keep each fill/foreground pair together.
- **Teal / violet:** selectable replacements for the blue accent, not additional simultaneous action colors.

### Secondary

- **Safe, caution, danger and information:** functional status colors with explanatory text or icons. Occupancy thresholds do not imply drive health.

### Neutral

- **Base / elevated / overlay / hover:** window, resting containers, nested or overlay surfaces, and interactive emphasis.
- **Subtle / strong border:** quiet separation versus stronger interaction or overlay boundaries.
- **Primary / secondary / muted text:** decisions and content, explanations, then metadata. `TextTertiaryBrush` aliases muted text.
- **Navigation:** the horizontal strip retains the legacy brush name `SidebarBrush`.
- **Badge / category icon / row hover / gauge track:** compact marks, category support, pointer response and unfilled capacity gauge.
- `CardBgBrush`, `CardBorderBrush`, `CardHoverBgBrush`, `SuccessBrush`, `WarningBrush`, `GradientCleanBrush` and `AccentGlowBrush` alias current semantic brushes. Their names do not establish additional colors or gradients.

### Named Rules

**The Paired Theme Rule.** Resolve theme-sensitive colors through dynamic semantic brushes; switch fill, text, border and state treatments together.

**The Explained Status Rule.** Warnings, compatibility and irreversible actions require explanatory text or icons; color alone cannot carry their meaning.

## Typography

**Heading font:** Segoe UI Variable Display, with Segoe UI fallback.  
**Body font:** Segoe UI Variable Text, with Segoe UI fallback.  
**Technical font:** Cascadia Mono, with Consolas fallback; several path/command rows explicitly use Consolas.

These native Windows families are part of the approved desktop direction. Regular body text and semibold titles establish hierarchy without a promotional display treatment.

### Hierarchy

- **Headline:** shared semibold page title style.
- **Title:** semibold category names and compact section values; local sections also use 16px semibold text.
- **Body:** operation names and wrapping secondary-text subtitles.
- **Label:** table headings, metadata and hints; paths may trim with full text available through tooltips.
- **Caption:** title-bar metrics and muted semibold section labels.
- **Mono:** technical identifiers and paths, never ordinary UI labels.

The 30px number and 20px title resources are not the page-title hierarchy: `PageTitleStyle` explicitly sets the headline size, and the sole `NumberTextStyle` instance overrides its size to 12px. Contextual summary values and dialog titles do not establish a second global scale. No global line-height or tracking scale is implemented.

### Named Rules

**The Consequence Column Rule.** Keep operation names and consequences distinct through primary/secondary text, aligned columns and expandable detail.

## Layout

All nine pages use outer margins of 20 horizontal and 16 vertical WPF device-independent units. Frontmatter uses CSS px for portable previews; application dimensions are WPF DIPs. A 4-unit rhythm supports compact local adjustments.

The shell defaults to 1360 × 860, with minimum 1120 × 700. Its 34-unit title bar contains title/version and optional metrics. Six labeled destinations run horizontally below. History, What's new, search and a standalone settings gear occupy a separate right utility zone. Primary navigation allows horizontal scrolling when constrained; the workspace fills the rest. This desktop implementation defines no mobile breakpoint.

Headings precede controls and useful lists. Cleanup/optimization align operation and consequence; Startup, History, Recovery and Disk use appropriate name, source, path, size and state columns. Table headers use overlay fill, a subtle bottom rule and 12px secondary labels. Actions and status remain explicit.

Cleanup reserves a 224-unit summary column plus 12-unit gutter. Below page width 860, code hides that column, reveals the compact inline summary and removes the gutter. This is source behavior, not evidence that the normal shell can reach that threshold at its minimum size. Recovery uses a 224-unit session column; the disk map uses a 300-unit capacity column and 12-unit gap. Settings scrolls within maximum width 1120, with 210-unit selectors; its local card padding is 10 and compact row/divider spacing is 6.

`OperationRowPadding` switches compact/comfortable row padding without changing theme or accent. Preserve wrapping, tooltips and scrolling for both languages and long paths. Actual minimum-window and scaled output require verification.

## Elevation & Depth

Resting surfaces are flat, distinguished by tone, borders and alignment. The palette and confirmation dialog use a large diffuse shadow, strong border and dimming scrim. Smaller shadow resources exist but are not applied by reviewed pages or shell; they are not active component rules.

### Shadow Vocabulary

- **Overlay:** `ShadowLg`: black, WPF blur radius 40, depth 12, direction 270, opacity 0.45. The sidecar CSS is a preview translation, not an assertion of identical WPF/browser rasterization.
- **Scrims:** command palette uses black alpha 128/255; confirmation uses black alpha 153/255; safety uses source graphite tint alpha 179/255.

### Named Rules

**The Overlay Depth Rule.** Reserve the observed large shadow for temporary overlays; resting tables and cards communicate hierarchy through tone and borders.

No active page-entry animation is established. Reviewed page code contains uncalled `AnimateIn` methods, and fast/base duration resources are unused by these surfaces. Do not canonize dormant timings. Primary-button opacity changes respond directly to hover, press and disabled state.

## Shapes

Cards, chips, primary buttons and category headers use the compact surface radius. Hover rows use the softer row radius; table headers and focus wrappers use tighter corners. Dialogs, the command palette and session items use the dialog radius. Table headers round only their top corners; navigation items stay rectangular with a bottom marker.

Borders are generally one unit. Primary buttons reserve a two-unit transparent border that becomes visible on keyboard focus. Unused radius aliases, decorative gradients and older icon-badge styles do not establish rules for new surfaces.

## Components

### Buttons

Primary actions use the current accent with its paired foreground. `PrimaryButtonStyle` defines the surface radius, optional icon with 8-unit gap and two-unit focus border. Frontmatter padding records the cleanup run-button instance; other instances choose local padding. Hover opacity is 0.9, pressed 0.75 and disabled 0.45; keyboard focus uses primary text as border color.

Secondary/transparent controls retain WPF-UI templates. Their full geometry and states are dependency-owned, not inferred from the primary style. Search/settings utility buttons are 34-unit squares; labeled history/news controls include tooltips and accessible names.

### Chips

Preset chips use overlay fill, one-unit subtle border, surface radius and semibold body text, with 8-unit right/bottom gaps. Hover uses hover fill and strong border; selection uses soft accent fill and accent border. Keyboard focus within the chip changes its border to primary text, including when selected. They are selectable presets, not decorative eyebrows.

### Cards / Containers

`CardStyle` uses elevated fill, subtle one-unit border, surface radius and 16-unit padding. Category containers use tighter padding around expanders. Preserve shared styles and observed local overrides for data panels; resting cards have no shadow.

Session items use overlay fill, dialog-radius corners and 14-unit padding. Hover strengthens the border; selected items use soft accent fill and accent border. Keyboard focus also uses accent border.

### Inputs / Fields

Editable searches, preset fields and preference selectors use installed WPF/WPF-UI control themes. The application intentionally avoids implicit styles that overwrite native chrome. Source defines no universal input radius, fill, padding, error style or focus-ring token; do not invent those values.

Read-only detail fields remain selectable text; explicitly configured instances have transparent background and no border. Directories and diagnostic details wrap or scroll. The sidecar reproduces this source-defined read-only field rather than reconstructing dependency-owned editable-field chrome.

### Navigation

Six destinations use horizontal list items with labels and Fluent line icons. Default text is secondary; hover uses hover fill. Selection uses soft accent fill, accent text and a 3-unit bottom marker inset 8 units per side. Focus adds an accent border; disabled opacity is 0.45. Keep utilities separate and settings a standalone gear.

### Operation Tables and Expandable Detail

Rows expose checkbox, level indicator, operation name and consequence; hover uses the row-hover brush. Expansion reveals explanation, compatibility and diagnostics. Category headers use compact 10-by-7 padding with hover/focus feedback. Existing text chevrons are a source defect, not the icon standard; future indicators should follow the approved Fluent line-icon system.

Optimization keeps the operation name separate from a compact consequence column with a second warning line when present. These local rows use 16-unit line height, truncation with full-text tooltips and an explicit text level label. Expanded detail retains the complete description and compatibility information; the compact summary does not replace it.

History uses paired rectangle bars for permanently deleted and quarantined bytes, with a shared byte scale labeled at maximum, midpoint and zero. Direct per-method values and dates accompany the bars; the legend and semantic colors reinforce their meaning. Disk occupancy describes used space. These meanings stay stable across themes; they must not become health scores or same-volume quarantine space-reclaimed claims.

### Settings Groups

Language, theme, density and accent lead the appearance group. Animations and metrics sit within the additional-appearance expander. Start page, Windows startup and opt-in automatic cleanup precede updates and expert settings. Compact hints use a local 2-unit top margin; this page-specific density does not change the shared card default.

## Do's and Don'ts

### Do:

- **Do** use dynamic semantic brushes and switch the full light/dark state together.
- **Do** retain horizontal destinations, a separate utility zone and a standalone settings gear.
- **Do** use compact rows, aligned consequence columns and expandable supporting detail.
- **Do** pair status colors with meaningful text or icons and retain explicit confirmation for consequential actions.
- **Do** preserve native Windows typography, Fluent line icons, keyboard interaction and accessible names.
- **Do** verify languages, themes, density, constrained windows and 100–200% scaling against actual output.

### Don't:

- **Don't** use mock values, health scores, invented delays or performance guarantees as visual content.
- **Don't** report same-volume quarantined bytes as reclaimed space or merge them with permanently deleted bytes.
- **Don't** copy startup fallback colors over the runtime palette or use stale brush names to introduce decorative gradients.
- **Don't** turn dormant methods or unused resources into rules for new surfaces.
- **Don't** replace native controls or safety behavior with rasterized interface imagery.
