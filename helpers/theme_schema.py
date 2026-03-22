"""
Theme JSON Schema for Stock Analyzer
Defines the complete structure matching existing themes (dark.json, neon-noir.json)
"""

# Complete theme structure for Claude's structured output
THEME_SCHEMA = {
    "type": "object",
    "required": ["id", "name", "version", "meta", "variables", "effects", "fonts"],
    "properties": {
        "id": {
            "type": "string",
            "description": "Unique theme identifier (lowercase, hyphens allowed)"
        },
        "name": {
            "type": "string",
            "description": "Human-readable theme name"
        },
        "version": {
            "type": "string",
            "description": "Semantic version (e.g., 1.0.0)"
        },
        "meta": {
            "type": "object",
            "required": ["category", "icon", "iconColor"],
            "properties": {
                "category": {
                    "type": "string",
                    "enum": ["light", "dark"],
                    "description": "Base category for the theme"
                },
                "icon": {
                    "type": "string",
                    "description": "Icon name (sun, moon, bolt, etc.)"
                },
                "iconColor": {
                    "type": "string",
                    "description": "Hex color for the icon"
                },
                "tags": {
                    "type": "array",
                    "items": {"type": "string"},
                    "description": "Optional tags describing the theme"
                },
                "originalPrompt": {
                    "type": "string",
                    "description": "The prompt that generated this theme"
                }
            }
        },
        "variables": {
            "type": "object",
            "description": "CSS custom properties for the theme",
            "required": [
                "bg-primary", "bg-secondary", "bg-tertiary", "bg-code",
                "text-primary", "text-secondary", "text-muted", "text-inverted",
                "border-primary", "border-secondary",
                "accent", "accent-hover", "accent-light", "accent-bg", "accent-bg-subtle",
                "success", "error", "warning", "warning-light",
                "btn-primary-bg", "btn-primary-bg-hover", "btn-primary-text",
                "chart-bg", "chart-text", "chart-grid", "chart-line-primary"
            ],
            "properties": {
                # Backgrounds
                "bg-primary": {"type": "string", "description": "Main background color"},
                "bg-secondary": {"type": "string", "description": "Secondary/sidebar background"},
                "bg-tertiary": {"type": "string", "description": "Tertiary/card background"},
                "bg-code": {"type": "string", "description": "Code block background"},

                # Text
                "text-primary": {"type": "string", "description": "Primary text color"},
                "text-secondary": {"type": "string", "description": "Secondary text color"},
                "text-muted": {"type": "string", "description": "Muted/disabled text color"},
                "text-inverted": {"type": "string", "description": "Text on accent backgrounds"},

                # Borders
                "border-primary": {"type": "string", "description": "Primary border color"},
                "border-secondary": {"type": "string", "description": "Secondary border color"},

                # Accent colors
                "accent": {"type": "string", "description": "Primary accent/brand color"},
                "accent-hover": {"type": "string", "description": "Accent hover state"},
                "accent-light": {"type": "string", "description": "Lighter accent variant"},
                "accent-bg": {"type": "string", "description": "Accent background (semi-transparent)"},
                "accent-bg-subtle": {"type": "string", "description": "Subtle accent background"},

                # Status colors
                "success": {"type": "string", "description": "Success/positive color"},
                "error": {"type": "string", "description": "Error/negative color"},
                "warning": {"type": "string", "description": "Warning color"},
                "warning-light": {"type": "string", "description": "Light warning variant"},

                # Highlights
                "highlight-bg": {"type": "string", "description": "Text highlight background"},
                "highlight-text": {"type": "string", "description": "Text highlight foreground"},

                # Danger zone
                "danger-bg": {"type": "string", "description": "Danger area background"},
                "danger-border": {"type": "string", "description": "Danger border color"},

                # Star/favorite
                "star-color": {"type": "string", "description": "Star/favorite icon color"},
                "star-bg": {"type": "string", "description": "Star button background"},
                "star-glow": {"type": "string", "description": "Star glow effect (or 'none')"},

                # Price indicators
                "price-up": {"type": "string", "description": "Price increase color"},
                "price-up-glow": {"type": "string", "description": "Price up glow (or 'none')"},
                "price-down": {"type": "string", "description": "Price decrease color"},
                "price-down-glow": {"type": "string", "description": "Price down glow (or 'none')"},

                # Audio
                "audio-active-bg": {"type": "string", "description": "Audio active state background"},

                # Music visualizer
                "music-active-color": {"type": "string", "description": "Music active state color"},
                "music-active-bg": {"type": "string", "description": "Music active background"},
                "music-active-glow": {"type": "string", "description": "Music glow effect (or 'none')"},
                "viz-bar-color": {"type": "string", "description": "Visualizer bar color"},
                "viz-bar-glow": {"type": "string", "description": "Visualizer bar glow (or 'none')"},

                # Buttons
                "btn-primary-bg": {"type": "string", "description": "Primary button background"},
                "btn-primary-bg-hover": {"type": "string", "description": "Primary button hover"},
                "btn-primary-text": {"type": "string", "description": "Primary button text color"},
                "btn-primary-glow": {"type": "string", "description": "Primary button glow (or 'none')"},
                "btn-primary-glow-hover": {"type": "string", "description": "Button glow on hover (or 'none')"},

                # Loader
                "loader-bg": {"type": "string", "description": "Loader background"},
                "loader-accent": {"type": "string", "description": "Loader accent/spinner color"},

                # Shadows
                "shadow-sm": {"type": "string", "description": "Small shadow"},
                "shadow-md": {"type": "string", "description": "Medium shadow"},
                "shadow-lg": {"type": "string", "description": "Large shadow"},
                "shadow-xl": {"type": "string", "description": "Extra large shadow"},

                # Border radius
                "radius-sm": {"type": "string", "description": "Small border radius"},
                "radius-md": {"type": "string", "description": "Medium border radius"},
                "radius-lg": {"type": "string", "description": "Large border radius"},

                # Tile headers
                "tile-title-color": {"type": "string", "description": "Tile header text color"},
                "tile-title-transform": {"type": "string", "description": "Text transform (none/uppercase)"},
                "tile-title-spacing": {"type": "string", "description": "Letter spacing"},
                "tile-title-weight": {"type": "string", "description": "Font weight"},
                "tile-title-glow": {"type": "string", "description": "Title glow effect (or 'none')"},

                # Chart colors
                "chart-bg": {"type": "string", "description": "Chart background"},
                "chart-text": {"type": "string", "description": "Chart text color"},
                "chart-grid": {"type": "string", "description": "Chart grid lines"},
                "chart-axis": {"type": "string", "description": "Chart axis color"},
                "chart-line-primary": {"type": "string", "description": "Primary line color"},
                "chart-line-secondary": {"type": "string", "description": "Secondary line color"},
                "chart-line-sma20": {"type": "string", "description": "SMA 20 line color"},
                "chart-line-sma50": {"type": "string", "description": "SMA 50 line color"},
                "chart-line-sma200": {"type": "string", "description": "SMA 200 line color"},
                "chart-candle-up": {"type": "string", "description": "Bullish candle color"},
                "chart-candle-down": {"type": "string", "description": "Bearish candle color"},
                "chart-volume-up": {"type": "string", "description": "Volume up bar color"},
                "chart-volume-down": {"type": "string", "description": "Volume down bar color"},
                "chart-rsi": {"type": "string", "description": "RSI indicator color"},
                "chart-macd": {"type": "string", "description": "MACD line color"},
                "chart-macd-signal": {"type": "string", "description": "MACD signal line color"},
                "chart-stochastic": {"type": "string", "description": "Stochastic %K color"},
                "chart-stochastic-d": {"type": "string", "description": "Stochastic %D color"},
                "chart-overbought": {"type": "string", "description": "Overbought zone color"},
                "chart-oversold": {"type": "string", "description": "Oversold zone color"},
                "chart-bollinger": {"type": "string", "description": "Bollinger bands color"},
                "chart-line-glow": {"type": "string", "description": "Chart line glow (enabled/none)"},
                "chart-line-glow-color": {"type": "string", "description": "Glow color"},
                "chart-line-glow-width": {"type": "string", "description": "Glow width in pixels"},
                "chart-marker-up": {"type": "string", "description": "Positive marker fill"},
                "chart-marker-down": {"type": "string", "description": "Negative marker fill"},
                "chart-marker-up-outline": {"type": "string", "description": "Positive marker outline"},
                "chart-marker-down-outline": {"type": "string", "description": "Negative marker outline"},
                "chart-marker-symbol": {"type": "string", "description": "Marker shape (triangle/diamond/circle)"},
                "chart-marker-size": {"type": "string", "description": "Marker size in pixels"},

                # Grid dots
                "grid-dot": {"type": "string", "description": "Grid dot color"},
                "grid-dot-active": {"type": "string", "description": "Active grid dot color"},

                # Zoom selection
                "zoom-bg": {"type": "string", "description": "Zoom selection background"},
                "zoom-border": {"type": "string", "description": "Zoom selection border"},

                # Measure tool
                "measure-bg": {"type": "string", "description": "Measure tool background"},
                "measure-line": {"type": "string", "description": "Measure tool line color"},

                # Placeholder
                "placeholder-bg": {"type": "string", "description": "Placeholder background"},
                "placeholder-border": {"type": "string", "description": "Placeholder border"},

                # Locked pattern
                "locked-pattern": {"type": "string", "description": "Locked tile pattern opacity"}
            }
        },
        "effects": {
            "type": "object",
            "description": "Optional visual effects",
            "properties": {
                "scanlines": {
                    "type": "object",
                    "properties": {
                        "enabled": {"type": "boolean"},
                        "opacity": {"type": "number", "minimum": 0, "maximum": 1},
                        "spacing": {"type": "integer", "minimum": 1, "maximum": 10}
                    }
                },
                "vignette": {
                    "type": "object",
                    "properties": {
                        "enabled": {"type": "boolean"},
                        "strength": {"type": "number", "minimum": 0, "maximum": 1}
                    }
                },
                "crtFlicker": {
                    "type": "object",
                    "properties": {
                        "enabled": {"type": "boolean"},
                        "intensity": {"type": "number", "minimum": 0, "maximum": 0.2}
                    }
                },
                "rain": {
                    "type": "object",
                    "properties": {
                        "enabled": {"type": "boolean"},
                        "color": {"type": "string"},
                        "speed": {"type": "number", "minimum": 0.1, "maximum": 2}
                    }
                },
                "bloom": {
                    "type": "object",
                    "properties": {
                        "enabled": {"type": "boolean"},
                        "contrast": {"type": "number", "minimum": 1, "maximum": 1.5},
                        "brightness": {"type": "number", "minimum": 1, "maximum": 1.5}
                    }
                },
                "matrixRain": {
                    "type": "object",
                    "description": "Canvas-based Matrix digital rain effect with falling characters",
                    "properties": {
                        "enabled": {"type": "boolean"},
                        "color": {"type": "string", "description": "Character color (default: #00ff41)"},
                        "backgroundColor": {"type": "string", "description": "Trail fade color (default: rgba(0,0,0,0.05))"},
                        "fontSize": {"type": "integer", "description": "Character size in pixels (default: 14)"},
                        "speed": {"type": "number", "description": "Fall speed multiplier (default: 1)"},
                        "density": {"type": "number", "description": "Column reset probability 0-1 (default: 0.98)"},
                        "characters": {"type": "string", "description": "Character set to use"},
                        "glowIntensity": {"type": "number", "description": "Leading character glow 0-1 (default: 0.8)"}
                    }
                },
                "snow": {
                    "type": "object",
                    "description": "Canvas-based falling snow effect",
                    "properties": {
                        "enabled": {"type": "boolean"},
                        "color": {"type": "string", "description": "Snowflake color (default: #ffffff)"},
                        "count": {"type": "integer", "description": "Number of snowflakes (default: 100)"},
                        "speed": {"type": "number", "description": "Fall speed multiplier (default: 1)"},
                        "wind": {"type": "number", "description": "Horizontal drift amount (default: 0.5)"}
                    }
                },
                "particles": {
                    "type": "object",
                    "description": "Canvas-based floating particles with optional connections",
                    "properties": {
                        "enabled": {"type": "boolean"},
                        "color": {"type": "string", "description": "Particle color (default: #ffffff)"},
                        "count": {"type": "integer", "description": "Number of particles (default: 50)"},
                        "speed": {"type": "number", "description": "Movement speed (default: 0.5)"},
                        "connections": {"type": "boolean", "description": "Draw lines between nearby particles (default: true)"},
                        "connectionDistance": {"type": "integer", "description": "Max distance for connections (default: 100)"}
                    }
                }
            }
        },
        "fonts": {
            "type": "object",
            "required": ["primary", "mono"],
            "properties": {
                "primary": {"type": "string", "description": "Primary font stack"},
                "mono": {"type": "string", "description": "Monospace font stack"}
            }
        },
        "overrideCSS": {
            "type": "string",
            "description": "Optional raw CSS overrides"
        },
        "background": {
            "type": "object",
            "description": "Full-screen background configuration",
            "properties": {
                "image": {
                    "type": "string",
                    "description": "URL to background image (local /images/ or external)"
                },
                "overlay": {
                    "type": "string",
                    "description": "Color/gradient overlay for readability (e.g., 'rgba(0,0,0,0.7)')"
                },
                "position": {
                    "type": "string",
                    "description": "CSS background-position (default: center)"
                },
                "size": {
                    "type": "string",
                    "description": "CSS background-size (default: cover)"
                },
                "attachment": {
                    "type": "string",
                    "enum": ["fixed", "scroll"],
                    "description": "CSS background-attachment (default: fixed)"
                },
                "blur": {
                    "type": "number",
                    "description": "Blur amount in pixels (0 = none)"
                }
            }
        },
        "customCSS": {
            "type": "object",
            "description": "Custom CSS declarations for specific UI elements. Each key is a slot name, each value is CSS property declarations (no selectors). Use this for creative styling beyond color variables - transforms, filters, animations, etc.",
            "additionalProperties": False,
            "properties": {
                "container": {"type": "string", "description": "Main preview container - use for overall transforms, filters"},
                "header": {"type": "string", "description": "Header section - backdrop-filter, gradients, etc."},
                "headerContent": {"type": "string", "description": "Header content wrapper"},
                "logo": {"type": "string", "description": "Logo container"},
                "logoText": {"type": "string", "description": "Logo text - font-style, text-shadow, etc."},
                "tiles": {"type": "string", "description": "All tiles - border-radius, transform, box-shadow, etc."},
                "tileHeaders": {"type": "string", "description": "Tile header bars"},
                "tileTitles": {"type": "string", "description": "Tile titles"},
                "tileBodies": {"type": "string", "description": "Tile body content areas"},
                "buttons": {"type": "string", "description": "All buttons"},
                "buttonsPrimary": {"type": "string", "description": "Primary action buttons"},
                "buttonsSecondary": {"type": "string", "description": "Secondary buttons"},
                "buttonsIcon": {"type": "string", "description": "Icon buttons"},
                "inputs": {"type": "string", "description": "Input fields"},
                "inputGroups": {"type": "string", "description": "Input group containers"},
                "searchSection": {"type": "string", "description": "Search/filter section"},
                "chart": {"type": "string", "description": "Chart canvas element"},
                "chartTile": {"type": "string", "description": "Chart tile container"},
                "watchlist": {"type": "string", "description": "Watchlist body"},
                "watchlistItems": {"type": "string", "description": "Individual watchlist rows"},
                "watchlistTicker": {"type": "string", "description": "Ticker symbols in watchlist"},
                "watchlistChange": {"type": "string", "description": "Change percentages"},
                "metrics": {"type": "string", "description": "Metrics grid"},
                "metricValues": {"type": "string", "description": "Metric value text"},
                "metricLabels": {"type": "string", "description": "Metric label text"},
                "footer": {"type": "string", "description": "Footer section"},
                "footerLinks": {"type": "string", "description": "Footer links"},
                "checkboxes": {"type": "string", "description": "Checkbox labels"},
                "markers": {"type": "string", "description": "Significant move markers (both up and down)"},
                "markersUp": {"type": "string", "description": "Up/positive markers"},
                "markersDown": {"type": "string", "description": "Down/negative markers"},
                "effects": {"type": "string", "description": "Effects overlay layer"},
                "grid": {"type": "string", "description": "Content grid layout"}
            }
        },
        "audio": {
            "type": "object",
            "description": "Music theory-driven audio parameters for procedural synthesis",
            "properties": {
                "key": {
                    "type": "string",
                    "description": "Root key (C, C#, D, Eb, E, F, F#, G, Ab, A, Bb, B)"
                },
                "mode": {
                    "type": "string",
                    "enum": ["major", "minor", "dorian", "phrygian", "lydian", "mixolydian", "locrian", "harmonic_minor", "melodic_minor"],
                    "description": "Scale/mode"
                },
                "chordProgression": {
                    "type": "array",
                    "items": {"type": "string"},
                    "description": "Chord progression using Roman numerals (i, iv, V, i) or chord names (Am, Dm, E7, Am)"
                },
                "chordVoicing": {
                    "type": "string",
                    "enum": ["triad", "seventh", "ninth", "suspended", "power"],
                    "description": "Chord voicing complexity"
                },
                "tempo": {
                    "type": "number",
                    "description": "BPM (beats per minute), or 0 for free time/ambient"
                },
                "chordDuration": {
                    "type": "number",
                    "description": "Seconds per chord change"
                },
                "octave": {
                    "type": "integer",
                    "minimum": 1,
                    "maximum": 6,
                    "description": "Base octave (1=bass, 3=middle, 5=high)"
                },
                "texture": {
                    "type": "string",
                    "enum": ["drone", "pad", "arpeggiated", "pulsing", "staccato", "swelling"],
                    "description": "Rhythmic/textural style"
                },
                "oscillator": {
                    "type": "string",
                    "enum": ["sine", "triangle", "sawtooth", "square"],
                    "description": "Primary oscillator waveform"
                },
                "detune": {
                    "type": "number",
                    "description": "Detune amount for chorus effect (1.0 = none, 1.01 = slight, 1.05 = heavy)"
                },
                "filterCutoff": {
                    "type": "object",
                    "properties": {
                        "min": {"type": "number", "description": "Minimum filter frequency Hz"},
                        "max": {"type": "number", "description": "Maximum filter frequency Hz"}
                    },
                    "description": "Lowpass filter sweep range"
                },
                "reverb": {
                    "type": "object",
                    "properties": {
                        "duration": {"type": "number", "description": "Reverb tail length in seconds"},
                        "decay": {"type": "number", "description": "Decay rate (1=fast, 5=slow)"},
                        "wetDry": {"type": "number", "description": "Wet/dry mix (0=dry, 1=all reverb)"}
                    },
                    "description": "Reverb parameters"
                },
                "pitchShift": {
                    "type": "number",
                    "description": "Pitch multiplier (1.0=normal, 0.8=slowed/vaporwave, 0.5=very slow)"
                },
                "style": {
                    "type": "string",
                    "description": "Style hint for future expansion (baroque, romantic, minimalist, ambient, industrial)"
                }
            }
        }
    }
}

