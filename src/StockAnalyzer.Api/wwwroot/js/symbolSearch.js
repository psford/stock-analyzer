/**
 * Client-Side Symbol Search
 * Loads symbol data once at startup for instant, offline-capable search.
 * File: /data/symbols.txt (~315KB gzipped)
 * Format: SYMBOL|Description\n (pipe-delimited)
 *
 * Search uses weighted relevance scoring:
 * - Exact ticker match: 1000 points
 * - Ticker starts with query: 200 points
 * - Description word starts with query: 100 points
 * - Description contains query (substring): 25 points
 * - Popularity boost: +10 to +50 for well-known tickers
 */
const SymbolSearch = {
    symbols: [],         // Array of {symbol, description}
    symbolMap: {},       // Map for O(1) exact match lookup
    isLoaded: false,
    isLoading: false,
    loadPromise: null,

    // Well-known tickers for relevance boost (static, no API calls)
    // Tiers: Mega-cap (+50), Indices/ETFs (+40), Large-cap (+30), Common (+10)
    popularTickers: {
        // Mega-cap / household names (+50)
        'AAPL': 50, 'MSFT': 50, 'GOOGL': 50, 'GOOG': 50, 'AMZN': 50, 'TSLA': 50,
        'META': 50, 'NVDA': 50, 'F': 50, 'GM': 50, 'T': 50, 'VZ': 50, 'BRK.A': 50,
        'BRK.B': 50, 'JNJ': 50, 'UNH': 50, 'V': 50, 'WMT': 50, 'JPM': 50, 'PG': 50,
        // Major indices & popular ETFs (+40)
        'SPY': 40, 'QQQ': 40, 'VOO': 40, 'VTI': 40, 'IWM': 40, 'DIA': 40, 'IVV': 40,
        '^DJI': 40, '^GSPC': 40, '^IXIC': 40, '^RUT': 40, 'VEA': 40, 'VWO': 40,
        'AGG': 40, 'BND': 40, 'GLD': 40, 'SLV': 40, 'USO': 40, 'TLT': 40, 'XLF': 40,
        'XLK': 40, 'XLE': 40, 'XLV': 40, 'XLI': 40, 'XLP': 40, 'XLY': 40, 'ARKK': 40,
        // Large-cap well-known (+30)
        'BAC': 30, 'WFC': 30, 'C': 30, 'GS': 30, 'MS': 30, 'AXP': 30,
        'COST': 30, 'TGT': 30, 'HD': 30, 'LOW': 30, 'NKE': 30,
        'XOM': 30, 'CVX': 30, 'COP': 30, 'SLB': 30, 'EOG': 30,
        'PFE': 30, 'MRK': 30, 'ABBV': 30, 'LLY': 30, 'BMY': 30, 'GILD': 30,
        'KO': 30, 'PEP': 30, 'MCD': 30, 'SBUX': 30, 'CMG': 30,
        'DIS': 30, 'NFLX': 30, 'CMCSA': 30, 'WBD': 30, 'PARA': 30,
        'BA': 30, 'CAT': 30, 'GE': 30, 'MMM': 30, 'RTX': 30, 'LMT': 30, 'HON': 30,
        'MA': 30, 'PYPL': 30, 'SQ': 30, 'COIN': 30,
        'INTC': 30, 'AMD': 30, 'QCOM': 30, 'AVGO': 30, 'TXN': 30, 'MU': 30,
        'CRM': 30, 'ORCL': 30, 'ADBE': 30, 'NOW': 30, 'SNOW': 30,
        'UBER': 30, 'LYFT': 30, 'ABNB': 30, 'DASH': 30,
        'RIVN': 30, 'LCID': 30, 'NIO': 30, 'LI': 30, 'XPEV': 30,
        'AMC': 30, 'GME': 30, 'PLTR': 30, 'SOFI': 30, 'HOOD': 30,
        // Common financials/utilities (+10)
        'USB': 10, 'PNC': 10, 'TFC': 10, 'SCHW': 10, 'BK': 10,
        'NEE': 10, 'DUK': 10, 'SO': 10, 'D': 10, 'AEP': 10,
        'SHOP': 10, 'MELI': 10, 'SE': 10, 'BABA': 10, 'JD': 10, 'PDD': 10,
        'TSM': 10, 'ASML': 10, 'SONY': 10, 'TM': 10, 'HMC': 10
    },

    /**
     * Load symbols from static file (called once at page load)
     * Returns a promise that resolves when symbols are loaded
     */
    async load() {
        if (this.isLoaded) return true;
        if (this.isLoading) return this.loadPromise;

        this.isLoading = true;
        this.loadPromise = this._doLoad();
        return this.loadPromise;
    },

    async _doLoad() {
        try {
            const startTime = performance.now();
            const response = await fetch('/data/symbols.txt');

            if (!response.ok) {
                console.warn('Symbol file not available, falling back to server search');
                this.isLoading = false;
                return false;
            }

            const text = await response.text();
            const lines = text.trim().split('\n');

            this.symbols = [];
            this.symbolMap = {};

            for (const line of lines) {
                const [symbol, description] = line.split('|');
                if (symbol) {
                    const entry = {
                        symbol: symbol.trim(),
                        description: description ? description.trim() : ''
                    };
                    this.symbols.push(entry);
                    this.symbolMap[entry.symbol] = entry;
                }
            }

            const loadTime = performance.now() - startTime;
            console.log(`Loaded ${this.symbols.length} symbols in ${loadTime.toFixed(1)}ms`);

            this.isLoaded = true;
            this.isLoading = false;
            return true;
        } catch (error) {
            console.warn('Failed to load symbol file:', error);
            this.isLoading = false;
            return false;
        }
    },

    /**
     * Calculate relevance score for a symbol match
     * @param {Object} entry - {symbol, description}
     * @param {string} lowerQuery - User's search query (lowercase)
     * @param {string} upperQuery - Query in uppercase
     * @returns {number} Relevance score (higher = better), 0 = no match
     */
    scoreMatch(entry, lowerQuery, upperQuery) {
        let score = 0;
        const symbol = entry.symbol;
        const descLower = entry.description.toLowerCase();

        // Exact ticker match (highest priority)
        if (symbol === upperQuery) {
            score = 1000;
        }
        // Ticker starts with query
        else if (symbol.startsWith(upperQuery)) {
            score = 200;
        }
        // Description word starts with query (word boundary match)
        // Use simple word boundary check: start of string or preceded by space/punctuation
        else if (this._wordStartsWith(entry.description, lowerQuery)) {
            score = 100;
        }
        // Description contains query anywhere (substring)
        else if (descLower.includes(lowerQuery)) {
            score = 25;
        }

        // Only add popularity boost if there's a base match
        if (score > 0) {
            score += this.popularTickers[symbol] || 0;
        }

        return score;
    },

    /**
     * Check if any word in text starts with the query
     * More efficient than regex for simple word boundary matching
     */
    _wordStartsWith(text, query) {
        const lower = text.toLowerCase();
        // Check start of string
        if (lower.startsWith(query)) return true;
        // Check after common word boundaries
        const boundaries = [' ', '-', '(', '/', '.', ',', "'"];
        for (const b of boundaries) {
            const idx = lower.indexOf(b + query);
            if (idx !== -1) return true;
        }
        return false;
    },

    /**
     * Search symbols by query with weighted relevance scoring
     * @param {string} query - Search query (ticker or company name)
     * @param {number} limit - Maximum results to return (default 10)
     * @returns {Array} Array of {symbol, shortName, longName, exchange, type}
     */
    search(query, limit = 10) {
        if (!this.isLoaded || !query) {
            return [];
        }

        const upperQuery = query.trim().toUpperCase();
        const lowerQuery = query.trim().toLowerCase();

        // Fast path: exact match still returns immediately
        if (this.symbolMap[upperQuery]) {
            const exact = this.symbolMap[upperQuery];
            return [{
                symbol: exact.symbol,
                shortName: exact.description,
                longName: exact.description,
                exchange: 'US',
                type: 'Common Stock',
                displayName: `${exact.symbol} - ${exact.description}`
            }];
        }

        // Collect and score all matches
        const matches = [];

        for (const entry of this.symbols) {
            // Quick rejection: skip if no possible match
            if (!entry.symbol.includes(upperQuery) &&
                !entry.description.toLowerCase().includes(lowerQuery)) {
                continue;
            }

            const score = this.scoreMatch(entry, lowerQuery, upperQuery);
            if (score > 0) {
                matches.push({ entry, score });
            }
        }

        // Sort by score descending, then alphabetically by symbol for ties
        matches.sort((a, b) => {
            if (b.score !== a.score) return b.score - a.score;
            return a.entry.symbol.localeCompare(b.entry.symbol);
        });

        // Take top results and format for display
        return matches.slice(0, limit).map(({ entry }) => ({
            symbol: entry.symbol,
            shortName: entry.description,
            longName: entry.description,
            exchange: 'US',
            type: 'Common Stock',
            displayName: `${entry.symbol} - ${entry.description}`
        }));
    },

    /**
     * Check if a symbol exists
     * @param {string} symbol - Ticker symbol to check
     * @returns {boolean}
     */
    exists(symbol) {
        if (!this.isLoaded) return false;
        return !!this.symbolMap[symbol.toUpperCase()];
    },

    /**
     * Get symbol info by exact match
     * @param {string} symbol - Ticker symbol
     * @returns {Object|null}
     */
    get(symbol) {
        if (!this.isLoaded) return null;
        const entry = this.symbolMap[symbol.toUpperCase()];
        if (!entry) return null;

        return {
            symbol: entry.symbol,
            shortName: entry.description,
            longName: entry.description,
            exchange: 'US',
            type: 'Common Stock'
        };
    }
};

// Auto-load symbols when script loads
SymbolSearch.load();
