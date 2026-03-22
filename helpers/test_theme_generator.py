"""Test script for Theme Generator Service"""
import requests
import json
import sys

BASE_URL = "http://localhost:8001"

def test_health():
    """Test health endpoint."""
    print("Testing /health...")
    resp = requests.get(f"{BASE_URL}/health", timeout=30)
    print(f"  Status: {resp.status_code}")
    print(f"  Response: {resp.json()}")
    return resp.status_code == 200

def test_generate():
    """Test theme generation."""
    print("\nTesting /generate...")
    payload = {
        "prompt": "warm sunset theme with orange and coral tones, elegant and professional",
        "name": "Sunset Glow",
        "base_theme": "dark"
    }
    print(f"  Prompt: {payload['prompt']}")

    resp = requests.post(f"{BASE_URL}/generate", json=payload, timeout=120)
    print(f"  Status: {resp.status_code}")

    if resp.status_code == 200:
        data = resp.json()
        theme = data["theme"]
        print(f"  Theme ID: {theme.get('id')}")
        print(f"  Theme Name: {theme.get('name')}")
        print(f"  Variables count: {len(theme.get('variables', {}))}")
        print(f"  Effects: {list(theme.get('effects', {}).keys())}")
        print(f"\n  Sample colors:")
        vars = theme.get("variables", {})
        print(f"    bg-primary: {vars.get('bg-primary')}")
        print(f"    accent: {vars.get('accent')}")
        print(f"    text-primary: {vars.get('text-primary')}")
        print(f"    chart-line-primary: {vars.get('chart-line-primary')}")

        # Save full theme for inspection
        with open("generated_theme.json", "w") as f:
            json.dump(theme, f, indent=2)
        print(f"\n  Full theme saved to generated_theme.json")
        return theme
    else:
        print(f"  Error: {resp.text}")
        return None

def test_refine(theme):
    """Test theme refinement."""
    if not theme:
        print("\nSkipping /refine test (no theme to refine)")
        return

    print("\nTesting /refine...")
    payload = {
        "theme": theme,
        "feedback": "Make the accent color more vibrant pink, and add subtle scanlines effect"
    }
    print(f"  Feedback: {payload['feedback']}")

    resp = requests.post(f"{BASE_URL}/refine", json=payload, timeout=120)
    print(f"  Status: {resp.status_code}")

    if resp.status_code == 200:
        data = resp.json()
        refined = data["theme"]
        print(f"\n  Refined colors:")
        vars = refined.get("variables", {})
        print(f"    accent (was {theme['variables'].get('accent')}): {vars.get('accent')}")
        print(f"    Effects: {refined.get('effects', {})}")

        # Save refined theme
        with open("refined_theme.json", "w") as f:
            json.dump(refined, f, indent=2)
        print(f"\n  Refined theme saved to refined_theme.json")
    else:
        print(f"  Error: {resp.text}")

if __name__ == "__main__":
    print("=" * 50)
    print("Theme Generator Service Test")
    print("=" * 50)

    if not test_health():
        print("\nService not healthy, exiting")
        sys.exit(1)

    theme = test_generate()
    test_refine(theme)

    print("\n" + "=" * 50)
    print("Tests complete!")
