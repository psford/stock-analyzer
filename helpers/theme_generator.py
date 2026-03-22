"""
Theme Generator Service
FastAPI sidecar that uses Claude API to generate and refine theme JSON.

MODES:
    - Development (default): Returns mock themes, no API calls, no cost
    - Production: Set THEME_GENERATOR_LIVE=true to enable real API calls

Usage:
    cd helpers
    python theme_generator.py                    # Mock mode (default)
    THEME_GENERATOR_LIVE=true python theme_generator.py  # Live mode (costs tokens)
"""

import json
import os
import re
import random
from typing import Optional
from collections import defaultdict
from datetime import datetime, timedelta
from fastapi import FastAPI, HTTPException, Request
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from dotenv import load_dotenv

from theme_schema import (
    THEME_SCHEMA,
    DARK_BASE_DEFAULTS,
    LIGHT_BASE_DEFAULTS,
    DEFAULT_FONTS
)

# ============================================================================
# PROMPT SANITIZATION - Security boundary for user input
# ============================================================================

# Maximum allowed prompt length (prevents DoS, keeps API costs reasonable)
MAX_PROMPT_LENGTH = 2000

# Patterns that suggest prompt injection attempts
SUSPICIOUS_PATTERNS = [
    r"ignore\s+(previous|above|all)\s+(instructions?|prompts?)",
    r"forget\s+(everything|all|your)",
    r"you\s+are\s+now\s+a",
    r"system\s*:\s*",
    r"<\|.*\|>",  # Special tokens
    r"\[INST\]",  # Instruction markers
    r"```system",  # Fake system blocks
]

# Keywords that indicate a legitimate theme request
THEME_KEYWORDS = [
    # Colors
    "color", "colour", "red", "blue", "green", "yellow", "orange", "purple",
    "pink", "cyan", "magenta", "teal", "coral", "gold", "silver", "black",
    "white", "gray", "grey", "dark", "light", "bright", "muted", "vibrant",
    "pastel", "neon", "warm", "cool", "neutral",
    # Visual styles
    "theme", "style", "aesthetic", "vibe", "mood", "look", "feel",
    "retro", "modern", "minimal", "futuristic", "vintage", "classic",
    "cyberpunk", "vaporwave", "synthwave", "gothic", "elegant", "professional",
    "playful", "serious", "calm", "energetic", "cozy", "sleek",
    # Nature/environment
    "ocean", "forest", "sunset", "sunrise", "night", "day", "space",
    "nature", "earth", "sky", "water", "fire", "ice", "desert", "beach",
    "mountain", "garden", "autumn", "winter", "spring", "summer",
    # Effects
    "glow", "scanlines", "rain", "bloom", "vignette", "flicker", "crt",
    # UI elements
    "button", "background", "border", "shadow", "accent", "highlight",
    "contrast", "saturation", "brightness",
]

# Patterns that indicate off-topic/chatbot abuse
OFF_TOPIC_PATTERNS = [
    r"^(hi|hello|hey|what'?s? up|how are you)",  # Greetings
    r"(write|tell|give) me (a |an )?(story|poem|essay|joke|code|script)",
    r"(explain|what is|define|describe)\s+(the\s+)?(concept|meaning|definition)",
    r"(help me with|assist with|can you)\s+(my |a )?(homework|assignment|project)",
    r"(translate|convert)\s+.+\s+(to|into)\s+\w+",
    r"(who|what|where|when|why|how)\s+(is|are|was|were|did|does|do)",
    r"(summarize|analyze|review)\s+(this|the)\s+(article|book|paper|text)",
    r"(play|let'?s? play)\s+(a game|20 questions|trivia)",
    r"(pretend|act like|roleplay|imagine)\s+(you'?re?|as)",
    r"(recipe|cook|make)\s+.+\s+(food|dish|meal)",
    r"(weather|news|stock|price)\s+(in|for|of|today)",
]


def is_theme_related(prompt: str, strict: bool = False) -> tuple[bool, str]:
    """Check if a prompt is actually about theme generation.

    Args:
        prompt: The user's prompt
        strict: If True (live mode), requires theme keywords. If False (mock mode), only blocks obvious abuse.

    Returns:
        tuple: (is_valid, rejection_reason or empty string)
    """
    prompt_lower = prompt.lower()

    # Always block obvious chatbot abuse patterns (both modes)
    for pattern in OFF_TOPIC_PATTERNS:
        if re.search(pattern, prompt_lower):
            return False, "This tool generates visual themes. Try describing colors, moods, or aesthetics."

    # In mock mode, that's all we check - be permissive for creativity
    if not strict:
        return True, ""

    # In live mode (strict=True), also check for theme-related content
    # to prevent token burn on completely irrelevant prompts
    has_theme_keyword = any(keyword in prompt_lower for keyword in THEME_KEYWORDS)

    if not has_theme_keyword and len(prompt.split()) > 15:
        return False, "Your prompt doesn't seem to be about visual themes. Describe colors, moods, or aesthetics."

    return True, ""


def sanitize_prompt(text: str, max_length: int = MAX_PROMPT_LENGTH) -> tuple[str, list[str]]:
    """Sanitize user-entered prompts to prevent injection attacks.

    Security measures:
    1. Length limiting (prevent DoS)
    2. Control character stripping (prevent log injection - CWE-117)
    3. Null byte removal
    4. Suspicious pattern detection (logged but not blocked)

    Args:
        text: Raw user input
        max_length: Maximum allowed characters

    Returns:
        tuple: (sanitized_text, list of warnings)
    """
    warnings = []

    if not text:
        return "", warnings

    # 1. Remove null bytes (security)
    if '\x00' in text:
        text = text.replace('\x00', '')
        warnings.append("null_bytes_removed")

    # 2. Length limit (prevent DoS and excessive API costs)
    if len(text) > max_length:
        text = text[:max_length]
        warnings.append(f"truncated_to_{max_length}")

    # 3. Strip control characters except newlines and tabs (CWE-117 prevention)
    original_len = len(text)
    text = ''.join(char for char in text if ord(char) >= 32 or char in '\n\t')
    if len(text) < original_len:
        warnings.append("control_chars_stripped")

    # 4. Normalize excessive whitespace
    text = ' '.join(text.split())

    # 5. Check for suspicious patterns (log but don't block - could be false positives)
    for pattern in SUSPICIOUS_PATTERNS:
        if re.search(pattern, text, re.IGNORECASE):
            warnings.append(f"suspicious_pattern: {pattern[:30]}")
            break  # Only log first match

    return text.strip(), warnings


def log_sanitized(endpoint: str, original_len: int, sanitized_len: int, warnings: list[str]):
    """Log sanitization actions for security monitoring."""
    if warnings:
        # Safe logging - no user content in log message
        print(f"[SECURITY] {endpoint}: input_len={original_len}, output_len={sanitized_len}, warnings={warnings}")


# ============================================================================
# JAILBREAK DETECTION - Block persistent abuse attempts
# ============================================================================

