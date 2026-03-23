#!/usr/bin/env python3
"""
Theme Manager for Stock Analyzer
Upload, validate, create, and manage JSON themes on Azure Blob Storage.

Usage:
    python theme_manager.py list                          List available themes
    python theme_manager.py preview theme_name            Show theme colors/effects
    python theme_manager.py create new_id --from base     Create new theme from template
    python theme_manager.py validate [theme_name]         Validate theme JSON structure
    python theme_manager.py upload [--all | theme_name]   Upload theme(s) to Azure
    python theme_manager.py deploy theme_name             Validate + upload a theme

Examples:
    python theme_manager.py list                          Show all themes
    python theme_manager.py create cyberpunk --from dark  Create 'cyberpunk' based on dark
    python theme_manager.py preview cyberpunk             Check your new theme
    python theme_manager.py deploy cyberpunk              Validate and upload to Azure
    python theme_manager.py upload --all                  Upload all themes
"""

import argparse
import json
import subprocess
import sys
from pathlib import Path

# Configuration
THEMES_DIR = Path(__file__).parent.parent / "src/StockAnalyzer.Api/wwwroot/themes"
AZURE_ACCOUNT = "stockanalyzerblob"
AZURE_CONTAINER = "$web"
AZURE_PATH_PREFIX = "themes/"
AZ_CLI = r"C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd"

# Required fields in theme JSON
REQUIRED_FIELDS = {"id", "name", "version", "variables"}
REQUIRED_VARIABLES = {
    "bg-primary", "bg-secondary", "text-primary", "text-secondary",
    "accent", "border-primary", "success", "error", "warning"
}


def load_manifest():
    """Load the themes manifest."""
    manifest_path = THEMES_DIR / "manifest.json"
    if not manifest_path.exists():
        print(f"ERROR: Manifest not found at {manifest_path}")
        sys.exit(1)
    return json.loads(manifest_path.read_text())


def save_manifest(manifest: dict):
    """Save the themes manifest."""
    manifest_path = THEMES_DIR / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")


def load_theme(theme_id: str) -> dict:
    """Load a theme JSON file."""
    manifest = load_manifest()
    for theme in manifest["themes"]:
        if theme["id"] == theme_id:
            theme_path = THEMES_DIR / theme["file"]
            if not theme_path.exists():
                print(f"ERROR: Theme file not found: {theme_path}")
                sys.exit(1)
            return json.loads(theme_path.read_text())
    print(f"ERROR: Theme '{theme_id}' not found in manifest")
    sys.exit(1)


def validate_theme(theme_data: dict, theme_id: str) -> list[str]:
    """Validate theme JSON structure. Returns list of errors."""
    errors = []

    # Check required top-level fields
    for field in REQUIRED_FIELDS:
        if field not in theme_data:
            errors.append(f"Missing required field: {field}")

    # Check ID matches filename
    if theme_data.get("id") != theme_id:
        errors.append(f"Theme ID '{theme_data.get('id')}' doesn't match expected '{theme_id}'")

    # Check required variables
    variables = theme_data.get("variables", {})
    for var in REQUIRED_VARIABLES:
        if var not in variables:
            errors.append(f"Missing required variable: {var}")

    # Validate effects structure if present
    effects = theme_data.get("effects", {})
    for effect_name, effect_config in effects.items():
        if not isinstance(effect_config, dict):
            errors.append(f"Effect '{effect_name}' must be an object")
        elif "enabled" not in effect_config:
            errors.append(f"Effect '{effect_name}' missing 'enabled' field")

    return errors


def upload_file(local_path: Path, blob_name: str) -> bool:
    """Upload a file to Azure Blob Storage."""
    cmd = [
        AZ_CLI, "storage", "blob", "upload",
        "--account-name", AZURE_ACCOUNT,
        "--container-name", AZURE_CONTAINER,
        "--name", blob_name,
        "--file", str(local_path),
        "--content-type", "application/json",
        "--overwrite"
    ]

    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(f"  ERROR: {result.stderr}")
        return False
    return True


def cmd_upload(args):
    """Upload themes to Azure."""
    manifest = load_manifest()

    if args.all:
        files_to_upload = ["manifest.json"] + [t["file"] for t in manifest["themes"]]
    elif args.theme:
        # Find the theme file
        theme_file = None
        for t in manifest["themes"]:
            if t["id"] == args.theme:
                theme_file = t["file"]
                break
        if not theme_file:
            print(f"ERROR: Theme '{args.theme}' not found")
            sys.exit(1)
        files_to_upload = [theme_file]
    else:
        print("Specify --all or a theme name")
        sys.exit(1)

    print(f"Uploading {len(files_to_upload)} file(s) to Azure...")

    success = 0
    for filename in files_to_upload:
        local_path = THEMES_DIR / filename
        blob_name = AZURE_PATH_PREFIX + filename
        print(f"  {filename} -> {blob_name}...", end=" ")

        if upload_file(local_path, blob_name):
            print("OK")
            success += 1
        else:
            print("FAILED")

    print(f"\nUploaded {success}/{len(files_to_upload)} files")
    print(f"Themes available at: https://{AZURE_ACCOUNT}.z13.web.core.windows.net/{AZURE_PATH_PREFIX}")