# Default values for a dark theme base
DARK_BASE_DEFAULTS = {
    "bg-primary": "#1f2937",
    "bg-secondary": "#111827",
    "bg-tertiary": "#374151",
    "bg-code": "#1f2937",
    "text-primary": "#f9fafb",
    "text-secondary": "#d1d5db",
    "text-muted": "#9ca3af",
    "text-inverted": "#e5e7eb",
    "border-primary": "#374151",
    "border-secondary": "#4b5563",
    "shadow-sm": "0 1px 2px rgba(0,0,0,0.1)",
    "shadow-md": "0 4px 6px -1px rgba(0,0,0,0.3)",
    "shadow-lg": "0 10px 15px -3px rgba(0,0,0,0.3)",
    "shadow-xl": "0 25px 50px -12px rgba(0,0,0,0.5)",
    "radius-sm": "0.25rem",
    "radius-md": "0.375rem",
    "radius-lg": "0.5rem",
    "tile-title-transform": "none",
    "tile-title-spacing": "normal",
    "tile-title-weight": "600",
    "tile-title-glow": "none",
    "chart-marker-symbol": "triangle",
    "chart-marker-size": "22",
    "chart-line-glow": "none",
    "chart-line-glow-color": "transparent",
    "chart-line-glow-width": "0",
    "locked-pattern": "rgba(255,255,255,0.02)"
}