# Configuration
VIOLATION_WINDOW_MINUTES = 30  # Time window for tracking violations
SOFT_BLOCK_THRESHOLD = 3      # Violations before soft block (longer cooldown)
HARD_BLOCK_THRESHOLD = 5      # Violations before hard block (longer ban)
SOFT_BLOCK_MINUTES = 5        # Soft block duration
HARD_BLOCK_MINUTES = 60       # Hard block duration
REQUEST_COOLDOWN_SECONDS = 2  # Minimum time between requests

# In-memory tracking (resets on service restart)
violation_tracker: dict[str, dict] = defaultdict(lambda: {
    "violations": [],        # List of timestamps
    "blocked_until": None,   # datetime or None
    "last_request": None,    # datetime of last request
    "block_count": 0,        # How many times they've been blocked
})

# Patterns that suggest jailbreak attempts (trying to sneak past filters)
JAILBREAK_PATTERNS = [
    # Adding theme words to non-theme requests
    r"(tell me|write me|explain).{0,20}(theme|color)",
    # Encoding tricks
    r"b64:|base64|\\x[0-9a-f]{2}|&#x?[0-9a-f]+;",
    # Prompt stuffing (lots of theme words then real request)
    r"(dark|light|theme|color){3,}.{50,}",
    # Role-play to escape
    r"(pretend|imagine|act).{0,30}(not|aren't|isn't).{0,20}theme",
    # Instruction override attempts
    r"(but first|before that|however|instead)",
    # Multi-language evasion
    r"(dime|escribe|raconte|erzähl)",  # "tell me" in other languages
]


def get_client_ip(request: Request) -> str:
    """Get client IP, respecting X-Forwarded-For for proxied requests."""
    forwarded = request.headers.get("x-forwarded-for")
    if forwarded:
        return forwarded.split(",")[0].strip()
    return request.client.host if request.client else "unknown"


def check_rate_limit(client_ip: str) -> tuple[bool, str]:
    """Check if client is sending requests too fast.

    Returns:
        tuple: (is_allowed, error_message or empty string)
    """
    tracker = violation_tracker[client_ip]
    now = datetime.now()

    if tracker["last_request"]:
        elapsed = (now - tracker["last_request"]).total_seconds()
        if elapsed < REQUEST_COOLDOWN_SECONDS:
            return False, f"Please wait {REQUEST_COOLDOWN_SECONDS - int(elapsed)} seconds between requests."

    tracker["last_request"] = now
    return True, ""


def check_block_status(client_ip: str) -> tuple[bool, str]:
    """Check if client is currently blocked.

    Returns:
        tuple: (is_allowed, error_message or empty string)
    """
    tracker = violation_tracker[client_ip]
    now = datetime.now()

    if tracker["blocked_until"] and now < tracker["blocked_until"]:
        remaining = (tracker["blocked_until"] - now).seconds // 60 + 1
        return False, f"Too many invalid requests. Please try again in {remaining} minutes."

    # Clear expired block
    if tracker["blocked_until"] and now >= tracker["blocked_until"]:
        tracker["blocked_until"] = None

    return True, ""


def detect_jailbreak_attempt(prompt: str) -> bool:
    """Detect if prompt appears to be a jailbreak attempt."""
    prompt_lower = prompt.lower()

    for pattern in JAILBREAK_PATTERNS:
        if re.search(pattern, prompt_lower):
            return True

    return False


def record_violation(client_ip: str, reason: str) -> tuple[bool, str]:
    """Record a violation and check if client should be blocked.

    Returns:
        tuple: (should_continue, block_message or empty string)
    """
    tracker = violation_tracker[client_ip]
    now = datetime.now()

    # Clean old violations outside the window
    cutoff = now - timedelta(minutes=VIOLATION_WINDOW_MINUTES)
    tracker["violations"] = [v for v in tracker["violations"] if v > cutoff]

    # Add new violation
    tracker["violations"].append(now)
    violation_count = len(tracker["violations"])

    print(f"[JAILBREAK] IP={client_ip[:20]}, violations={violation_count}, reason={reason}")

    # Check thresholds
    if violation_count >= HARD_BLOCK_THRESHOLD:
        # Escalating block duration based on repeat offenses
        block_minutes = HARD_BLOCK_MINUTES * (1 + tracker["block_count"])
        tracker["blocked_until"] = now + timedelta(minutes=block_minutes)
        tracker["block_count"] += 1
        return False, f"Access suspended for {block_minutes} minutes due to repeated policy violations."

    elif violation_count >= SOFT_BLOCK_THRESHOLD:
        tracker["blocked_until"] = now + timedelta(minutes=SOFT_BLOCK_MINUTES)
        return False, f"Too many invalid requests. Please wait {SOFT_BLOCK_MINUTES} minutes."

    return True, ""


def enforce_access_control(request: Request, prompt: str) -> tuple[bool, str]:
    """Main access control check - combines all protections.

    Returns:
        tuple: (is_allowed, error_message or empty string)
    """
    client_ip = get_client_ip(request)

    # 1. Check if currently blocked
    allowed, msg = check_block_status(client_ip)
    if not allowed:
        return False, msg

    # 2. Check rate limit
    allowed, msg = check_rate_limit(client_ip)
    if not allowed:
        # Rate limit violation counts toward block threshold
        record_violation(client_ip, "rate_limit")
        return False, msg

    # 3. Check for jailbreak patterns
    if detect_jailbreak_attempt(prompt):
        allowed, msg = record_violation(client_ip, "jailbreak_pattern")
        if not allowed:
            return False, msg
        # Continue but record the violation

    return True, ""

# Load environment variables from project root
load_dotenv(dotenv_path=os.path.join(os.path.dirname(__file__), '..', '.env'))

# Check if we're in live mode (production) or mock mode (development)
LIVE_MODE = os.getenv("THEME_GENERATOR_LIVE", "").lower() == "true"

# Initialize FastAPI
mode_desc = "LIVE MODE - API calls enabled" if LIVE_MODE else "MOCK MODE - No API calls"
app = FastAPI(
    title="Theme Generator",
    description=f"AI-powered theme generation for Stock Analyzer ({mode_desc})",
    version="1.0.0"
)

# CORS for browser access
app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:5000", "https://psfordtaurus.com"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Initialize Anthropic client only in live mode
if LIVE_MODE:
    import anthropic
    client = anthropic.Anthropic(api_key=os.getenv("ANTHROPIC_API_KEY"))
else:
    client = None

