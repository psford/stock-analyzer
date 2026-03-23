#!/usr/bin/env python3
"""
Claude Code PostToolUse hook: Spec staleness guard.

Fires after git push. Compares source-code delta on this branch against
TECHNICAL_SPEC.md changes. If significant source changes exist but
TECHNICAL_SPEC.md has not been touched in this branch, injects a
mandatory reminder.

Advisory only (no exit 2) — injects into the response as hard context.
Replaces the per-commit spec reminder in git_commit_guard.py.

Threshold: 50 net changed lines of .js/.cs code without any spec change.
"""

import json
import sys
import re
import subprocess


SOURCE_EXTENSIONS = re.compile(r'\.(js|cs|ts|py)$', re.IGNORECASE)
# Paths that are NOT application source — don't require spec updates
EXCLUDED_PATHS = re.compile(
    r'^(?:'
    r'\.claude/|'
    r'infrastructure/|'
    r'docs/|'
    r'helpers/hooks/|'
    r'\.github/'
    r')'
)
SPEC_PATH = "docs/TECHNICAL_SPEC.md"
NEW_LINES_THRESHOLD = 50


def run_git(*args, timeout=10):
    try:
        result = subprocess.run(
            ["git"] + list(args),
            capture_output=True, text=True, timeout=timeout
        )
        return result.stdout.strip() if result.returncode == 0 else ""
    except Exception:
        return ""


def get_merge_base():
    return run_git("merge-base", "HEAD", "origin/main")


def get_spec_diff_lines(merge_base):
    if not merge_base:
        return 0
    diff = run_git("diff", merge_base, "HEAD", "--", SPEC_PATH)
    if not diff:
        return 0
    added = sum(1 for line in diff.split("\n") if line.startswith("+") and not line.startswith("+++"))
    return added


def get_source_diff_lines(merge_base):
    if not merge_base:
        return 0
    diff = run_git("diff", merge_base, "HEAD", "--stat")
    if not diff:
        return 0

    total = 0
    for line in diff.split("\n"):
        m = re.match(r'\s*(.+?)\s*\|\s*(\d+)', line)
        if not m:
            continue
        fpath, changes = m.group(1).strip(), int(m.group(2))
        if SOURCE_EXTENSIONS.search(fpath) and not EXCLUDED_PATHS.match(fpath):
            total += changes
    return total


def get_current_branch():
    return run_git("rev-parse", "--abbrev-ref", "HEAD")


def main():
    try:
        hook_input = json.load(sys.stdin)
    except (json.JSONDecodeError, EOFError):
        return 0

    tool_name = hook_input.get("tool_name", "")
    tool_input = hook_input.get("tool_input", {})

    if tool_name != "Bash":
        return 0

    command = tool_input.get("command", "")
    if not re.search(r'\bgit\b.*\bpush\b', command, re.IGNORECASE):
        return 0

    branch = get_current_branch()
    if not branch or branch in ("main", "develop"):
        return 0

    run_git("fetch", "origin", "main", "--quiet", timeout=15)

    merge_base = get_merge_base()
    if not merge_base:
        return 0

    source_lines = get_source_diff_lines(merge_base)
    spec_lines = get_spec_diff_lines(merge_base)

    if spec_lines > 0 or source_lines < NEW_LINES_THRESHOLD:
        return 0

    context = (
        f"SPEC STALENESS WARNING\n\n"
        f"Branch '{branch}' has {source_lines} changed lines of source code "
        f"since branching from main, but TECHNICAL_SPEC.md has 0 changes.\n\n"
        f"CLAUDE.md requires: Update TECHNICAL_SPEC.md AS you code, "
        f"stage with code commits.\n\n"
        f"Before this push is complete:\n"
        f"1. Read docs/TECHNICAL_SPEC.md\n"
        f"2. Add/update sections covering: architecture changes, new endpoints, "
        f"new JS modules, changed data flows\n"
        f"3. Stage and commit the spec update\n"
        f"4. Push again\n\n"
        f"If the changes are genuinely non-spec-worthy (CSS tweaks, test files only), "
        f"add a comment in the commit message explaining why."
    )

    output = {
        "hookSpecificOutput": {
            "hookEventName": "PostToolUse",
            "additionalContext": context
        }
    }
    print(json.dumps(output))
    return 0


if __name__ == "__main__":
    sys.exit(main())