# Default values for a light theme base
LIGHT_BASE_DEFAULTS = {
    "bg-primary": "#ffffff",
    "bg-secondary": "#f8fafc",
    "bg-tertiary": "#f1f5f9",
    "bg-code": "#f1f5f9",
    "text-primary": "#1e293b",
    "text-secondary": "#475569",
    "text-muted": "#94a3b8",
    "text-inverted": "#1e293b",
    "border-primary": "#e2e8f0",
    "border-secondary": "#cbd5e1",
    "shadow-sm": "0 1px 2px rgba(0,0,0,0.05)",
    "shadow-md": "0 4px 6px -1px rgba(0,0,0,0.1)",
    "shadow-lg": "0 10px 15px -3px rgba(0,0,0,0.1)",
    "shadow-xl": "0 25px 50px -12px rgba(0,0,0,0.25)",
    "radius-sm": "0.25rem",
    "radius-md": "0.375rem",
    "radius-lg": "0.5rem",
    "tile-title-transform": "none",
    "tile-title-spacing": "normal",
    "tile-title-weight": "600",
    "tile-title-glow": "none",
    "chart-marker-symbol": "triangle",
    "chart-marker-size": "22",
    "chart-line-glow": "none",
    "chart-line-glow-color": "transparent",
    "chart-line-glow-width": "0",
    "locked-pattern": "rgba(0,0,0,0.02)"
}

# Default fonts
DEFAULT_FONTS = {
    "primary": "ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif",
    "mono": "ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono', 'Courier New', monospace"
}