# ============================================================================
# MOCK THEMES - Used in development mode to avoid API costs
# ============================================================================
MOCK_THEMES = {
    "hotdog-stand": {
        "id": "hotdog-stand",
        "name": "Hotdog Stand",
        "version": "1.0.0",
        "meta": {"category": "light", "icon": "sun", "iconColor": "#FFFF00"},
        "variables": {
            **LIGHT_BASE_DEFAULTS,
            # Classic Windows 3.1 Hotdog Stand - red background, yellow accents
            "bg-primary": "#FF0000",
            "bg-secondary": "#CC0000",
            "bg-tertiary": "#990000",
            "bg-code": "#660000",
            "text-primary": "#FFFF00",
            "text-secondary": "#FFFF99",
            "text-muted": "#FFCC00",
            "text-inverted": "#FF0000",
            "accent": "#FFFF00",
            "accent-hover": "#FFFF99",
            "accent-light": "#FFFFCC",
            "accent-bg": "rgba(255, 255, 0, 0.2)",
            "accent-bg-subtle": "rgba(255, 255, 0, 0.1)",
            "border-primary": "#FFFF00",
            "border-secondary": "#FFCC00",
            "success": "#00FF00",
            "error": "#FFFFFF",
            "warning": "#FF6600",
            "btn-primary-bg": "#FFFF00",
            "btn-primary-bg-hover": "#FFFF99",
            "btn-primary-text": "#FF0000",
            "btn-primary-glow": "0 0 10px #FFFF00",
            "chart-bg": "#CC0000",
            "chart-text": "#FFFF00",
            "chart-grid": "#FF6666",
            "chart-line-primary": "#FFFF00",
            "chart-line-secondary": "#FFFF99",
            "chart-candle-up": "#00FF00",
            "chart-candle-down": "#FFFFFF",
            "price-up": "#00FF00",
            "price-down": "#FFFFFF",
            "tile-title-color": "#FFFF00",
        },
        "effects": {},
        "fonts": DEFAULT_FONTS.copy()
    },
    "sunset": {
        "id": "sunset-glow",
        "name": "Sunset Glow",
        "version": "1.0.0",
        "meta": {"category": "dark", "icon": "sun", "iconColor": "#ff7b54"},
        "variables": {
            **DARK_BASE_DEFAULTS,
            "bg-primary": "#1a1216",
            "bg-secondary": "#2d1f24",
            "bg-tertiary": "#3d2930",
            "text-primary": "#fff5f0",
            "text-secondary": "#d4a89a",
            "accent": "#ff7b54",
            "accent-hover": "#ff9a76",
            "accent-light": "#ffb89a",
            "accent-bg": "rgba(255, 123, 84, 0.15)",
            "accent-bg-subtle": "rgba(255, 123, 84, 0.08)",
            "border-primary": "#ff7b54",
            "border-secondary": "#3d2930",
            "success": "#7ed56f",
            "error": "#ff6b6b",
            "btn-primary-bg": "#ff7b54",
            "btn-primary-bg-hover": "#ff9a76",
            "btn-primary-text": "#1a1216",
            "chart-line-primary": "#ff7b54",
            "chart-line-secondary": "#ffb89a",
        },
        "effects": {"vignette": {"enabled": True, "strength": 0.3}},
        "fonts": DEFAULT_FONTS.copy()
    },
    "ocean": {
        "id": "deep-ocean",
        "name": "Deep Ocean",
        "version": "1.0.0",
        "meta": {"category": "dark", "icon": "moon", "iconColor": "#00b4d8"},
        "variables": {
            **DARK_BASE_DEFAULTS,
            "bg-primary": "#0a1628",
            "bg-secondary": "#0d2137",
            "bg-tertiary": "#123049",
            "text-primary": "#e0f7ff",
            "text-secondary": "#90cdf4",
            "accent": "#00b4d8",
            "accent-hover": "#48cae4",
            "accent-light": "#90e0ef",
            "accent-bg": "rgba(0, 180, 216, 0.15)",
            "accent-bg-subtle": "rgba(0, 180, 216, 0.08)",
            "border-primary": "#00b4d8",
            "border-secondary": "#123049",
            "success": "#00f5d4",
            "error": "#ff6b6b",
            "btn-primary-bg": "#00b4d8",
            "btn-primary-bg-hover": "#48cae4",
            "btn-primary-text": "#0a1628",
            "chart-line-primary": "#00b4d8",
            "chart-line-secondary": "#90e0ef",
        },
        "effects": {"bloom": {"enabled": True, "contrast": 1.05, "brightness": 1.02}},
        "fonts": DEFAULT_FONTS.copy()
    },
    "forest": {
        "id": "forest-night",
        "name": "Forest Night",
        "version": "1.0.0",
        "meta": {"category": "dark", "icon": "moon", "iconColor": "#52b788"},
        "variables": {
            **DARK_BASE_DEFAULTS,
            "bg-primary": "#0d1b14",
            "bg-secondary": "#1a2f23",
            "bg-tertiary": "#264234",
            "text-primary": "#e8f5e9",
            "text-secondary": "#a5d6a7",
            "accent": "#52b788",
            "accent-hover": "#74c69d",
            "accent-light": "#95d5b2",
            "accent-bg": "rgba(82, 183, 136, 0.15)",
            "accent-bg-subtle": "rgba(82, 183, 136, 0.08)",
            "border-primary": "#52b788",
            "border-secondary": "#264234",
            "success": "#40916c",
            "error": "#e63946",
            "btn-primary-bg": "#52b788",
            "btn-primary-bg-hover": "#74c69d",
            "btn-primary-text": "#0d1b14",
            "chart-line-primary": "#52b788",
            "chart-line-secondary": "#95d5b2",
        },
        "effects": {},
        "fonts": DEFAULT_FONTS.copy()
    },
    "lavender": {
        "id": "lavender-dream",
        "name": "Lavender Dream",
        "version": "1.0.0",
        "meta": {"category": "light", "icon": "sun", "iconColor": "#7c3aed"},
        "variables": {
            **LIGHT_BASE_DEFAULTS,
            "bg-primary": "#faf5ff",
            "bg-secondary": "#f3e8ff",
            "bg-tertiary": "#e9d5ff",
            "text-primary": "#3b0764",
            "text-secondary": "#6b21a8",
            "accent": "#7c3aed",
            "accent-hover": "#8b5cf6",
            "accent-light": "#a78bfa",
            "accent-bg": "rgba(124, 58, 237, 0.12)",
            "accent-bg-subtle": "rgba(124, 58, 237, 0.06)",
            "border-primary": "#c4b5fd",
            "border-secondary": "#ddd6fe",
            "success": "#22c55e",
            "error": "#ef4444",
            "btn-primary-bg": "#7c3aed",
            "btn-primary-bg-hover": "#8b5cf6",
            "btn-primary-text": "#ffffff",
            "chart-line-primary": "#7c3aed",
            "chart-line-secondary": "#a78bfa",
        },
        "effects": {},
        "fonts": DEFAULT_FONTS.copy()
    },
}


