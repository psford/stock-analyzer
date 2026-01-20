# .NET Security Tools Evaluation

## Overview

This document evaluates SAST (Static Application Security Testing) and DAST (Dynamic Application Security Testing) tools for our .NET 8.0 application, replacing/augmenting the Python-focused tools we used previously.

---

## Current State

| Tool | Type | Status | Notes |
|------|------|--------|-------|
| SecurityCodeScan.VS2019 | SAST | ✅ Installed | Runs during build, analyzes C# code |
| CodeQL | SAST | ✅ GitHub Actions | Weekly scans + PR checks |
| Bandit | Python SAST | ✅ Pre-commit | Only for Python helper scripts |
| detect-secrets | Secrets | ✅ Pre-commit | Blocks credential commits |

---

## Recommended Additions for .NET

### 1. SAST (Static Analysis)

#### Microsoft.CodeAnalysis.NetAnalyzers (Recommended)
- **Type:** Built-in .NET analyzers
- **Cost:** Free (included with .NET SDK 5+)
- **Coverage:** Security, reliability, performance, design
- **Integration:** NuGet package, runs during build
- **Why:** Official Microsoft analyzers with security rules (CA2xxx series)

```xml
<PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="9.0.0">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

#### Roslynator.Analyzers (Recommended)
- **Type:** Extended Roslyn analyzers
- **Cost:** Free
- **Coverage:** Code quality, some security patterns
- **Integration:** NuGet package
- **Why:** Catches additional code quality issues that can lead to vulnerabilities

```xml
<PackageReference Include="Roslynator.Analyzers" Version="4.12.10">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

#### SecurityCodeScan (Already Installed)
- **Type:** Security-focused SAST
- **Status:** Already in project
- **Coverage:** OWASP Top 10, SQL injection, XSS, CSRF, etc.

### 2. Dependency Scanning (SCA)

#### dotnet-outdated (Recommended)
- **Type:** Dependency version checker
- **Cost:** Free
- **Usage:** `dotnet tool install --global dotnet-outdated-tool`
- **Why:** Identifies outdated packages that may have security fixes

#### OWASP Dependency Check (Recommended)
- **Type:** Vulnerability database scanner
- **Cost:** Free
- **Integration:** CLI tool or GitHub Action
- **Why:** Checks NuGet packages against NVD (National Vulnerability Database)

```yaml
# GitHub Action
- name: OWASP Dependency Check
  uses: dependency-check/Dependency-Check_Action@main
  with:
    project: 'StockAnalyzer'
    path: 'stock_analyzer_dotnet'
    format: 'HTML'
```

#### GitHub Dependabot (Recommended)
- **Type:** Automated dependency updates
- **Cost:** Free
- **Integration:** GitHub native
- **Why:** Auto-creates PRs for vulnerable dependencies

### 3. DAST (Dynamic Analysis)

#### OWASP ZAP (Recommended for future)
- **Type:** Web application scanner
- **Cost:** Free
- **When:** After deployment to staging/production
- **Integration:** Can run in CI/CD against deployed app

```yaml
# Future: Add to CI/CD when we have deployed environment
- name: OWASP ZAP Scan
  uses: zaproxy/action-baseline@v0.10.0
  with:
    target: 'https://staging.stockanalyzer.example.com'
```

---

## Implementation Plan

### Phase 1: Add Build-Time Analyzers

1. Add Microsoft.CodeAnalysis.NetAnalyzers to all projects
2. Add Roslynator.Analyzers to all projects
3. Configure analyzer severity levels in `.editorconfig`
4. Fix any new warnings surfaced

### Phase 2: Add Dependency Scanning to CI/CD

1. Add OWASP Dependency Check to GitHub Actions
2. Enable GitHub Dependabot
3. Configure vulnerability thresholds (fail on high/critical)

### Phase 3: DAST (Future)

1. Deploy to staging environment
2. Add OWASP ZAP baseline scan
3. Configure authenticated scanning

---

## Analyzer Configuration (.editorconfig)

