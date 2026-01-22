/**
 * Client-Side Symbol Search
 * Loads symbol data once at startup for instant, offline-capable search.
 * File: /data/symbols.txt (~315KB gzipped)
 * Format: SYMBOL|Description\n (pipe-delimited)
 */
const SymbolSearch = {
    symbols: [],         // Array of {symbol, description}
    symbolMap: {},       // Map for O(1) exact match lookup
    isLoaded: false,
    isLoading: false,
    loadPromise: null,

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
     * Search symbols by query
     * @param {string} query - Search query (ticker or company name)
     * @param {number} limit - Maximum results to return (default 10)
     * @returns {Array} Array of {symbol, shortName, longName, exchange, type}
     */
    search(query, limit = 10) {
        if (!this.isLoaded || !query) {
            return [];
        }

        const normalizedQuery = query.trim().toUpperCase();
        const results = [];

        // Fast path: exact match
        if (this.symbolMap[normalizedQuery]) {
            const exact = this.symbolMap[normalizedQuery];
            results.push({
                symbol: exact.symbol,
                shortName: exact.description,
                longName: exact.description,
                exchange: 'US',
                type: 'Common Stock',
                displayName: `${exact.symbol} - ${exact.description}`
            });
            return results;
        }

        // Search with ranking: symbol prefix matches first, then description matches
        const prefixMatches = [];
        const descriptionMatches = [];
        const lowerQuery = query.toLowerCase();

        for (const entry of this.symbols) {
            if (entry.symbol.startsWith(normalizedQuery)) {
                prefixMatches.push(entry);
            } else if (entry.description.toLowerCase().includes(lowerQuery)) {
                descriptionMatches.push(entry);
            }

            // Early exit if we have enough prefix matches
            if (prefixMatches.length >= limit) break;
        }

        // Combine results: prefix matches first, then description matches
        const combined = [...prefixMatches, ...descriptionMatches].slice(0, limit);

        return combined.map(entry => ({
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