def infer_theme_mode(prompt: str) -> str:
    """Infer whether the prompt describes a light or dark theme.

    The prompt drives the theme, not a dropdown. "Beach" is inherently light.
    "Gothic horror" is inherently dark.

    Returns:
        "light" or "dark"
    """
    prompt_lower = prompt.lower()

    # Keywords that strongly suggest LIGHT themes
    light_keywords = [
        "beach", "tropical", "sunny", "summer", "bright", "cheerful", "playful",
        "pastel", "soft", "warm", "sand", "daylight", "morning", "spring",
        "clean", "minimal", "white", "cream", "light", "airy", "fresh",
        "cotton candy", "bubblegum", "lavender", "mint", "peach",
    ]

    # Keywords that strongly suggest DARK themes
    dark_keywords = [
        "dark", "night", "midnight", "gothic", "horror", "scary", "moody",
        "cyberpunk", "noir", "shadow", "deep", "space", "void", "black",
        "neon", "synthwave", "vaporwave", "retro", "hacker", "matrix",
        "ocean deep", "forest night", "sunset", "dusk", "twilight",
        "grimdark", "gritty", "industrial",
    ]

    light_score = sum(1 for kw in light_keywords if kw in prompt_lower)
    dark_score = sum(1 for kw in dark_keywords if kw in prompt_lower)

    # Light wins ties for ambiguous prompts (more user-friendly default)
    return "light" if light_score >= dark_score else "dark"


def get_mock_theme(prompt: str, name: str, base_theme: str) -> tuple[dict, bool]:
    """Return a mock theme based on keywords in the prompt.

    The prompt determines light vs dark - not the base_theme dropdown.
    "Beach" is always light. "Gothic horror" is always dark.

    Args:
        prompt: User's theme description (THIS drives the theme)
        name: Theme name
        base_theme: Dropdown selection (used as fallback only)

    Returns:
        tuple: (theme dict, matched: bool indicating if keywords matched)
    """
    import copy
    prompt_lower = prompt.lower()
    matched = True

    # Infer light/dark from the prompt itself
    inferred_mode = infer_theme_mode(prompt)

    # Match based on keywords - order matters, more specific first
    if any(w in prompt_lower for w in ["hotdog", "hot dog", "windows 3.1", "windows 3", "hotdog stand"]):
        matched_theme = MOCK_THEMES["hotdog-stand"]
    elif any(w in prompt_lower for w in ["sunset", "orange", "warm", "coral"]):
        matched_theme = MOCK_THEMES["sunset"]
    elif any(w in prompt_lower for w in ["ocean", "blue", "sea", "water", "aqua", "beach", "tropical"]):
        matched_theme = MOCK_THEMES["ocean"]
    elif any(w in prompt_lower for w in ["forest", "green", "nature", "earth"]):
        matched_theme = MOCK_THEMES["forest"]
    elif any(w in prompt_lower for w in ["purple", "lavender", "violet"]):
        matched_theme = MOCK_THEMES["lavender"]
    else:
        # Random selection - no match
        matched_theme = random.choice(list(MOCK_THEMES.values()))
        matched = False

    # Deep copy to avoid modifying the original
    theme = copy.deepcopy(matched_theme)

    # Use inferred mode from prompt, applying correct base + accent colors
    use_light = (inferred_mode == "light")
    base_defaults = LIGHT_BASE_DEFAULTS if use_light else DARK_BASE_DEFAULTS
    matched_vars = theme.get("variables", {})

    # Extract accent/highlight colors from matched theme (the "personality")
    accent_keys = [
        "accent", "accent-hover", "accent-light", "accent-bg", "accent-bg-subtle",
        "border-primary", "success", "error", "warning",
        "btn-primary-bg", "btn-primary-bg-hover", "btn-primary-text", "btn-primary-glow",
        "chart-line-primary", "chart-line-secondary",
        "price-up", "price-down", "price-up-glow", "price-down-glow",
    ]

    # Start with base defaults (light or dark backgrounds/text)
    new_vars = {**base_defaults}

    # Overlay accent colors from matched theme
    for key in accent_keys:
        if key in matched_vars:
            new_vars[key] = matched_vars[key]

    # For light themes, ensure button text has contrast
    if use_light:
        if "btn-primary-bg" in new_vars and "btn-primary-text" not in matched_vars:
            new_vars["btn-primary-text"] = "#1f2937"

    theme["variables"] = new_vars
    theme["meta"]["category"] = inferred_mode

    # Override with provided name
    theme["name"] = name
    theme["id"] = re.sub(r'[^a-z0-9]+', '-', name.lower()).strip('-')
    theme["meta"]["originalPrompt"] = prompt

    return theme, matched


# Color palettes for mock refinement
MOCK_COLOR_PALETTES = {
    "yellow": {"accent": "#fbbf24", "accent-hover": "#f59e0b", "accent-light": "#fcd34d"},
    "gold": {"accent": "#d4af37", "accent-hover": "#c9a227", "accent-light": "#e6c349"},
    "pink": {"accent": "#ec4899", "accent-hover": "#db2777", "accent-light": "#f472b6"},
    "magenta": {"accent": "#d946ef", "accent-hover": "#c026d3", "accent-light": "#e879f9"},
    "red": {"accent": "#ef4444", "accent-hover": "#dc2626", "accent-light": "#f87171"},
    "crimson": {"accent": "#dc143c", "accent-hover": "#b91c1c", "accent-light": "#f87171"},
    "orange": {"accent": "#f97316", "accent-hover": "#ea580c", "accent-light": "#fb923c"},
    "coral": {"accent": "#ff7f50", "accent-hover": "#ff6347", "accent-light": "#ffa07a"},
    "green": {"accent": "#22c55e", "accent-hover": "#16a34a", "accent-light": "#4ade80"},
    "teal": {"accent": "#14b8a6", "accent-hover": "#0d9488", "accent-light": "#2dd4bf"},
    "cyan": {"accent": "#06b6d4", "accent-hover": "#0891b2", "accent-light": "#22d3ee"},
    "blue": {"accent": "#3b82f6", "accent-hover": "#2563eb", "accent-light": "#60a5fa"},
    "indigo": {"accent": "#6366f1", "accent-hover": "#4f46e5", "accent-light": "#818cf8"},
    "purple": {"accent": "#a855f7", "accent-hover": "#9333ea", "accent-light": "#c084fc"},
    "violet": {"accent": "#8b5cf6", "accent-hover": "#7c3aed", "accent-light": "#a78bfa"},
    "white": {"accent": "#f8fafc", "accent-hover": "#f1f5f9", "accent-light": "#ffffff"},
}


