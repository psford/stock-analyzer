# Privacy & Security Policy

**Last Updated:** January 22, 2026

This policy explains what Stock Analyzer does and doesn't do with your data, written in plain English.

---

## The Short Version

- We don't track you
- We don't collect your personal information
- We don't sell data (we don't have any to sell)
- We don't use cookies for advertising
- Your watchlists are stored in your browser's localStorage - we never see them

---

## What We Collect

### Absolutely Nothing About You Personally

Stock Analyzer doesn't have user accounts, logins, or registration. We don't know who you are, and we don't want to.

### What Gets Stored (and Where)

| Data | Where | Who Can See It |
|------|-------|----------------|
| Watchlists | **Your browser's localStorage** | Only you. We never receive this data. If you clear your browser data or switch browsers, your watchlists are gone. |
| Dark mode preference | **Your browser's localStorage** | Only you |
| Cat/dog images | Our server's database | Just us (pre-cached for performance) |
| Stock symbols | Our server's database | Just us (~30,000 US tickers for fast search) |

**Important about watchlists:** Your watchlists exist only in your browser. We have no copy. If you want to keep them, use the Export feature to save a backup file. There's no "account" to recover them from.

That's it. No analytics. No tracking pixels. No user profiles.

### What We Don't Store

- Your IP address
- Your browser fingerprint
- Your search history
- Which stocks you looked at
- How long you spent on the site
- Where you came from
- Where you went after

---

## Third-Party Services

We use a few external services to make the app work:

| Service | What It Does | What They Might See |
|---------|--------------|---------------------|
| **Yahoo Finance** | Stock price data | Your IP address (when fetching prices) |
| **Finnhub** | Company news | Your IP address (when fetching news) |
| **Cataas** | Cat images | Your IP address (when loading images) |
| **Dog CEO** | Dog images | Your IP address (when loading images) |
| **Cloudflare** | Website security & speed | Your IP address (they protect our site from attacks) |

We don't control what these services do with the requests they receive. If that concerns you, consider using a VPN.

**We explicitly do NOT use:**
- Google Analytics
- Facebook Pixel
- Any social media tracking
- Any advertising networks
- Any data brokers

---

## Cookies

We use one cookie: a simple preference to remember if you chose dark mode. That's stored in your browser's localStorage, not on our servers.

We don't use:
- Tracking cookies
- Third-party cookies
- Session cookies that follow you across sites

---

## Security

### What We Do

- **Encryption**: All traffic uses HTTPS (TLS 1.2+)
- **Security headers**: We set strict security headers to prevent common attacks
- **Code scanning**: Our code is automatically scanned for vulnerabilities
- **Dependency monitoring**: We're alerted when our software dependencies have known security issues
- **No passwords**: Since there are no accounts, there are no passwords to steal

### What Could Go Wrong

Let's be honest about limitations:

- **Watchlists aren't private**: Anyone who can guess or find your watchlist ID can see it. Don't put sensitive information in watchlist names.
- **No authentication**: We can't verify who you are, so we can't restrict access to "your" data.
- **Third-party risks**: If Yahoo Finance, Finnhub, or our image providers are compromised, that could affect our app.

---

## Future Plans

We're considering adding anonymous search telemetry to improve search results (so "ford" finds Ford Motor Company, not obscure matches). If we do:

- Data will be anonymous and aggregated
- Individual searches won't be stored
- We'll update this policy before implementing

---

## Your Rights

Since we don't collect personal data, there's nothing to:
- Request a copy of
- Ask us to delete
- Ask us to correct

Your watchlists live in your browser. To delete them, either:
- Use the app's delete feature, or
- Clear your browser's localStorage for this site

---

## Changes to This Policy

If we change this policy, we'll update the date at the top. For significant changes, we'll note them here:

| Date | Change |
|------|--------|
| 2026-01-22 | Initial policy |

---

## Contact

Questions? Open an issue on [GitHub](https://github.com/psford/claudeProjects).

---

## The Legal Bit

This policy applies to Stock Analyzer at psfordtaurus.com. We're not lawyers, and this isn't legal advice. We're just trying to be transparent about how a hobby project handles (or rather, doesn't handle) your data.
