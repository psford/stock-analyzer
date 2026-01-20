/**
 * LocalStorage-based Watchlist Storage
 * Privacy-first approach: all data stored client-side, no PII collected
 */
const WatchlistStorage = {
    STORAGE_KEY: 'stockanalyzer_watchlists',
    VERSION: 1,

    /**
     * Get all watchlists from localStorage
     * @returns {Array} Array of watchlist objects
     */
    getAll() {
        try {
            const data = localStorage.getItem(this.STORAGE_KEY);
            if (!data) return [];

            const parsed = JSON.parse(data);
            // Handle version migration if needed
            if (parsed.version !== this.VERSION) {
                return this.migrate(parsed);
            }
            return parsed.watchlists || [];
        } catch (error) {
            console.error('Failed to load watchlists from localStorage:', error);
            return [];
        }
    },

    /**
     * Save all watchlists to localStorage
     * @param {Array} watchlists - Array of watchlist objects
     */
    saveAll(watchlists) {
        try {
            const data = {
                version: this.VERSION,
                lastUpdated: new Date().toISOString(),
                watchlists: watchlists
            };
            localStorage.setItem(this.STORAGE_KEY, JSON.stringify(data));
            return true;
        } catch (error) {
            console.error('Failed to save watchlists to localStorage:', error);
            // Check if quota exceeded
            if (error.name === 'QuotaExceededError') {
                alert('Storage quota exceeded. Please delete some watchlists.');
            }
            return false;
        }
    },

    /**
     * Create a new watchlist
     * @param {string} name - Watchlist name
     * @returns {Object} Created watchlist
     */
    create(name) {
        const watchlists = this.getAll();
        const newWatchlist = {
            id: this.generateId(),
            name: name,
            tickers: [],
            holdings: [],
            weightingMode: 'equal',
            createdAt: new Date().toISOString(),
            updatedAt: new Date().toISOString()
        };
        watchlists.push(newWatchlist);
        this.saveAll(watchlists);
        return newWatchlist;
    },

    /**
     * Get a watchlist by ID
     * @param {string} id - Watchlist ID
     * @returns {Object|null} Watchlist or null if not found
     */
    getById(id) {
        const watchlists = this.getAll();
        return watchlists.find(w => w.id === id) || null;
    },

    /**
     * Update a watchlist
     * @param {string} id - Watchlist ID
     * @param {Object} updates - Fields to update
     * @returns {Object|null} Updated watchlist or null
     */
    update(id, updates) {
        const watchlists = this.getAll();
        const index = watchlists.findIndex(w => w.id === id);
        if (index === -1) return null;

        watchlists[index] = {
            ...watchlists[index],
            ...updates,
            updatedAt: new Date().toISOString()
        };
        this.saveAll(watchlists);
        return watchlists[index];
    },

    /**
     * Delete a watchlist
     * @param {string} id - Watchlist ID
     * @returns {boolean} Success
     */
    delete(id) {
        const watchlists = this.getAll();
        const filtered = watchlists.filter(w => w.id !== id);
        if (filtered.length === watchlists.length) return false;
        this.saveAll(filtered);
        return true;
    },

    /**
     * Add a ticker to a watchlist
     * @param {string} watchlistId - Watchlist ID
     * @param {string} ticker - Ticker symbol
     * @returns {Object|null} Updated watchlist or null
     */
    addTicker(watchlistId, ticker) {
        const watchlist = this.getById(watchlistId);
        if (!watchlist) return null;

        const upperTicker = ticker.toUpperCase();
        if (watchlist.tickers.includes(upperTicker)) {
            return watchlist; // Already exists
        }

        watchlist.tickers.push(upperTicker);
        return this.update(watchlistId, { tickers: watchlist.tickers });
    },

    /**
     * Remove a ticker from a watchlist
     * @param {string} watchlistId - Watchlist ID
     * @param {string} ticker - Ticker symbol
     * @returns {Object|null} Updated watchlist or null
     */
    removeTicker(watchlistId, ticker) {
        const watchlist = this.getById(watchlistId);
        if (!watchlist) return null;

        const upperTicker = ticker.toUpperCase();
        watchlist.tickers = watchlist.tickers.filter(t => t !== upperTicker);
        // Also remove from holdings if present
        watchlist.holdings = (watchlist.holdings || []).filter(h => h.ticker !== upperTicker);

        return this.update(watchlistId, {
            tickers: watchlist.tickers,
            holdings: watchlist.holdings
        });
    },

    /**
     * Update holdings for a watchlist
     * @param {string} watchlistId - Watchlist ID
     * @param {string} weightingMode - "equal", "shares", or "dollars"
     * @param {Array} holdings - Array of {ticker, shares, dollarValue}
     * @returns {Object|null} Updated watchlist or null
     */
    updateHoldings(watchlistId, weightingMode, holdings) {
        return this.update(watchlistId, {
            weightingMode: weightingMode,
            holdings: holdings
        });
    },

    /**
     * Generate a unique ID
     * @returns {string} UUID-like ID
     */
    generateId() {
        return 'wl_' + Date.now().toString(36) + '_' + Math.random().toString(36).substr(2, 9);
    },

    /**
     * Migrate data from older versions
     * @param {Object} data - Old format data
     * @returns {Array} Migrated watchlists
     */
    migrate(data) {
        // For now, just return watchlists array if it exists
        // Add migration logic here as versions evolve
        console.log('Migrating watchlist data from version', data.version || 0, 'to', this.VERSION);
        return data.watchlists || [];
    },

    /**
     * Export watchlists to JSON file
     */
    exportToFile() {
        const data = {
            version: this.VERSION,
            exportedAt: new Date().toISOString(),
            watchlists: this.getAll()
        };

        const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);

        const a = document.createElement('a');
        a.href = url;
        a.download = `stockanalyzer-watchlists-${new Date().toISOString().split('T')[0]}.json`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    },

    /**
     * Import watchlists from JSON file
     * @param {File} file - JSON file to import
     * @returns {Promise<{success: boolean, imported: number, message: string}>}
     */
    async importFromFile(file) {
        return new Promise((resolve) => {
            const reader = new FileReader();

            reader.onload = (e) => {
                try {
                    const data = JSON.parse(e.target.result);

                    if (!data.watchlists || !Array.isArray(data.watchlists)) {
                        resolve({ success: false, imported: 0, message: 'Invalid file format' });
                        return;
                    }

                    const existing = this.getAll();
                    const existingIds = new Set(existing.map(w => w.id));

                    let imported = 0;
                    for (const watchlist of data.watchlists) {
                        // Generate new ID if collision
                        if (existingIds.has(watchlist.id)) {
                            watchlist.id = this.generateId();
                        }
                        // Validate required fields
                        if (watchlist.name && Array.isArray(watchlist.tickers)) {
                            existing.push({
                                id: watchlist.id || this.generateId(),
                                name: watchlist.name,
                                tickers: watchlist.tickers,
                                holdings: watchlist.holdings || [],
                                weightingMode: watchlist.weightingMode || 'equal',
                                createdAt: watchlist.createdAt || new Date().toISOString(),
                                updatedAt: new Date().toISOString()
                            });
                            imported++;
                        }
                    }

                    if (imported > 0) {
                        this.saveAll(existing);
                    }

                    resolve({
                        success: true,
                        imported,
                        message: `Imported ${imported} watchlist(s)`
                    });
                } catch (error) {
                    console.error('Import error:', error);
                    resolve({ success: false, imported: 0, message: 'Failed to parse file' });
                }
            };

            reader.onerror = () => {
                resolve({ success: false, imported: 0, message: 'Failed to read file' });
            };

            reader.readAsText(file);
        });
    },

    /**
     * Clear all watchlist data
     * @returns {boolean} Success
     */
    clearAll() {
        try {
            localStorage.removeItem(this.STORAGE_KEY);
            return true;
        } catch (error) {
            console.error('Failed to clear watchlists:', error);
            return false;
        }
    },

    /**
     * Get storage usage info
     * @returns {Object} {used: bytes, available: bytes, percentage: number}
     */
    getStorageInfo() {
        try {
            const data = localStorage.getItem(this.STORAGE_KEY) || '';
            const used = new Blob([data]).size;
            // localStorage typically has 5MB limit
            const total = 5 * 1024 * 1024;
            return {
                used,
                total,
                percentage: (used / total * 100).toFixed(2)
            };
        } catch {
            return { used: 0, total: 5 * 1024 * 1024, percentage: 0 };
        }
    }
};