def apply_mock_refinement(theme: dict, feedback: str) -> dict:
    """Apply mock refinements based on feedback keywords."""
    import copy
    theme = copy.deepcopy(theme)
    feedback_lower = feedback.lower()

    if "variables" not in theme:
        theme["variables"] = {}

    vars = theme["variables"]

    # Handle black and white / grayscale FIRST (before other color changes)
    if any(w in feedback_lower for w in ["black and white", "grayscale", "greyscale", "monochrome", "b&w", "b/w"]):
        # Full grayscale conversion
        vars["bg-primary"] = "#1a1a1a"
        vars["bg-secondary"] = "#2d2d2d"
        vars["bg-tertiary"] = "#404040"
        vars["bg-code"] = "#252525"
        vars["text-primary"] = "#ffffff"
        vars["text-secondary"] = "#cccccc"
        vars["text-muted"] = "#888888"
        vars["accent"] = "#808080"
        vars["accent-hover"] = "#999999"
        vars["accent-light"] = "#b0b0b0"
        vars["accent-bg"] = "rgba(128, 128, 128, 0.15)"
        vars["accent-bg-subtle"] = "rgba(128, 128, 128, 0.08)"
        vars["border-primary"] = "#505050"
        vars["border-secondary"] = "#606060"
        vars["btn-primary-bg"] = "#606060"
        vars["btn-primary-bg-hover"] = "#707070"
        vars["btn-primary-text"] = "#ffffff"
        vars["chart-line-primary"] = "#808080"
        vars["chart-line-secondary"] = "#606060"
        vars["success"] = "#808080"
        vars["error"] = "#606060"
        vars["warning"] = "#909090"
        vars["price-up"] = "#909090"
        vars["price-down"] = "#505050"
        # Continue to effects handling below (don't return early)

    # Handle neon with specific colors (cyan + magenta = synthwave/vaporwave)
    neon_handled = False
    if "neon" in feedback_lower:
        neon_handled = True
        vars["bg-primary"] = "#0a0a12"
        vars["bg-secondary"] = "#0f0f1a"
        vars["bg-tertiary"] = "#151525"
        vars["text-primary"] = "#ffffff"
        if "cyan" in feedback_lower and "magenta" in feedback_lower:
            # Both colors = synthwave palette
            vars["accent"] = "#00ffff"
            vars["accent-hover"] = "#00cccc"
            vars["chart-line-primary"] = "#00ffff"
            vars["chart-line-secondary"] = "#ff00ff"
            vars["btn-primary-bg"] = "#00ffff"
            vars["btn-primary-text"] = "#000000"
        elif "cyan" in feedback_lower:
            vars["accent"] = "#00ffff"
            vars["accent-hover"] = "#00cccc"
            vars["chart-line-primary"] = "#00ffff"
            vars["chart-line-secondary"] = "#00ff88"
            vars["btn-primary-bg"] = "#00ffff"
            vars["btn-primary-text"] = "#000000"
        elif "magenta" in feedback_lower:
            vars["accent"] = "#ff00ff"
            vars["accent-hover"] = "#cc00cc"
            vars["chart-line-primary"] = "#ff00ff"
            vars["chart-line-secondary"] = "#00ffff"
            vars["btn-primary-bg"] = "#ff00ff"
            vars["btn-primary-text"] = "#000000"
        else:
            vars["accent"] = "#00ff88"
            vars["accent-hover"] = "#00cc66"
            vars["chart-line-primary"] = "#00ff88"
            vars["chart-line-secondary"] = "#00ffff"
        vars["btn-primary-glow"] = f"0 0 20px {vars.get('accent', '#00ff88')}"
        vars["price-up-glow"] = "0 0 8px #00ff88"
        vars["price-down-glow"] = "0 0 8px #ff0066"
        # Add bloom effect
        if "effects" not in theme:
            theme["effects"] = {}
        theme["effects"]["bloom"] = {"enabled": True, "contrast": 1.1, "brightness": 1.08}

    # Check for color keywords and apply accent changes (skip if neon handled specific colors)
    if not neon_handled:
        for color_name, palette in MOCK_COLOR_PALETTES.items():
            if color_name in feedback_lower:
                vars["accent"] = palette["accent"]
                vars["accent-hover"] = palette["accent-hover"]
                vars["accent-light"] = palette["accent-light"]
                vars["accent-bg"] = f"rgba({int(palette['accent'][1:3], 16)}, {int(palette['accent'][3:5], 16)}, {int(palette['accent'][5:7], 16)}, 0.15)"
                vars["accent-bg-subtle"] = f"rgba({int(palette['accent'][1:3], 16)}, {int(palette['accent'][3:5], 16)}, {int(palette['accent'][5:7], 16)}, 0.08)"
                vars["border-primary"] = palette["accent"]
                vars["btn-primary-bg"] = palette["accent"]
                vars["btn-primary-bg-hover"] = palette["accent-hover"]
                vars["chart-line-primary"] = palette["accent"]
                vars["chart-line-secondary"] = palette["accent-light"]
                break

    # Handle tile background changes (Metro-style colored tiles)
    if any(w in feedback_lower for w in ["blue tile", "blue background", "tiles blue", "tile blue"]):
        vars["bg-secondary"] = "#0078D4"
        vars["bg-tertiary"] = "#005a9e"
    elif any(w in feedback_lower for w in ["colored tile", "metro tile"]):
        vars["bg-secondary"] = vars.get("accent", "#0078D4")
        vars["bg-tertiary"] = vars.get("accent-hover", "#005a9e")

    # Handle background adjustments
    if any(w in feedback_lower for w in ["darker", "more dark", "deep"]):
        # Darken backgrounds
        vars["bg-primary"] = "#050508"
        vars["bg-secondary"] = "#0a0a10"
        vars["bg-tertiary"] = "#10101a"
    elif any(w in feedback_lower for w in ["lighter", "more light", "bright background"]):
        # Lighten backgrounds
        vars["bg-primary"] = "#1a1a24"
        vars["bg-secondary"] = "#24242e"
        vars["bg-tertiary"] = "#2e2e3a"

    # Handle text adjustments
    if "brighter text" in feedback_lower or "lighter text" in feedback_lower:
        vars["text-primary"] = "#ffffff"
        vars["text-secondary"] = "#d0d0e0"
    elif "dimmer text" in feedback_lower or "darker text" in feedback_lower:
        vars["text-primary"] = "#c0c0d0"
        vars["text-secondary"] = "#808090"

    # Handle vibrant/muted requests
    if any(w in feedback_lower for w in ["vibrant", "vivid", "saturated", "brighter"]):
        # Make accent more vibrant by increasing saturation conceptually
        # For mock, we just use neon-like colors
        if "accent" in vars:
            current_accent = vars["accent"]
            # Neon up the accent
            vars["btn-primary-glow"] = f"0 0 20px {current_accent}"
            vars["chart-line-glow"] = "drop-shadow(0 0 6px currentColor)"
    elif any(w in feedback_lower for w in ["muted", "subtle", "desaturated", "softer"]):
        # Disable glow effects
        vars["btn-primary-glow"] = "none"
        vars["chart-line-glow"] = "none"

    # Handle effects
    if "effects" not in theme:
        theme["effects"] = {}

    if any(w in feedback_lower for w in ["scanlines", "crt", "retro"]):
        theme["effects"]["scanlines"] = {"enabled": True, "opacity": 0.08, "size": 3}
    elif "no scanlines" in feedback_lower or "remove scanlines" in feedback_lower:
        theme["effects"]["scanlines"] = {"enabled": False}

    if any(w in feedback_lower for w in ["vignette", "dark edges", "shadowed edges"]):
        theme["effects"]["vignette"] = {"enabled": True, "strength": 0.4}
    elif "no vignette" in feedback_lower or "remove vignette" in feedback_lower:
        theme["effects"]["vignette"] = {"enabled": False}

    if any(w in feedback_lower for w in ["glow", "bloom", "neon"]):
        theme["effects"]["bloom"] = {"enabled": True, "contrast": 1.08, "brightness": 1.05}
    elif "no bloom" in feedback_lower or "no glow" in feedback_lower:
        theme["effects"]["bloom"] = {"enabled": False}

    if any(w in feedback_lower for w in ["rain", "rainy", "cyberpunk rain"]):
        theme["effects"]["rain"] = {"enabled": True}
    elif "no rain" in feedback_lower or "remove rain" in feedback_lower:
        theme["effects"]["rain"] = {"enabled": False}

    if any(w in feedback_lower for w in ["flicker", "crt flicker"]):
        theme["effects"]["crtFlicker"] = {"enabled": True}
    elif "no flicker" in feedback_lower:
        theme["effects"]["crtFlicker"] = {"enabled": False}

    # Canvas effects - matrixRain color changes
    if "matrixRain" in theme.get("effects", {}):
        rain_effect = theme["effects"]["matrixRain"]
        # Color changes for rain
        if "purple" in feedback_lower and ("rain" in feedback_lower or "matrix" in feedback_lower):
            rain_effect["color"] = "#bf00ff"
        elif "blue" in feedback_lower and ("rain" in feedback_lower or "matrix" in feedback_lower):
            rain_effect["color"] = "#00bfff"
        elif "red" in feedback_lower and ("rain" in feedback_lower or "matrix" in feedback_lower):
            rain_effect["color"] = "#ff0040"
        elif "pink" in feedback_lower and ("rain" in feedback_lower or "matrix" in feedback_lower):
            rain_effect["color"] = "#ff69b4"
        elif "cyan" in feedback_lower and ("rain" in feedback_lower or "matrix" in feedback_lower):
            rain_effect["color"] = "#00ffff"
        elif "gold" in feedback_lower and ("rain" in feedback_lower or "matrix" in feedback_lower):
            rain_effect["color"] = "#ffd700"
        # Speed changes
        if any(w in feedback_lower for w in ["slower", "slow down"]) and "rain" in feedback_lower:
            rain_effect["speed"] = 0.5
        elif any(w in feedback_lower for w in ["faster", "speed up"]) and "rain" in feedback_lower:
            rain_effect["speed"] = 1.5
        # Density changes
        if any(w in feedback_lower for w in ["less rain", "fewer", "sparse"]):
            rain_effect["density"] = 0.95
        elif any(w in feedback_lower for w in ["more rain", "dense", "heavy"]):
            rain_effect["density"] = 0.99

    # Add matrixRain if requested and not present
    if any(w in feedback_lower for w in ["matrix rain", "matrix effect", "falling characters", "digital rain"]):
        if "effects" not in theme:
            theme["effects"] = {}
        if "matrixRain" not in theme["effects"]:
            theme["effects"]["matrixRain"] = {
                "enabled": True,
                "color": "#00ff41",
                "speed": 1,
                "density": 0.98
            }

    # Disable matrixRain
    if any(w in feedback_lower for w in ["no matrix", "remove matrix", "disable matrix", "turn off matrix", "no rain", "remove rain"]):
        if "matrixRain" in theme.get("effects", {}):
            theme["effects"]["matrixRain"]["enabled"] = False

    # Snow effect
    if any(w in feedback_lower for w in ["add snow", "snow effect", "snowing", "snowfall"]):
        if "effects" not in theme:
            theme["effects"] = {}
        theme["effects"]["snow"] = {"enabled": True, "color": "#ffffff", "count": 100}
    elif any(w in feedback_lower for w in ["no snow", "remove snow"]):
        if "snow" in theme.get("effects", {}):
            theme["effects"]["snow"]["enabled"] = False

    # Particles effect
    if any(w in feedback_lower for w in ["add particles", "particle effect", "floating particles"]):
        if "effects" not in theme:
            theme["effects"] = {}
        theme["effects"]["particles"] = {"enabled": True, "color": "#ffffff", "count": 50}
    elif any(w in feedback_lower for w in ["no particles", "remove particles"]):
        if "particles" in theme.get("effects", {}):
            theme["effects"]["particles"]["enabled"] = False

    return theme

