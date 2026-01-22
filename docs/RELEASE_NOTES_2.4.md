# Release Notes: Client-Side Instant Search

**Date:** 2026-01-22
**Version:** 2.4
**Branch:** develop

---

## Summary

Stock symbol search is now instant. Instead of round-tripping to the server for every keystroke, the browser loads all ~30,000 US stock symbols at page load and searches them locally in sub-millisecond time.

---

## What Changed

### For Users

- **Instant search results** - No more waiting 300-600ms for the server to respond. Results appear as you type.
- **Works offline** - Once the page loads, search works even if your connection drops.
- **Smarter fallback** - If you search for something not in our database (typos, new listings), the app waits 5 seconds then checks the server. This prevents unnecessary server calls while you're still typing.

### For Developers

- **New file: `symbolSearch.js`** - Client-side search module with prefix matching and ranking
- **Static symbol file** - `/data/symbols.txt` (~315KB gzipped) generated at startup
- **Auto-refresh** - Symbol file regenerates after daily Finnhub sync (2 AM UTC)
- **Graceful degradation** - Falls back to server API if client-side hasn't loaded yet

---

## Technical Details

| Metric | Before | After |
|--------|--------|-------|
| Search latency | 300-600ms | <1ms |
| Network calls per search | 1 | 0 |
| Initial page load | ~50KB | ~365KB (+315KB symbols) |
| Offline search | No | Yes |

### Architecture

```
Page Load → Fetch /data/symbols.txt (315KB gzip)
          → Parse into 30K symbol array
          → Ready for instant search

User Types → 300ms input debounce
           → Client-side SymbolSearch.search()
           → Instant results (or 5s server fallback)
```

### Files Changed

| File | Change |
|------|--------|
| `SymbolCache.cs` | Added `GenerateClientFile()` method |
| `SymbolRefreshService.cs` | Regenerates file after daily refresh |
| `Program.cs` | Generates file at startup |
| `symbolSearch.js` | New client-side search module |
| `api.js` | Client-first search with server fallback |
| `app.js` | 5-second debounced fallback handling |
| `index.html` | Added symbolSearch.js script |
| `.gitignore` | Ignore generated data files |
| `FUNCTIONAL_SPEC.md` | v2.4 with FR-001.8-14 |

---

## Testing Checklist

- [ ] Page load shows "Loaded ~30K symbols in Xms" in console
- [ ] Search for "AAPL" returns instant results (no network tab activity)
- [ ] Search for "Apple" finds AAPL by company name
- [ ] Search for nonsense (e.g., "XYZABC") shows "Checking server..." message
- [ ] After 5 seconds, server fallback fires
- [ ] Typing during 5-second wait resets the timer
- [ ] Disconnect network after page load - search still works

---

## Rollback

If issues arise, revert to server-side search:

1. Remove `<script src="js/symbolSearch.js">` from index.html
2. Revert `api.js` search() method to direct server call
3. Revert `app.js` performSearch() to remove fallback logic

The server-side `/api/search` endpoint remains fully functional.
