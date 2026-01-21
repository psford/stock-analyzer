# Security Overview

**Document Classification:** Internal
**Last Updated:** 2026-01-21
**Version:** 1.2

This document provides an executive summary of security controls implemented in the Stock Analyzer application, intended for security leadership and compliance review.

---

## Executive Summary

Stock Analyzer is a financial data visualization application deployed on Microsoft Azure. The application implements defense-in-depth security controls across the software development lifecycle (SDLC), including:

- **Static Application Security Testing (SAST)** at build time
- **Software Composition Analysis (SCA)** for dependency vulnerabilities
- **Secrets management** with pre-commit scanning
- **Infrastructure security** via Azure and Cloudflare
- **Runtime protections** including security headers and TLS encryption

The security posture follows OWASP guidelines and leverages industry-standard tooling.

---

## Risk Profile

| Attribute | Value |
|-----------|-------|
| **Data Classification** | Public financial data (no PII) |
| **Authentication** | None (public read-only dashboard) |
| **Attack Surface** | Web application (HTTPS only) |
| **Deployment Model** | Containerized (Azure App Service B1) |
| **Third-Party Integrations** | Yahoo Finance API, Finnhub API (read-only) |

### Data Handled

| Data Type | Source | Sensitivity |
|-----------|--------|-------------|
| Stock prices | Yahoo Finance | Public |
| Company news | Finnhub | Public |
| Watchlist preferences | User-created | Low (no authentication) |

**Note:** The application does not process, store, or transmit personally identifiable information (PII), financial transactions, or authentication credentials for end users.

---

## Security Controls by Phase

### 1. Development Phase

#### 1.1 Static Application Security Testing (SAST)

Multiple SAST tools run during the build process to identify vulnerabilities before deployment.

| Tool | Coverage | Integration |
|------|----------|-------------|
| **CodeQL** | SQL injection, XSS, command injection, 100+ security rules | GitHub Actions (weekly + PR) |
| **SecurityCodeScan** | OWASP Top 10 for C# | Build-time analyzer |
| **Microsoft.CodeAnalysis.NetAnalyzers** | Security rules (CA2xxx, CA3xxx, CA5xxx series) | Build-time analyzer |
| **Roslynator.Analyzers** | Code quality patterns that may indicate vulnerabilities | Build-time analyzer |
| **Bandit** | Python SAST for helper scripts | Pre-commit hook |

**Configuration:** Security analyzer rules (CA5xxx series) are configured as build errors, not warnings. Builds fail if security issues are detected.

#### 1.2 Secrets Management

| Control | Implementation |
|---------|----------------|
| **Pre-commit scanning** | `detect-secrets` blocks commits containing API keys, passwords, or tokens |
| **Private key detection** | `detect-private-key` blocks cryptographic key files |
| **Secrets baseline** | Known false positives tracked in `.secrets.baseline` |
| **Azure Key Vault** | Production secrets (SQL connection, Finnhub API key) stored in Key Vault with App Service managed identity access |
| **GitHub Secrets** | CI/CD credentials (Azure service principal, ACR password) stored in GitHub Secrets |
| **No secrets in code** | API keys loaded from environment variables or Key Vault references only |

#### 1.3 Software Composition Analysis (SCA)

Dependency vulnerabilities are monitored through multiple channels.

| Tool | Schedule | Action |
|------|----------|--------|
| **OWASP Dependency Check** | Every push/PR | Fails build on CVSS 7+ (High/Critical) |
| **GitHub Dependabot** | Weekly scan | Auto-creates PRs for vulnerable packages |
| **Dependabot Security Alerts** | Continuous | Email notifications for new CVEs |

**Ecosystem Coverage:**
- NuGet packages (.NET dependencies)
- pip packages (Python helper scripts)
- GitHub Actions (CI/CD dependencies)

---

### 2. CI/CD Pipeline Security

#### 2.1 GitHub Actions Workflow

```
Developer Push → Pre-flight Checks → Build & Test → Security Scans → Deploy
                       ↓                    ↓              ↓
                 Credential test     OWASP Dep Check   CodeQL SAST
                 (Azure, ACR)              ↓
                                    Fail on High/Critical
```

#### 2.2 Branch Protection

Both `develop` and `master` branches are protected. All changes must follow:

```
feature/X → PR to develop → (approval) → PR to master → Production
```

| Rule | Configuration |
|------|---------------|
| **Protected branches** | Both `develop` and `master` |
| **Pull request required** | All changes must go through PR (no direct commits) |
| **Enforce for admins** | Enabled on both branches - no bypass allowed |
| **Status checks** | `build-and-test` must pass before merge |
| **Dismiss stale reviews** | Enabled - new commits invalidate approvals |
| **Force push** | Disabled on protected branches |
| **Signed commits** | Recommended (not enforced) |

#### 2.3 Deployment Controls

| Control | Implementation |
|---------|----------------|
| **Manual approval** | Production deploys require explicit confirmation |
| **Credential pre-flight** | Azure credentials tested before expensive build steps |
| **Health verification** | Automated health check after deployment |
| **Rollback capability** | Previous container images tagged for quick rollback |

---

### 3. Runtime Security

#### 3.1 Infrastructure

| Component | Security Feature |
|-----------|------------------|
| **Azure App Service** | Managed container runtime, Always On, HTTPS Only enforced |
| **Azure Key Vault** | Secrets management with managed identity access, RBAC |
| **Azure SQL Database** | Encrypted at rest, Azure-managed firewall |
| **Azure Container Registry** | Private registry, authenticated pulls only |
| **Cloudflare** | DDoS protection, WAF (basic rules), Full (strict) SSL |