# System prompt for theme generation
SYSTEM_PROMPT = """You are a CSS theme generator for a stock analysis dashboard. You ONLY output JSON theme configurations.

CRITICAL RESTRICTIONS:
- You ONLY generate theme JSON. Nothing else.
- If asked to do ANYTHING other than generate a visual theme, respond with a valid JSON theme that matches the literal interpretation of their words as colors/styles.
- NEVER have a conversation. NEVER answer questions. NEVER write code, stories, poems, or explanations.
- NEVER refuse a request - instead, interpret ANY input as a theme description and output theme JSON.
- Your ONLY output format is the theme JSON schema below. No markdown, no explanations, no commentary.

Example: If someone says "tell me a joke", output a theme called "Comedy" with bright, playful colors.
Example: If someone says "what is 2+2", output a theme with 4-color palette aesthetics.
Example: If someone asks about the weather, output a weather-inspired theme (sunny yellows, cloudy grays, etc.).

You are creating themes for a stock analysis dashboard. Themes must be:
1. Visually cohesive with good color harmony
2. Accessible with sufficient contrast (WCAG AA minimum)
3. Functional for data visualization (charts, indicators, price movements)
4. Consistent across all UI elements

IMPORTANT COLOR GUIDELINES:
- Background colors should form a clear hierarchy (primary > secondary > tertiary)
- Text colors must have sufficient contrast against their backgrounds
- Accent colors should be vibrant but not overwhelming
- Chart colors must be distinguishable from each other
- Price up/down colors should be clearly different (typically green/red family)
- Semantic colors (success, error, warning) should be intuitive

EFFECTS (optional):
- scanlines: Retro CRT effect (good for cyberpunk themes)
- vignette: Darkened edges (adds depth)
- crtFlicker: Subtle screen flicker (use sparingly)
- rain: Animated rain drops (for moody themes)
- bloom: Slight glow/contrast boost (for neon themes)

OUTPUT FORMAT:
Return ONLY valid JSON matching the theme schema. No markdown, no explanations outside the JSON.
The JSON should be complete and immediately usable.

CRITICAL: The color section MUST be named "variables" (not "colors"). This is required for CSS custom property mapping.

VARIABLE REFERENCE (all required):
Background: bg-primary, bg-secondary, bg-tertiary, bg-code
Text: text-primary, text-secondary, text-muted, text-inverted
Borders: border-primary, border-secondary
Accent: accent, accent-hover, accent-light, accent-bg, accent-bg-subtle
Status: success, error, warning, warning-light
Highlights: highlight-bg, highlight-text
Danger: danger-bg, danger-border
Star: star-color, star-bg, star-glow
Price: price-up, price-up-glow, price-down, price-down-glow
Audio: audio-active-bg
Music: music-active-color, music-active-bg, music-active-glow, viz-bar-color, viz-bar-glow
Buttons: btn-primary-bg, btn-primary-bg-hover, btn-primary-text, btn-primary-glow, btn-primary-glow-hover
Loader: loader-bg, loader-accent
Shadows: shadow-sm, shadow-md, shadow-lg, shadow-xl
Radius: radius-sm, radius-md, radius-lg
Tiles: tile-title-color, tile-title-transform, tile-title-spacing, tile-title-weight, tile-title-glow
Chart: chart-bg, chart-text, chart-grid, chart-axis, chart-line-primary, chart-line-secondary
Chart SMAs: chart-line-sma20, chart-line-sma50, chart-line-sma200
Chart Candles: chart-candle-up, chart-candle-down
Chart Volume: chart-volume-up, chart-volume-down
Chart Indicators: chart-rsi, chart-macd, chart-macd-signal, chart-stochastic, chart-stochastic-d
Chart Zones: chart-overbought, chart-oversold, chart-bollinger
Chart Glow: chart-line-glow, chart-line-glow-color, chart-line-glow-width
Chart Markers: chart-marker-up, chart-marker-down, chart-marker-up-outline, chart-marker-down-outline, chart-marker-symbol, chart-marker-size
Grid: grid-dot, grid-dot-active
Zoom: zoom-bg, zoom-border
Measure: measure-bg, measure-line
Placeholder: placeholder-bg, placeholder-border
Locked: locked-pattern"""