```ini
# Security rules - treat as errors
dotnet_diagnostic.CA2100.severity = error  # SQL injection
dotnet_diagnostic.CA2109.severity = error  # Review visible event handlers
dotnet_diagnostic.CA2119.severity = error  # Seal methods that satisfy private interfaces
dotnet_diagnostic.CA2153.severity = error  # Avoid handling Corrupted State Exceptions
dotnet_diagnostic.CA3001.severity = error  # SQL injection
dotnet_diagnostic.CA3002.severity = error  # XSS
dotnet_diagnostic.CA3003.severity = error  # File path injection
dotnet_diagnostic.CA3004.severity = error  # Information disclosure
dotnet_diagnostic.CA3006.severity = error  # Command injection
dotnet_diagnostic.CA3007.severity = error  # Open redirect
dotnet_diagnostic.CA3008.severity = error  # XPath injection
dotnet_diagnostic.CA3009.severity = error  # XML injection
dotnet_diagnostic.CA3010.severity = error  # XAML injection
dotnet_diagnostic.CA3011.severity = error  # DLL injection
dotnet_diagnostic.CA3012.severity = error  # Regex injection
dotnet_diagnostic.CA5350.severity = error  # Weak crypto
dotnet_diagnostic.CA5351.severity = error  # Broken crypto
dotnet_diagnostic.CA5358.severity = error  # Unsafe cipher mode
dotnet_diagnostic.CA5359.severity = error  # Disable certificate validation
dotnet_diagnostic.CA5360.severity = error  # Dangerous deserialization
dotnet_diagnostic.CA5361.severity = error  # SChannel disable strong crypto
dotnet_diagnostic.CA5362.severity = error  # Potential reference cycle
dotnet_diagnostic.CA5363.severity = error  # Request validation disabled
dotnet_diagnostic.CA5364.severity = error  # Deprecated security protocols
dotnet_diagnostic.CA5365.severity = error  # HTTP header checking disabled
dotnet_diagnostic.CA5366.severity = error  # XmlReader for dataset read
dotnet_diagnostic.CA5367.severity = error  # Pointer types serialization
dotnet_diagnostic.CA5368.severity = error  # ViewState MAC disabled
dotnet_diagnostic.CA5369.severity = error  # XmlReader for deserialize
dotnet_diagnostic.CA5370.severity = error  # XmlReader for validating reader
dotnet_diagnostic.CA5371.severity = error  # XmlReader for schema read
dotnet_diagnostic.CA5372.severity = error  # XmlReader for XPathDocument
dotnet_diagnostic.CA5373.severity = error  # Obsolete key derivation function
dotnet_diagnostic.CA5374.severity = error  # XslCompiledTransform
dotnet_diagnostic.CA5375.severity = error  # Account SAS
dotnet_diagnostic.CA5376.severity = error  # SharedAccessProtocol HttpsOnly
dotnet_diagnostic.CA5377.severity = error  # Container level access policy
dotnet_diagnostic.CA5378.severity = error  # ServicePointManager security protocols
dotnet_diagnostic.CA5379.severity = error  # Weak key derivation function
dotnet_diagnostic.CA5380.severity = error  # Root certificates to current user
dotnet_diagnostic.CA5381.severity = error  # Root certificates to machine store
dotnet_diagnostic.CA5382.severity = error  # Secure cookies
dotnet_diagnostic.CA5383.severity = error  # Secure cookies ASP.NET Core
dotnet_diagnostic.CA5384.severity = error  # DSA
dotnet_diagnostic.CA5385.severity = error  # RSA key size
dotnet_diagnostic.CA5386.severity = error  # Hardcoded security protocol
dotnet_diagnostic.CA5387.severity = error  # Weak key derivation function iteration count
dotnet_diagnostic.CA5388.severity = error  # Sufficient iteration count
dotnet_diagnostic.CA5389.severity = error  # Archive item path
dotnet_diagnostic.CA5390.severity = error  # Hard-code encryption key
dotnet_diagnostic.CA5391.severity = error  # Antiforgery token
dotnet_diagnostic.CA5392.severity = error  # DefaultDllImportSearchPaths
dotnet_diagnostic.CA5393.severity = error  # Unsafe DllImportSearchPath
dotnet_diagnostic.CA5394.severity = warning # Insecure randomness
dotnet_diagnostic.CA5395.severity = error  # HttpVerb attribute
dotnet_diagnostic.CA5396.severity = error  # HttpOnly for cookies
dotnet_diagnostic.CA5397.severity = error  # SslProtocols
dotnet_diagnostic.CA5398.severity = error  # Hardcoded SslProtocols
dotnet_diagnostic.CA5399.severity = error  # HttpClient certificate check
dotnet_diagnostic.CA5400.severity = error  # HttpClient certificate check (HttpClientHandler)
dotnet_diagnostic.CA5401.severity = error  # CreateEncryptor with non-default IV
dotnet_diagnostic.CA5402.severity = error  # CreateEncryptor with default IV
dotnet_diagnostic.CA5403.severity = error  # Hard-code certificate
```

---

## Tool Comparison Summary

| Tool | Type | Cost | Integration | Recommended |
|------|------|------|-------------|-------------|
| SecurityCodeScan | SAST | Free | Build-time | ✅ Keep |
| CodeQL | SAST | Free | GitHub Actions | ✅ Keep |
| NetAnalyzers | SAST | Free | Build-time | ✅ Add |
| Roslynator | SAST | Free | Build-time | ✅ Add |
| OWASP Dep Check | SCA | Free | CI/CD | ✅ Add |
| Dependabot | SCA | Free | GitHub | ✅ Enable |
| OWASP ZAP | DAST | Free | CI/CD | 📋 Future |
| Snyk | All | Freemium | CI/CD | ⏸️ Optional |
| SonarQube | Quality | Freemium | CI/CD | ⏸️ Optional |

---

## Version History

| Date | Change |
|------|--------|
| 01/17/2026 | Initial evaluation created |
