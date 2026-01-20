# CI/CD Security Implementation

**Last Updated:** 2026-01-18
**Status:** Implemented

This document describes the security tooling integrated into the Stock Analyzer CI/CD pipeline.

---

## Security Toolchain Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         Security Pipeline                                │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────────────────┐   │
│  │  Pre-commit  │ →  │  GitHub CI   │ →  │  Production Deployment   │   │
│  │    Hooks     │    │   Actions    │    │      (Azure)             │   │
│  └──────────────┘    └──────────────┘    └──────────────────────────┘   │
│         │                   │                        │                   │
│         ▼                   ▼                        ▼                   │
│  • detect-secrets    • CodeQL SAST          • Cloudflare SSL            │
│  • detect-private-   • OWASP Dep Check      • Security headers          │
│    key               • .NET analyzers       • CSP policy                │
│  • Bandit (Python)   • Build validation                                 │
│                      • Test execution                                    │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Pre-commit Security (Local)

Runs automatically on every `git commit`. Configured in `.pre-commit-config.yaml`.

| Tool | Purpose | Blocks Commit? |
|------|---------|----------------|
| **detect-secrets** | Prevents committing API keys, passwords, tokens | Yes |
| **detect-private-key** | Blocks private key files | Yes |
| **Bandit** | Python SAST for security issues | Yes (on findings) |
| **end-of-file-fixer** | Ensures newline at EOF | Auto-fixes |
| **trailing-whitespace** | Removes trailing spaces | Auto-fixes |
| **check-merge-conflict** | Blocks merge conflict markers | Yes |

**Secrets baseline:** `.secrets.baseline` tracks known false positives.

---

## GitHub Actions Security

### CodeQL Analysis (`.github/workflows/codeql.yml`)

Static Application Security Testing (SAST) for C# and Python.

| Setting | Value |
|---------|-------|
| **Languages** | C#, Python |
| **Triggers** | Push to master/main, PRs, Weekly (Monday 6 AM UTC) |
| **Query Suite** | security-extended (more rules than default) |
| **Results** | GitHub Security tab |

**What it detects:**
- SQL injection
- Command injection
- Path traversal
- XSS vulnerabilities
- Insecure deserialization
- Hardcoded credentials
- And 100+ other security rules

### OWASP Dependency Check (`.github/workflows/dotnet-ci.yml`)

Scans NuGet packages for known vulnerabilities (CVEs).

| Setting | Value |
|---------|-------|
| **Fail Threshold** | CVSS 7+ (High/Critical) |
| **Output** | HTML report (archived as artifact) |
| **Schedule** | Every push and PR |

### Dependabot (`.github/dependabot.yml`)

Automated dependency updates for security patches.

| Ecosystem | Directory | Schedule |
|-----------|-----------|----------|
| **NuGet** | /stock_analyzer_dotnet | Weekly (Monday) |
| **pip** | / | Weekly (Monday) |
| **GitHub Actions** | / | Weekly (Monday) |

**Features:**
- Auto-creates PRs for vulnerable dependencies
- Groups minor/patch updates to reduce noise
- Labels PRs with "dependencies" and "security"

---

## Build-time Security

### .NET Security Analyzers

Integrated into the build process via NuGet packages.

| Analyzer | Purpose |
|----------|---------|
| **Microsoft.CodeAnalysis.NetAnalyzers** | Official .NET security/reliability rules |
| **Roslynator.Analyzers** | Extended code quality + security checks |
| **SecurityCodeScan.VS2019** | OWASP Top 10 detection for C# |

**Configuration:** Warnings treated as errors for security rules (CA2xxx, SCS0xxx).

---

## Branch Protection

GitHub branch protection rules enforce security checks. **Direct pushes to both `develop` and `master` are blocked for all users, including admins.**

### Protected Branches

| Branch | Purpose | Protection Rules |
|--------|---------|------------------|
| `develop` | Integration/testing | PR required, CI must pass, enforce admins |
| `master` | Production releases | PR from develop only, CI must pass, enforce admins |

### Workflow

```
feature/X → PR to develop → (approval) → merge → (deploy approval) → PR to master → Production
```

All changes follow this path. No exceptions.

### Protection Rules (Both Branches)

| Rule | Setting |
|------|---------|
| **Require pull request** | All changes must go through PR |
| **Enforce for admins** | Enabled - no bypass allowed |
| **Require status checks** | build-and-test must pass |
| **Dismiss stale reviews** | New commits invalidate prior approvals |
| **No force push** | Protected on both branches |

---

## Production Security

### Cloudflare (Edge)

| Feature | Status |
|---------|--------|
| **SSL/TLS** | Flexible mode (HTTPS to users) |
| **DDoS protection** | Automatic |
| **WAF** | Basic rules (free tier) |