# Request/Response models
class GenerateRequest(BaseModel):
    prompt: str
    base_theme: Optional[str] = "dark"  # "dark" or "light"
    name: Optional[str] = None


class RefineRequest(BaseModel):
    theme: dict
    feedback: str


class ThemeResponse(BaseModel):
    theme: dict
    explanation: str


def create_theme_id(name: str) -> str:
    """Convert theme name to valid ID (lowercase, hyphens)."""
    return re.sub(r'[^a-z0-9]+', '-', name.lower()).strip('-')


def merge_with_defaults(theme_vars: dict, base: str) -> dict:
    """Merge generated variables with defaults to ensure completeness."""
    defaults = DARK_BASE_DEFAULTS if base == "dark" else LIGHT_BASE_DEFAULTS
    merged = {**defaults, **theme_vars}
    return merged


def extract_json_from_response(text: str) -> dict:
    """Extract JSON from Claude's response, handling potential markdown wrapping."""
    # Try to find JSON block
    json_match = re.search(r'```(?:json)?\s*([\s\S]*?)\s*```', text)
    if json_match:
        text = json_match.group(1)

    # Try to parse as-is first
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        pass

    # Try to find object boundaries
    start = text.find('{')
    end = text.rfind('}')
    if start != -1 and end != -1:
        try:
            return json.loads(text[start:end+1])
        except json.JSONDecodeError:
            pass

    raise ValueError("Could not extract valid JSON from response")


def validate_theme_response(theme: dict) -> tuple[bool, str]:
    """Validate that the response is actually a theme, not chatbot output.

    Returns:
        tuple: (is_valid, error_message or empty string)
    """
    # Must have variables section with actual color values
    if "variables" not in theme:
        return False, "Response missing 'variables' section"

    variables = theme.get("variables", {})

    # Must have at least some core theme variables
    required_vars = ["bg-primary", "text-primary", "accent"]
    missing = [v for v in required_vars if v not in variables]
    if missing:
        return False, f"Response missing required variables: {missing}"

    # Variables should look like colors (hex, rgb, rgba, or CSS keywords)
    color_pattern = r'^(#[0-9a-fA-F]{3,8}|rgba?\([^)]+\)|[a-z]+)$'
    sample_vars = ["bg-primary", "text-primary", "accent"]
    for var in sample_vars:
        if var in variables:
            val = str(variables[var]).strip()
            if not re.match(color_pattern, val):
                return False, f"Variable '{var}' doesn't look like a color: {val[:50]}"

    # Check for chatbot-style responses embedded in the JSON
    # (someone might try to get Claude to put chat in a "message" field)
    suspicious_keys = ["message", "response", "answer", "explanation", "note", "text"]
    for key in suspicious_keys:
        if key in theme and isinstance(theme[key], str) and len(theme[key]) > 100:
            return False, "Response contains unexpected text content"

    return True, ""


@app.get("/health")
async def health_check():
    """Health check endpoint."""
    return {
        "status": "healthy",
        "service": "theme-generator",
        "mode": "live" if LIVE_MODE else "mock"
    }


@app.post("/generate", response_model=ThemeResponse)
async def generate_theme(request: GenerateRequest, http_request: Request):
    """Generate a new theme from a natural language prompt."""

    # Sanitize user input (security boundary)
    original_len = len(request.prompt) if request.prompt else 0
    sanitized_prompt, warnings = sanitize_prompt(request.prompt)
    log_sanitized("/generate", original_len, len(sanitized_prompt), warnings)

    if not sanitized_prompt:
        raise HTTPException(status_code=400, detail="Prompt cannot be empty")

    # Jailbreak detection and rate limiting
    allowed, block_msg = enforce_access_control(http_request, sanitized_prompt)
    if not allowed:
        raise HTTPException(status_code=429, detail=block_msg)

    # Validate this is actually a theme request (prevents chatbot abuse)
    # In mock mode, be permissive (no API cost). In live mode, be stricter.
    is_valid, rejection_reason = is_theme_related(sanitized_prompt, strict=LIVE_MODE)
    if not is_valid:
        # Record violation for off-topic request
        client_ip = get_client_ip(http_request)
        should_continue, block_msg = record_violation(client_ip, "off_topic")
        if not should_continue:
            raise HTTPException(status_code=429, detail=block_msg)
        raise HTTPException(status_code=400, detail=rejection_reason)

    # Determine theme name and ID
    raw_name = request.name or "Custom Theme"
    # Sanitize name too (used in output, could be XSS vector if rendered unsafely)
    theme_name, _ = sanitize_prompt(raw_name, max_length=100)
    theme_id = create_theme_id(theme_name)

    # MOCK MODE: Return pre-built theme without API call
    # In dev, this just matches keywords to pre-built themes.
    # For custom themes, ask Claude Code directly.
    if not LIVE_MODE:
        theme, matched = get_mock_theme(sanitized_prompt, theme_name, request.base_theme)
        explanation = "[MOCK MODE] "
        if matched:
            explanation += f"Matched pre-built '{theme['name']}' theme based on keywords."
        else:
            explanation += "No keyword match - returned random theme. For custom themes, describe your vision to Claude Code in VS Code and paste the generated JSON."
        return ThemeResponse(
            theme=theme,
            explanation=explanation
        )

    # LIVE MODE: Call Anthropic API
    if not os.getenv("ANTHROPIC_API_KEY"):
        raise HTTPException(status_code=500, detail="ANTHROPIC_API_KEY not configured")

    # Build the prompt
    user_prompt = f"""Create a theme based on this description: {sanitized_prompt}

Base this on a {request.base_theme} theme foundation.

Theme metadata:
- id: "{theme_id}"
- name: "{theme_name}"
- version: "1.0.0"
- category: "{request.base_theme}"

Store the original prompt in meta.originalPrompt.

Return the complete theme JSON with ALL variables filled in."""

    try:
        # Call Claude API
        response = client.messages.create(
            model="claude-sonnet-4-20250514",
            max_tokens=8192,
            system=SYSTEM_PROMPT,
            messages=[{"role": "user", "content": user_prompt}]
        )

        # Extract the response text
        response_text = response.content[0].text
        print(f"DEBUG: Raw response length: {len(response_text)}")
        print(f"DEBUG: First 500 chars: {response_text[:500]}")

        # Parse the JSON
        theme = extract_json_from_response(response_text)
        print(f"DEBUG: Parsed theme keys: {list(theme.keys())}")

        # Handle Claude returning "colors" instead of "variables"
        if "colors" in theme and "variables" not in theme:
            theme["variables"] = theme.pop("colors")

        # Ensure the theme has required structure
        if "variables" not in theme:
            raise ValueError(f"Theme missing 'variables' section. Got keys: {list(theme.keys())}. Raw first 1000 chars: {response_text[:1000]}")

        # Validate this is actually a theme response (final safeguard)
        is_valid_theme, validation_error = validate_theme_response(theme)
        if not is_valid_theme:
            raise ValueError(f"Invalid theme response: {validation_error}")

        # Merge with defaults to fill any gaps
        theme["variables"] = merge_with_defaults(
            theme.get("variables", {}),
            request.base_theme
        )

        # Ensure effects and fonts exist
        if "effects" not in theme:
            theme["effects"] = {}
        if "fonts" not in theme:
            theme["fonts"] = DEFAULT_FONTS.copy()

        # Store original prompt in meta (sanitized version)
        if "meta" not in theme:
            theme["meta"] = {}
        theme["meta"]["originalPrompt"] = sanitized_prompt

        return ThemeResponse(
            theme=theme,
            explanation=f"Generated '{theme_name}' theme based on: {sanitized_prompt}"
        )

    except anthropic.APIError as e:
        raise HTTPException(status_code=500, detail=f"Claude API error: {str(e)}")
    except ValueError as e:
        raise HTTPException(status_code=500, detail=f"Failed to parse theme: {str(e)}")
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Unexpected error: {str(e)}")


