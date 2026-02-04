# Theming Guide — Framework-First Approach

## Core Principle

**Themes ONLY override CSS variables. Themes NEVER add custom selectors or rules.**

When implementing theming features:
1. Add the CSS variable to the framework (`:root` in `input.css`)
2. Add the component rule that uses the variable (in the framework section)
3. Themes then override the variable value only

This ensures any new theme can be created by only overriding variables, with no need to write component-specific CSS.

---

## Architecture

### CSS Layer Structure (input.css)

```
:root {
    /* Framework variables - ALL themeable properties */
    --radius-md: 0.375rem;
    --tile-title-color: var(--text-primary);
    --chart-marker-symbol: triangle;
    /* etc. */
}

.dark {
    /* Dark mode variable overrides */
}

.neon-noir {
    /* Neon Noir variable overrides ONLY */
    --radius-md: 0;
    --tile-title-color: #ff71ce;
    --chart-marker-symbol: diamond;
}

/* Framework component rules - use variables */
.tile-card {
    border-radius: var(--radius-md) !important;
}

.tile-title {
    color: var(--tile-title-color);
}

/* Theme-specific EFFECTS (not colors/sizes) go here */
.neon-noir .tile-card::before { /* animated border sweep */ }
html.neon-noir::before { /* scanlines overlay */ }
```

### JavaScript Layer (charts.js)

Charts read theme colors from CSS variables via `getThemeColors()`:

```javascript
getThemeColors() {
    return {
        markerSymbol: this.getCssVar('--chart-marker-symbol') || 'triangle',
        markerSize: parseInt(this.getCssVar('--chart-marker-size')) || 22,
        // ... all chart theming from CSS
    };
}
```

Traces then use these values:
```javascript
marker: {
    symbol: themeColors.markerSymbol + '-up',
    size: themeColors.markerSize,
    color: themeColors.markerUp
}
```

---

## Adding a New Themeable Property

### Step 1: Add CSS Variable to Framework

In `input.css`, add to `:root`:

```css
:root {
    /* ... existing vars ... */
    --my-new-property: default-value;
}
```

### Step 2: Add Component Rule Using Variable

In the framework section (NOT inside a theme selector):

```css
.my-component {
    some-property: var(--my-new-property);
}
```

### Step 3: Override in Theme

In the theme selector (e.g., `.neon-noir`):

```css
.neon-noir {
    --my-new-property: theme-specific-value;
}
```

### Step 4: For JavaScript (Charts)

If the property affects charts, add to `getThemeColors()`:

```javascript
myNewProperty: this.getCssVar('--my-new-property') || 'fallback'
```

---

## What Goes Where

| Item | Location | Example |
|------|----------|---------|
| Default values | `:root` | `--radius-md: 0.375rem` |
| Dark mode defaults | `.dark` | `--chart-bg: #1f2937` |
| Theme overrides | `.theme-name` | `--radius-md: 0` |
| Component rules | Framework section | `.tile-card { border-radius: var(--radius-md) }` |
| Visual effects | Theme section | `.neon-noir::before { /* scanlines */ }` |

---

## Categories of Themeable Properties

### Colors
```css
--bg-primary, --text-primary, --accent, --border-primary
--chart-line-primary, --chart-candle-up, --chart-marker-up
```

### Structural
```css
--radius-sm, --radius-md, --radius-lg  /* corners */
--shadow-sm, --shadow-md, --shadow-lg  /* elevation */
```

### Typography
```css
--tile-title-color, --tile-title-glow
--tile-title-transform, --tile-title-spacing, --tile-title-weight
```

### Charts (Plotly.js)
```css
--chart-bg, --chart-text, --chart-grid
--chart-line-primary, --chart-line-glow, --chart-line-glow-color
--chart-marker-symbol, --chart-marker-size
--chart-marker-up, --chart-marker-down
```

---

## Forbidden Patterns

### WRONG: Theme adds component selectors

```css
/* DON'T DO THIS */
.neon-noir .tile-card {
    border-radius: 0;  /* Should be a variable override! */
}
```

### WRONG: Hardcoded values in JS

```javascript
/* DON'T DO THIS */
marker: {
    symbol: 'triangle-up',  /* Should be themeColors.markerSymbol + '-up' */
    size: 22                /* Should be themeColors.markerSize */
}
```

### WRONG: Framework rule without variable

```css
/* DON'T DO THIS */
.tile-card {
    border-radius: 0.375rem;  /* Should use var(--radius-md) */
}
```

---

## Testing Themes

When creating or modifying themes:

1. **Check all framework variables** — Does the theme override the variables it needs?
2. **Check for leaking selectors** — Does the theme add any component rules that should be framework-level?
3. **Test in isolation** — Disable the theme and verify the framework defaults work
4. **Cross-theme compatibility** — Does removing this theme break anything?

---

## Visual Effects (Exception to Variable-Only Rule)

Some effects require pseudo-elements or animations that can't be expressed as simple variable overrides. These are allowed as theme-specific CSS, but should be kept minimal:

- Scanlines overlay (`html.theme::before`)
- Rain/particle effects (`html.theme::after`)
- Animated border sweeps (`.theme .card::before`)
- Glow pulses (`@keyframes theme-pulse`)

These effects enhance the theme but the base layout/colors still come from variables.

---

## Adding a New Theme

1. Create a new selector (e.g., `.synthwave`)
2. Override ONLY CSS variables inside it
3. Add any essential visual effects (scanlines, etc.)
4. Test that removing the theme class returns to framework defaults

Example minimal theme:

```css
.synthwave {
    /* Colors */
    --bg-primary: #1a1a2e;
    --accent: #e94560;

    /* Structure */
    --radius-md: 0.25rem;

    /* Charts */
    --chart-line-primary: #e94560;
    --chart-marker-symbol: star;
}
```

That's it. No component selectors needed.

---

## Reference

- Framework CSS: `src/input.css`
- Chart JS: `wwwroot/js/charts.js`
- Design lessons: `research/DESIGN_IMPLEMENTATION_LESSONS.md`