#### 3.2 Network Security

| Layer | Protection |
|-------|------------|
| **Edge (Cloudflare)** | TLS 1.2+ encryption, automatic certificate renewal |
| **Origin (Azure)** | Proxied through Cloudflare, no direct exposure |
| **Custom Domains** | Azure-managed SSL certificates (auto-renewing) |
| **Database** | Azure Services firewall rule, no public endpoint |

**Custom Domain Configuration:**

| Domain | SSL Certificate | Issuer |
|--------|-----------------|--------|
| psfordtaurus.com | Cloudflare (via proxy) | Cloudflare |
| psfordtest.com | Azure Managed Certificate | GeoTrust |
| www.psfordtest.com | Azure Managed Certificate | GeoTrust |

Azure managed certificates auto-renew approximately 30 days before expiration. No manual certificate management required.

#### 3.3 Application Security Headers

The following security headers are enforced on all responses:

| Header | Value | Purpose |
|--------|-------|---------|
| `Content-Security-Policy` | Restrictive policy | Mitigates XSS attacks |
| `X-Content-Type-Options` | `nosniff` | Prevents MIME sniffing |
| `X-Frame-Options` | `DENY` | Prevents clickjacking |
| `X-XSS-Protection` | `1; mode=block` | Legacy XSS filter |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Controls referrer leakage |
| `Permissions-Policy` | Restrictive | Limits browser features |

#### 3.4 Content Security Policy

```
default-src 'self';
script-src 'self' https://cdn.tailwindcss.com https://cdn.plot.ly https://cdn.jsdelivr.net 'unsafe-inline';
style-src 'self' 'unsafe-inline';
img-src 'self' blob: data: https://cataas.com https://images.dog.ceo;
connect-src 'self' https://dog.ceo;
font-src 'self';
object-src 'none';
base-uri 'self';
form-action 'self';
frame-ancestors 'none';
```

#### 3.5 Subresource Integrity (SRI)

External CDN scripts include integrity hashes to detect tampering:

```html
<script src="https://cdn.plot.ly/plotly-2.27.0.min.js"
        integrity="sha384-..."
        crossorigin="anonymous"></script>
```

---

## Compliance Mapping

### OWASP Top 10 (2021) Coverage

| Risk | Mitigation |
|------|------------|
| **A01: Broken Access Control** | No authentication required (public data only) |
| **A02: Cryptographic Failures** | TLS enforced, no sensitive data stored |
| **A03: Injection** | CodeQL + SecurityCodeScan detect SQL/command injection |
| **A04: Insecure Design** | Security review integrated into PR process |
| **A05: Security Misconfiguration** | Security headers enforced, CSP configured |
| **A06: Vulnerable Components** | OWASP Dependency Check + Dependabot |
| **A07: Auth Failures** | N/A (no authentication) |
| **A08: Data Integrity Failures** | SRI for CDN resources, signed commits encouraged |
| **A09: Logging Failures** | Structured logging with Serilog, excludes sensitive data |
| **A10: SSRF** | Limited outbound connections to known APIs only |

---

## Incident Response

### Security Contact

For security concerns, contact the repository owner through GitHub.

### Vulnerability Disclosure

1. **Private disclosure preferred** via GitHub Security Advisories
2. Acknowledge within 48 hours
3. Provide fix timeline within 7 days
4. Coordinate public disclosure after fix deployed

### Monitoring

| Signal | Source | Alert |
|--------|--------|-------|
| **Build failures** | GitHub Actions | Email notification |
| **Security alerts** | Dependabot, CodeQL | GitHub notifications |
| **Health check failures** | Deployment workflow | Workflow failure |
| **Runtime errors** | Serilog logs | Manual review (Application Insights planned) |

---

## Security Roadmap

### Implemented (Current State)

- [x] SAST (CodeQL, SecurityCodeScan, .NET Analyzers)
- [x] SCA (OWASP Dependency Check, Dependabot)
- [x] Pre-commit secrets scanning
- [x] Security headers and CSP
- [x] TLS encryption via Cloudflare (Full strict mode)
- [x] Branch protection and PR reviews
- [x] Azure Key Vault for secrets (SQL connection, Finnhub API key)
- [x] App Service with HTTPS Only enforcement

### Planned Enhancements

| Enhancement | Priority | Status |
|-------------|----------|--------|
| Azure Application Insights | Medium | Planned |
| VNet + Private Endpoints for SQL | Medium | Planned |
| Container image scanning | Low | Planned |
| DAST (OWASP ZAP) in staging | Low | Planned |

---

## Appendix: Tool Versions

| Tool | Version | License |
|------|---------|---------|
| .NET SDK | 8.0.x | MIT |
| CodeQL | v4 | MIT |
| SecurityCodeScan | 5.x | LGPL |
| OWASP Dependency Check | Latest | Apache 2.0 |
| Cloudflare | Free tier | Commercial |

---

## Document History

| Date | Version | Change |
|------|---------|--------|
| 2026-01-21 | 1.2 | Added custom domain configuration (psfordtest.com) with Azure managed SSL |
| 2026-01-19 | 1.1 | Updated for App Service migration, Azure Key Vault implementation |
| 2026-01-18 | 1.0 | Initial document |
