#!/usr/bin/env python3
"""
Claude Code PostToolUse hook: After any git commit that touches eodhd-loader files,
inject a mandatory reminder to kill, rebuild, and relaunch the WPF app.

Problem this solves:
  The eodhd-loader is a local WPF desktop app. Unlike the API (which auto-deploys via
  container), code changes have zero effect until the app is manually rebuilt and
  relaunched. On 2026-02-01, a full dashboard redesign was committed and "deployed" to
  production, but the user still saw the old 5-card layout because the WPF app was
  never rebuilt.

Solution:
  After every git commit that includes files under eodhd-loader/**, this hook
  injects a HARD reminder that the app must be killed, rebuilt, and relaunched before
  telling the user the work is done.

Hook event: PostToolUse (fires AFTER the tool completes)
Matcher: Bash (only git commit commands)
"""

import json
import sys
import re
import subprocess


def is_wsl():
    """Detect if running inside WSL2."""
    try:
        with open("/proc/version", "r") as f:
            return "microsoft" in f.read().lower()
    except (FileNotFoundError, PermissionError):
        return False


def get_committed_files():
    """Get the list of files changed in the most recent commit."""
    try:
        result = subprocess.run(
            ["git", "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD"],
            capture_output=True, text=True, timeout=5
        )
        if result.returncode == 0:
            return result.stdout.strip().split("\n")
    except Exception:
        pass
    return []


def main():
    try:
        hook_input = json.load(sys.stdin)
    except json.JSONDecodeError:
        return 0

    tool_name = hook_input.get("tool_name", "")
    tool_input = hook_input.get("tool_input", {})

    if tool_name != "Bash":
        return 0

    command = tool_input.get("command", "")

    # Only care about git commit commands
    if not re.search(r'\bgit\b.*\bcommit\b', command, re.IGNORECASE):
        return 0

    # Check what files were in this commit
    files = get_committed_files()
    eodhd_files = [f for f in files if f.startswith("eodhd-loader/")]

    if not eodhd_files:
        return 0

    # eodhd-loader files were committed — inject rebuild reminder
    file_list = "\n".join(f"  - {f}" for f in eodhd_files[:10])
    if len(eodhd_files) > 10:
        file_list += f"\n  ... and {len(eodhd_files) - 10} more"

    # Different message based on platform
    if is_wsl():
        context_message = f"""
⚠️ EODHD-LOADER REBUILD REQUIRED — DO NOT SKIP ⚠️

You just committed changes to eodhd-loader files:
{file_list}

Since you're in WSL2, you cannot rebuild EodhdLoader directly (it's a Windows WPF app).
Push your changes and rebuild on Windows manually:

1. Push your changes: git push
2. On Windows, pull the changes: git pull
3. Kill the running EodhdLoader: Get-Process -Name EodhdLoader | Stop-Process -Force
4. Rebuild: dotnet build eodhd-loader/src/EodhdLoader/EodhdLoader.csproj -c Release
5. Relaunch: Start-Process the rebuilt .exe

Do NOT tell the user the work is complete until steps 1-5 are done.
Do NOT say "deployed" — this is a local app, not a deployed service.
"""
    else:
        context_message = f"""
⚠️ EODHD-LOADER REBUILD REQUIRED — DO NOT SKIP ⚠️

You just committed changes to eodhd-loader files:
{file_list}

The eodhd-loader is a LOCAL WPF app. Code changes have ZERO EFFECT until you:
1. Kill the running EodhdLoader process (Stop-Process or taskkill)
2. Rebuild: dotnet build eodhd-loader/src/EodhdLoader/EodhdLoader.csproj -c Release
3. Relaunch: Start-Process the rebuilt .exe

Do NOT tell the user the work is complete until steps 1-3 are done.
Do NOT say "deployed" — this is a local app, not a deployed service.

FAILURE TO REBUILD CAUSED A CRITICAL INCIDENT ON 2026-02-01.
"""

    output = {
        "hookSpecificOutput": {
            "hookEventName": "PostToolUse",
            "additionalContext": context_message
        }
    }

    print(json.dumps(output))
    return 0


if __name__ == "__main__":
    sys.exit(main())
