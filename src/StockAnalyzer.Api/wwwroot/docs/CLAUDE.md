# CLAUDE.md

Instructions and shared knowledge for Claude Code sessions.

---

## About

**User:** Patrick - financial services business analyst background, experience with Matlab, Python, Ruby.

**Preferred languages:** Python, TypeScript, HTML, CSS, C# (.NET)

**Active project:** Stock Analyzer (.NET) - `stock_analyzer_dotnet/`

---

## Principles

These always apply, regardless of task.

| Principle | Description |
|-----------|-------------|
| **Challenge me** | If I ask for something against best practices or introducing security vulnerabilities, push back. |
| **Admit limitations** | If asked to do something I cannot actually do (e.g., "verify the UI looks correct" when I can't see rendered output), say so immediately and suggest mitigations. Never pretend to have capabilities I lack. |
| **No illegal actions** | Never act illegally, period. |
| **No paid services** | Never sign up for paid services on my behalf. |
| **Cite sources** | When making recommendations, cite sources so I can verify. |
| **Offer alternatives** | When suggesting a language/approach, provide alternatives with tradeoffs. |
| **Prefer FOSS** | Choose well-supported open source (MIT, Apache 2.0, BSD) over proprietary. Prefer lightweight, offline-capable, established tools. |
| **Use winget** | For Windows app installations, prefer winget as the package manager. Fall back to Chocolatey if winget fails or lacks the package. |
| **No ad tech/tracking** | Never integrate advertising technology, tracking pixels, analytics that share data externally, or any data sharing with X (Twitter) or Meta. |
| **Math precision** | If uncertain about calculation accuracy to 5 decimal places, say so. |
| **No feature regression** | Changes should never lose functionality. If unavoidable, explain tradeoffs clearly. |
| **Minimize yak-shaving** | Work autonomously whenever possible. Create accounts, store passwords securely, build scaffolding without asking for direction. Don't ask for help on tasks you can figure out yourself. |
| **Act on credentials** | When given API keys, passwords, or other credentials, use them directly to complete the task. Don't provide instructions for the user to do it themselves - do it. |
| **Update specs proactively** | When implementing features, always update TECHNICAL_SPEC.md, ROADMAP.md, and other docs as part of the work - not as an afterthought. Don't wait to be reminded. |
| **Commit to GitHub** | Work isn't finished until it's committed and pushed to GitHub. Always end sessions with everything in the repo. |
| **GitHub best practices** | Follow GitHub conventions: README.md and LICENSE at repo root, CONTRIBUTING.md for contribution guidelines, .github/ for templates and workflows. Use standard file names (README.md not readme.txt). |
| **Validate doc links** | Before committing documentation changes, run `python helpers/check_links.py --all` to verify all markdown links resolve. Broken links are unacceptable. |

---

## Session Protocol

### Starting a Session ("hello!")

When I say "hello!" at the start of a session:

1. Read these files to restore context:
   - `CLAUDE.md` (this file)
   - `sessionState.md` (where we left off)
   - `claudeLog.md` (recent actions)
   - `whileYouWereAway.md` (pending tasks)

2. If `whileYouWereAway.md` has tasks, ask if I want to work on them.

3. For multi-step WYA tasks, complete one step at a time and wait for my evaluation.

### During a Session

**Checkpoints** - Save state periodically to enable recovery:
- When: After major tasks, every 10-15 exchanges, before complex work
- How: Update `sessionState.md` or run `python helpers/checkpoint.py save "description"`
- Reserve ~5,000-6,000 tokens for graceful exit
- Warning signs: Output truncation, summarization, very long conversation

**Context efficiency** - Don't load files "just in case":
- Hot (load now): Data actively needed for current task
- Cold (fetch later): Reference material that might not be needed
- Exception: Always load CLAUDE.md - rules files are sacrosanct

**Between tasks** - When a task is complete and looking for what to do next:
- Check Slack for new messages/tasks
- Review `whileYouWereAway.md` for pending items
- Ask if there's anything else to work on

### Ending a Session ("night!")

When I say "night!":

1. Update `sessionState.md` with current context
2. Commit all pending changes
3. Update `claudeLog.md`

---

## Development Workflow

### Branching Strategy (MANDATORY)

We use a **Light SDLC** model with manual production deploys. **All development work happens on `develop` branch.**

```
develop (all work here) → User says "deploy" → Merge to master → Deploy to Production
```

| Branch | Purpose | Protection |
|--------|---------|------------|
| `develop` | ALL development work. Default working branch. | CI must pass |
| `master` | Production-ready code ONLY. Triggers deployment. | PR from develop only |

**STRICT Workflow - Follow exactly:**

1. **Start of work:** Switch to `develop` branch
   ```bash
   git checkout develop
   ```

2. **During work:** Commit to `develop` freely
   - Push to develop after completing work
   - Restart localhost server so user can test
   - Tell user: "Ready for testing at localhost:5000"

3. **After user testing:** Wait for explicit approval
   - User must say **"deploy"** to authorize production deployment
   - Do NOT merge to master or deploy without this approval

4. **On "deploy" approval:**
   - Verify all pre-deploy checklist items (see below)
   - Merge `develop` → `master`
   - **Show merge command and WAIT for explicit user confirmation before executing**
   - Trigger GitHub Actions deployment after user confirms merge

**CRITICAL MERGE RULES:**
- **NEVER** commit directly to master
- **NEVER** deploy without user saying "deploy"
- **NEVER** execute `git merge` to master without explicit user acceptance
- Always show the merge command and wait for confirmation

**Production Deploy:**
- Go to GitHub Actions → "Deploy to Azure Production"
- Click "Run workflow"
- Type `deploy` to confirm, provide reason
- Workflow builds, tests, and deploys to https://psfordtaurus.com

**CRITICAL - Pre-Deploy Checklist:**
Before ANY deployment to production:
1. ✅ TECHNICAL_SPEC.md updated with all code changes
2. ✅ FUNCTIONAL_SPEC.md updated if user-facing changes
3. ✅ wwwroot/docs/ synced with source docs (rebuild triggers sync)
4. ✅ Version history updated in specs
5. ✅ Security scans passed (CI checks)
6. ✅ User has tested on localhost and approved

**Never deploy to production without updating specs first.** This is a hard rule.

**Rollback:** See `stock_analyzer_dotnet/docs/RUNBOOK.md`

### Planning Phase

- For non-trivial tasks, use plan mode to design approach before coding
- When uncertain about requirements, research the web first, then ask me if still unclear
- Treat "as a user..." statements as functional requirements - add to `FUNCTIONAL_SPEC.md`

### Coding Phase

**Standards:**
- JavaScript/TypeScript: `camelCase`
- Python: `snake_case` (PEP 8)
- Documentation: GitHub-flavored Markdown

**Testing requirements:**
- Code compiling is NOT sufficient verification
- For UI changes: Use Playwright (`helpers/ui_test.py`) to verify:
  - `smoke` - Page loads without JS errors
  - `verify` - Required elements exist and are visible
  - `screenshot` - Capture visual state for review
- For interactive features: Verify the interaction produces expected results
- For services: Test the full round-trip, not just that it starts
- If unable to verify directly, say so and ask me to test

**External dependencies:**
- Before integrating any external service/API/CDN, verify it's operational
- Check the response is what you expect
- Have a fallback plan if unreliable
- Never assume a service works - test it now

**Spec updates - do them incrementally:**
- Update specs AS you write code, not after
- Don't batch spec updates at end of task - that leads to forgotten details
- Each code commit should include its corresponding spec changes
- If adding a new file/service/endpoint, update TECHNICAL_SPEC.md before moving on

### Pre-Commit Checkpoint (CRITICAL)

**"Commit" means the full workflow, not just `git commit`.**

Before every commit, STOP and verify:

1. **Specs updated?**
   - `TECHNICAL_SPEC.md` - update for ANY code changes (files, deps, architecture, config, tests)
   - `FUNCTIONAL_SPEC.md` - update ONLY for user-facing changes (features, UI, workflows)
   - Stage specs WITH code - same commit, not follow-up

2. **Log updated?**
   - Add entry to `claudeLog.md`

3. **All files staged?**
   - Check `git status` before committing

4. **Tested?**
   - Server should be running
   - Feature should be verified working

Commit message should describe what was built AND documented.

### Post-Commit / Deployment

**D1. Kill before deploy**
- Check for existing processes of the service
- Kill old instances before starting new
- Verify termination before proceeding

**D2. Redeploy after committing**
- A commit doesn't restart running services
- Ask if I want to restart after committing service changes

**D3. Building ≠ Running**
- Successful build doesn't mean service is accessible
- Verify process is running and port is listening
- Hit health check to confirm
- Never claim "ready at localhost:X" based solely on build success

**D4. Test after deployment**
- Don't assume new code works because process started
- Exercise the modified feature
- Check for regressions

**D5. Deployment checklist**
1. Commit changes
2. Kill old process(es)
3. Start new process
4. Verify process running
5. Test the specific change
6. Smoke test basic functionality

---

## Communication

**Research before asking** - Search the web first for syntax, best practices, technical details. Only ask me if research doesn't provide a clear answer.

**Correction vs inquiry** - If I ask "Did you do X?" and the answer is no, ask whether I want it added as a guideline. I may be inquiring or correcting - don't assume which.

**Proactive guideline updates** - When I give feedback that would improve future results or prevent repeated issues, add it to this file without being asked. Not every comment needs a rule, but patterns and corrections should be captured.

**Slack integration:**
- Proactively restart the Slack listener if it appears disconnected
- Add ✅ reaction to EVERY Slack message when acknowledged (not just when completed)
- Mark message as `read: true` in `slack_inbox.json` after reacting
- Keep `slack_inbox.json` and `slack_last_sync.txt` at project root

---

## File Management

**Version control:**
- Commit freely as part of normal workflow - no need to ask permission
- Before overwriting any file, ensure previous version is recoverable (commit first, or backup)
- Never overwrite plan files - create new ones instead

**CLAUDE.md backups:**
- Before updating this file, save backup as `claude_MMDDYYYY-N.md`
- N = commit number for that day (1, 2, 3...)

**Logging:**
- Log actions to `claudeLog.md` with date, description, result (success/failure)
- Omit sensitive data
- If file exceeds 1GB, archive as `claudeLog_MMDDYYYY.md` and start fresh

**Archiving projects:**
- Archive source code to `archive/` folder (preserve for reference)
- Delete cruft: `__pycache__`, `node_modules`, `bin/`, `obj/`, logs, temp files
- Consolidate shared helpers/configs to common location

---

## Security

**Scanning:**
- When introducing new frameworks/languages, review SAST/DAST coverage
- Add appropriate scanners (SecurityCodeScan for C#, Bandit for Python)
- Document scan findings in `ROADMAP.md` with severity and recommended fix

**Pre-commit hooks:**
- Hooks run automatically on commit
- If blocked by a hook, determine if you can adjust; if not, ask me to check hook configuration

---

## Project Files Reference

| File | Purpose |
|------|---------|
| `CLAUDE.md` | This file - rules and shared knowledge |
| `sessionState.md` | Current session context for continuity |
| `claudeLog.md` | Action log with dates and outcomes |
| `whileYouWereAway.md` | Task queue for async work |
| `ROADMAP.md` | Feature roadmap (in `stock_analyzer_dotnet/`) |
| `FUNCTIONAL_SPEC.md` | User-facing requirements (in `stock_analyzer_dotnet/docs/`) |
| `TECHNICAL_SPEC.md` | Technical implementation details (in `stock_analyzer_dotnet/docs/`) |
| `helpers/` | Reusable Python scripts (Slack, security, checkpoints, UI testing, speech-to-text) |
| `.env` | API keys (Slack tokens, Finnhub) - not committed |

---

## Stock Analyzer Specific

**Web documentation sync:**
The documentation page serves copies of specs from `wwwroot/docs/`. These sync automatically during `dotnet build` via MSBuild targets. After updating source specs, rebuild to sync.

| Source | Destination |
|--------|-------------|
| `claudeProjects/CLAUDE.md` | `wwwroot/docs/CLAUDE.md` |
| `stock_analyzer_dotnet/docs/FUNCTIONAL_SPEC.md` | `wwwroot/docs/FUNCTIONAL_SPEC.md` |
| `stock_analyzer_dotnet/docs/TECHNICAL_SPEC.md` | `wwwroot/docs/TECHNICAL_SPEC.md` |

**Feature conventions:**

| Pattern | Components |
|---------|------------|
| **±5% Significant Move Markers** | When adding this feature to any chart, include the complete package: (1) Triangle markers on chart for days with ≥5% change, (2) Toggle checkbox to show/hide markers, (3) Wikipedia-style hover cards on marker hover, (4) Cat/dog image toggle, (5) News content in hover card (source varies by context - stock-specific news for individual stocks, market news for portfolios). |

---

## Deprecated / Archived

**Python stock_analysis project** - Archived to `archive/stock_analysis_python/`. The .NET version is now the sole active implementation.

**yfinance dividend yield issue** - Applied to archived Python code. The .NET version uses a different data source.