def cmd_validate(args):
    """Validate theme JSON structure."""
    manifest = load_manifest()

    if args.theme:
        themes_to_check = [t for t in manifest["themes"] if t["id"] == args.theme]
        if not themes_to_check:
            print(f"ERROR: Theme '{args.theme}' not found")
            sys.exit(1)
    else:
        themes_to_check = manifest["themes"]

    all_valid = True
    for theme_info in themes_to_check:
        theme_id = theme_info["id"]
        theme_path = THEMES_DIR / theme_info["file"]

        print(f"Validating {theme_id}...", end=" ")

        try:
            theme_data = json.loads(theme_path.read_text())
            errors = validate_theme(theme_data, theme_id)

            if errors:
                print("INVALID")
                for err in errors:
                    print(f"  - {err}")
                all_valid = False
            else:
                var_count = len(theme_data.get("variables", {}))
                effect_count = len(theme_data.get("effects", {}))
                print(f"OK ({var_count} variables, {effect_count} effects)")
        except json.JSONDecodeError as e:
            print(f"INVALID JSON: {e}")
            all_valid = False

    if all_valid:
        print("\nAll themes valid!")
    else:
        print("\nSome themes have errors")
        sys.exit(1)


def cmd_list(args):
    """List available themes."""
    manifest = load_manifest()

    print(f"Theme Manifest v{manifest.get('version', '?')}")
    print(f"Default: {manifest.get('default', 'none')}")
    print()

    for theme in manifest["themes"]:
        theme_path = THEMES_DIR / theme["file"]
        if theme_path.exists():
            data = json.loads(theme_path.read_text())
            var_count = len(data.get("variables", {}))
            effect_count = len(data.get("effects", {}))
            status = f"{var_count} vars, {effect_count} effects"
        else:
            status = "FILE MISSING"

        builtin = " [builtin]" if theme.get("builtin") else ""
        print(f"  {theme['id']:12} - {theme['name']}{builtin}")
        print(f"               {theme['file']} ({status})")


def cmd_preview(args):
    """Preview a theme's colors and effects."""
    theme_data = load_theme(args.theme)

    print(f"Theme: {theme_data['name']} (v{theme_data.get('version', '?')})")
    print()

    # Show key colors
    variables = theme_data.get("variables", {})
    print("Key Colors:")
    key_vars = ["bg-primary", "bg-secondary", "text-primary", "accent", "success", "error"]
    for var in key_vars:
        value = variables.get(var, "NOT SET")
        print(f"  --{var}: {value}")

    # Show effects
    effects = theme_data.get("effects", {})
    if effects:
        print("\nEffects:")
        for name, config in effects.items():
            enabled = "ON" if config.get("enabled") else "OFF"
            params = ", ".join(f"{k}={v}" for k, v in config.items() if k != "enabled")
            print(f"  {name}: {enabled}" + (f" ({params})" if params else ""))
    else:
        print("\nNo effects configured")

    # Show fonts
    fonts = theme_data.get("fonts", {})
    if fonts:
        print("\nFonts:")
        for name, value in fonts.items():
            # Truncate long font stacks
            display = value[:50] + "..." if len(value) > 50 else value
            print(f"  {name}: {display}")