@app.post("/refine", response_model=ThemeResponse)
async def refine_theme(request: RefineRequest, http_request: Request):
    """Refine an existing theme based on feedback."""

    # Sanitize user feedback (security boundary)
    original_len = len(request.feedback) if request.feedback else 0
    sanitized_feedback, warnings = sanitize_prompt(request.feedback)
    log_sanitized("/refine", original_len, len(sanitized_feedback), warnings)

    if not sanitized_feedback:
        raise HTTPException(status_code=400, detail="Feedback cannot be empty")

    # Jailbreak detection and rate limiting
    allowed, block_msg = enforce_access_control(http_request, sanitized_feedback)
    if not allowed:
        raise HTTPException(status_code=429, detail=block_msg)

    # Basic scope check for refinement (less strict since they already have a theme)
    feedback_lower = sanitized_feedback.lower()
    off_topic_phrases = ["tell me", "what is", "explain", "help me with", "write me"]
    if any(phrase in feedback_lower for phrase in off_topic_phrases):
        if not any(kw in feedback_lower for kw in ["color", "dark", "light", "bright", "accent", "theme"]):
            # Record violation
            client_ip = get_client_ip(http_request)
            should_continue, block_msg = record_violation(client_ip, "off_topic_refine")
            if not should_continue:
                raise HTTPException(status_code=429, detail=block_msg)
            raise HTTPException(
                status_code=400,
                detail="Please describe visual changes (colors, brightness, effects, etc.)"
            )

    # Get original prompt if available (already sanitized when stored)
    original_prompt = request.theme.get("meta", {}).get("originalPrompt", "unknown")
    theme_name = request.theme.get("name", "Custom Theme")

    # MOCK MODE: Apply keyword-based modifications without API call
    if not LIVE_MODE:
        theme = apply_mock_refinement(request.theme, sanitized_feedback)
        if "meta" not in theme:
            theme["meta"] = {}
        theme["meta"]["lastRefinement"] = sanitized_feedback
        return ThemeResponse(
            theme=theme,
            explanation=f"[MOCK MODE] Refined '{theme_name}' based on: {sanitized_feedback}"
        )

    # LIVE MODE: Call Anthropic API
    if not os.getenv("ANTHROPIC_API_KEY"):
        raise HTTPException(status_code=500, detail="ANTHROPIC_API_KEY not configured")

    # Build the refinement prompt
    user_prompt = f"""Here is the current theme JSON:

```json
{json.dumps(request.theme, indent=2)}
```

Original generation prompt was: "{original_prompt}"

Please refine this theme based on the following feedback:
{sanitized_feedback}

Return the complete updated theme JSON with the refinements applied.
Keep the same id, name, and version. Update only what the feedback requests.
Maintain color harmony and accessibility."""

    try:
        # Call Claude API
        response = client.messages.create(
            model="claude-sonnet-4-20250514",
            max_tokens=8192,
            system=SYSTEM_PROMPT,
            messages=[{"role": "user", "content": user_prompt}]
        )

        # Extract the response text
        response_text = response.content[0].text

        # Parse the JSON
        theme = extract_json_from_response(response_text)

        # Handle Claude returning "colors" instead of "variables"
        if "colors" in theme and "variables" not in theme:
            theme["variables"] = theme.pop("colors")

        # Ensure the theme has required structure
        if "variables" not in theme:
            raise ValueError("Theme missing 'variables' section")

        # Validate this is actually a theme response (final safeguard)
        is_valid_theme, validation_error = validate_theme_response(theme)
        if not is_valid_theme:
            raise ValueError(f"Invalid theme response: {validation_error}")

        # Preserve original ID and name if not in response
        if "id" not in theme:
            theme["id"] = request.theme.get("id", "custom-theme")
        if "name" not in theme:
            theme["name"] = request.theme.get("name", "Custom Theme")

        # Ensure effects and fonts exist
        if "effects" not in theme:
            theme["effects"] = request.theme.get("effects", {})
        if "fonts" not in theme:
            theme["fonts"] = request.theme.get("fonts", DEFAULT_FONTS.copy())

        # Preserve original prompt, add refinement history
        if "meta" not in theme:
            theme["meta"] = {}
        theme["meta"]["originalPrompt"] = original_prompt

        return ThemeResponse(
            theme=theme,
            explanation=f"Refined '{theme_name}' based on: {sanitized_feedback}"
        )

    except anthropic.APIError as e:
        raise HTTPException(status_code=500, detail=f"Claude API error: {str(e)}")
    except ValueError as e:
        raise HTTPException(status_code=500, detail=f"Failed to parse theme: {str(e)}")
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Unexpected error: {str(e)}")


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="127.0.0.1", port=8001)