### Application Headers

Security headers configured in ASP.NET Core middleware:

```csharp
// Program.cs
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] = "...";
    await next();
});
```

### Subresource Integrity (SRI)

CDN scripts include integrity hashes:

```html
<script src="https://cdn.plot.ly/plotly-2.27.0.min.js"
        integrity="sha384-..."
        crossorigin="anonymous"></script>
```

---

## Security Workflow

```
Developer Machine                GitHub                        Production
─────────────────               ──────────                     ──────────

1. Write code
       │
       ▼
2. git commit
       │
       ▼
3. Pre-commit hooks ──────▶ BLOCKED if secrets detected
       │
       ▼
4. git push ─────────────────▶ 5. GitHub Actions
                                      │
                              ┌───────┴───────┐
                              ▼               ▼
                          CodeQL          Build/Test
                          (SAST)          + OWASP
                              │               │
                              └───────┬───────┘
                                      ▼
                              6. PR Review
                                      │
                                      ▼
                              7. Merge to master
                                      │
                                      ▼
                              8. azure-deploy.yml ────────────▶ 9. ACI + SQL
                                                                     │
                                                                     ▼
                                                               10. Cloudflare
                                                                   (SSL/WAF)
```

---

## Implementation Checklist

All items have been implemented unless noted otherwise.

### GitHub Actions
- [x] Create `.github/workflows/codeql.yml` - CodeQL for C# and Python
- [x] Enable Dependabot security updates - `.github/dependabot.yml`
- [x] Enable secret scanning - GitHub repo settings
- [x] Configure branch protection rules - PR required, status checks

### CI Pipeline
- [x] OWASP Dependency Check in `.github/workflows/dotnet-ci.yml`
- [x] .NET security analyzers (NetAnalyzers, Roslynator, SecurityCodeScan)
- [x] .NET test execution and artifact upload
- [x] JavaScript unit tests (Jest) for frontend portfolio aggregation
- [x] Cross-platform build validation (Ubuntu + Windows)

### Pre-commit
- [x] detect-secrets with baseline
- [x] detect-private-key
- [x] Bandit for Python
- [x] Merge conflict detection

### Production
- [x] Cloudflare SSL/TLS
- [x] Security headers middleware
- [x] SRI for CDN scripts

### Pending
- [ ] Cloudflare IP allowlist at origin (security hardening)
- [ ] Azure Key Vault for secrets management
- [ ] Container image scanning (if needed)
- [ ] DAST (ZAP) in staging environment

---

## Tool Summary

| Tool | Purpose | Where | Status |
|------|---------|-------|--------|
| **CodeQL** | SAST for C#/Python | GitHub Actions | Active |
| **Dependabot** | Dependency vulnerabilities | GitHub | Active |
| **OWASP Dependency Check** | .NET dependency scan | GitHub Actions | Active |
| **Jest** | JavaScript unit tests | GitHub Actions | Active |
| **Bandit** | Python SAST | Pre-commit | Active |
| **detect-secrets** | Secrets in code | Pre-commit | Active |
| **NetAnalyzers** | .NET code analysis | Build | Active |
| **Roslynator** | Extended code quality | Build | Active |
| **SecurityCodeScan** | OWASP Top 10 for C# | Build | Active |
| **Cloudflare** | Edge security (SSL, WAF) | Production | Active |

---

## Reviewing Security Findings

### CodeQL Findings
1. Go to GitHub repo → Security tab → Code scanning alerts
2. Review findings by severity (Critical, High, Medium, Low)
3. Click alert for details and remediation guidance
4. Dismiss with reason if false positive

### OWASP Dependency Check
1. Go to GitHub repo → Actions → Latest dotnet-ci run
2. Download "dependency-check-report" artifact
3. Open HTML report in browser
4. Focus on CVSS 7+ (High/Critical) vulnerabilities

### Dependabot Alerts
1. Go to GitHub repo → Security tab → Dependabot alerts
2. Review vulnerable dependencies
3. Merge auto-generated PRs or update manually

---

## Version History

| Date | Change |
|------|--------|
| 2026-01-19 | Extended branch protection to develop branch; documented strict PR workflow |
| 2026-01-19 | Enabled enforce_admins - all users including admins must use PRs |
| 2026-01-18 | Added JavaScript (Jest) unit tests to CI pipeline |
| 2026-01-18 | Restructured from plan to implementation doc, all sections updated |
| 2026-01-17 | Added OWASP Dependency Check, Dependabot, .NET analyzers |
| 2026-01-17 | Added CodeQL workflow, branch protection |
| 2026-01-17 | Initial plan document created |