def cmd_create(args):
    """Create a new theme from a template."""
    new_id = args.new_id
    base_id = args.base
    new_name = args.name or new_id.replace("-", " ").title()

    # Validate new_id format
    if not new_id.replace("-", "").isalnum():
        print("ERROR: Theme ID should only contain letters, numbers, and hyphens")
        sys.exit(1)

    # Check if theme already exists
    manifest = load_manifest()
    existing_ids = [t["id"] for t in manifest["themes"]]
    if new_id in existing_ids:
        print(f"ERROR: Theme '{new_id}' already exists")
        sys.exit(1)

    # Load base theme
    if base_id not in existing_ids:
        print(f"ERROR: Base theme '{base_id}' not found")
        print(f"Available: {', '.join(existing_ids)}")
        sys.exit(1)

    base_data = load_theme(base_id)

    # Create new theme
    new_theme = base_data.copy()
    new_theme["id"] = new_id
    new_theme["name"] = new_name
    new_theme["version"] = "1.0.0"

    # Update meta if present
    if "meta" in new_theme:
        new_theme["meta"] = new_theme["meta"].copy()
        # Keep category from base but could be customized

    # Write theme file
    new_filename = f"{new_id}.json"
    new_path = THEMES_DIR / new_filename
    new_path.write_text(json.dumps(new_theme, indent=2) + "\n")
    print(f"Created: {new_path}")

    # Add to manifest
    manifest["themes"].append({
        "id": new_id,
        "name": new_name,
        "file": new_filename,
        "builtin": False
    })
    save_manifest(manifest)
    print(f"Added to manifest")

    print(f"\nTheme '{new_name}' created successfully!")
    print(f"\nNext steps:")
    print(f"  1. Edit {new_filename} to customize colors/effects")
    print(f"  2. python theme_manager.py preview {new_id}")
    print(f"  3. python theme_manager.py deploy {new_id}")


def cmd_deploy(args):
    """Validate and upload a theme to Azure."""
    theme_id = args.theme

    # Validate first
    manifest = load_manifest()
    theme_info = None
    for t in manifest["themes"]:
        if t["id"] == theme_id:
            theme_info = t
            break

    if not theme_info:
        print(f"ERROR: Theme '{theme_id}' not found")
        sys.exit(1)

    theme_path = THEMES_DIR / theme_info["file"]
    print(f"Validating {theme_id}...", end=" ")

    try:
        theme_data = json.loads(theme_path.read_text())
        errors = validate_theme(theme_data, theme_id)

        if errors:
            print("INVALID")
            for err in errors:
                print(f"  - {err}")
            sys.exit(1)
        else:
            print("OK")
    except json.JSONDecodeError as e:
        print(f"INVALID JSON: {e}")
        sys.exit(1)

    # Upload theme file
    print(f"\nUploading {theme_info['file']}...", end=" ")
    blob_name = AZURE_PATH_PREFIX + theme_info["file"]
    if upload_file(theme_path, blob_name):
        print("OK")
    else:
        print("FAILED")
        sys.exit(1)

    # Upload updated manifest
    print("Uploading manifest.json...", end=" ")
    manifest_path = THEMES_DIR / "manifest.json"
    if upload_file(manifest_path, AZURE_PATH_PREFIX + "manifest.json"):
        print("OK")
    else:
        print("FAILED")
        sys.exit(1)

    print(f"\nTheme '{theme_id}' deployed!")
    print(f"Live at: https://{AZURE_ACCOUNT}.z13.web.core.windows.net/{AZURE_PATH_PREFIX}{theme_info['file']}")


def main():
    parser = argparse.ArgumentParser(description="Manage Stock Analyzer themes")
    subparsers = parser.add_subparsers(dest="command", help="Command")

    # List command
    subparsers.add_parser("list", help="List available themes")

    # Preview command
    preview_parser = subparsers.add_parser("preview", help="Preview theme colors/effects")
    preview_parser.add_argument("theme", help="Theme ID to preview")

    # Create command
    create_parser = subparsers.add_parser("create", help="Create a new theme from template")
    create_parser.add_argument("new_id", help="ID for the new theme (e.g., 'cyberpunk')")
    create_parser.add_argument("--from", dest="base", required=True, help="Base theme to copy from")
    create_parser.add_argument("--name", help="Display name (defaults to title-cased ID)")

    # Validate command
    validate_parser = subparsers.add_parser("validate", help="Validate theme JSON")
    validate_parser.add_argument("theme", nargs="?", help="Theme ID (or all if omitted)")

    # Upload command
    upload_parser = subparsers.add_parser("upload", help="Upload theme(s) to Azure")
    upload_group = upload_parser.add_mutually_exclusive_group()
    upload_group.add_argument("--all", action="store_true", help="Upload all themes")
    upload_group.add_argument("theme", nargs="?", help="Theme ID to upload")

    # Deploy command (validate + upload)
    deploy_parser = subparsers.add_parser("deploy", help="Validate and upload a theme")
    deploy_parser.add_argument("theme", help="Theme ID to deploy")

    args = parser.parse_args()

    if args.command == "list":
        cmd_list(args)
    elif args.command == "preview":
        cmd_preview(args)
    elif args.command == "create":
        cmd_create(args)
    elif args.command == "validate":
        cmd_validate(args)
    elif args.command == "upload":
        cmd_upload(args)
    elif args.command == "deploy":
        cmd_deploy(args)
    else:
        parser.print_help()


if __name__ == "__main__":
    main()
