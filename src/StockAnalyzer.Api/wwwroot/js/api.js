/**
 * Stock Analyzer API Client
 * Handles all API calls to the .NET backend
 */
const API = {
    baseUrl: '/api',

    /**
     * Get stock information
     * @param {string} ticker - Stock ticker symbol
     */
    async getStockInfo(ticker) {
        const response = await fetch(`${this.baseUrl}/stock/${ticker}`);
        if (!response.ok) {
            throw new Error(`Stock not found: ${ticker}`);
        }
        return response.json();
    },

    /**
     * Get historical price data
     * @param {string} ticker - Stock ticker symbol
     * @param {string} period - Time period (1mo, 3mo, 6mo, 1y, 2y, 5y)
     */
    async getHistory(ticker, period = '1y') {
        const response = await fetch(`${this.baseUrl}/stock/${ticker}/history?period=${period}`);
        if (!response.ok) {
            throw new Error('Failed to fetch historical data');
        }
        return response.json();
    },

    /**
     * Get stock analysis (performance metrics + moving averages)
     * @param {string} ticker - Stock ticker symbol
     * @param {string} period - Time period
     */
    async getAnalysis(ticker, period = '1y') {
        const response = await fetch(`${this.baseUrl}/stock/${ticker}/analysis?period=${period}`);
        if (!response.ok) {
            throw new Error('Failed to fetch analysis data');
        }
        return response.json();
    },

    /**
     * Get significant price moves
     * @param {string} ticker - Stock ticker symbol
     * @param {number} threshold - Minimum percentage change to consider significant
     */
    async getSignificantMoves(ticker, threshold = 3) {
        const response = await fetch(`${this.baseUrl}/stock/${ticker}/significant?threshold=${threshold}`);
        if (!response.ok) {
            throw new Error('Failed to fetch significant moves');
        }
        return response.json();
    },

    /**
     * Get company news
     * @param {string} ticker - Stock ticker symbol
     * @param {number} days - Number of days of news to fetch
     */
    async getNews(ticker, days = 30) {
        const response = await fetch(`${this.baseUrl}/stock/${ticker}/news?days=${days}`);
        if (!response.ok) {
            throw new Error('Failed to fetch news');
        }
        return response.json();
    },

    /**
     * Search for tickers
     * @param {string} query - Search query
     */
    async search(query) {
        const response = await fetch(`${this.baseUrl}/search?q=${encodeURIComponent(query)}`);
        if (!response.ok) {
            throw new Error('Search failed');
        }
        return response.json();
    },

    /**
     * Get trending stocks
     * @param {number} count - Number of stocks to fetch
     */
    async getTrending(count = 10) {
        const response = await fetch(`${this.baseUrl}/trending?count=${count}`);
        if (!response.ok) {
            throw new Error('Failed to fetch trending stocks');
        }
        return response.json();
    },

    /**
     * Health check
     */
    async healthCheck() {
        // Health endpoint is at /health, not /api/health
        const response = await fetch('/health');
        return response.json();
    },

    // ============================================
    // Watchlist Methods (LocalStorage-based)
    // Privacy-first: all watchlist data stored client-side
    // ============================================

    /**
     * Get all watchlists from localStorage
     */
    async getWatchlists() {
        return WatchlistStorage.getAll();
    },

    /**
     * Create a new watchlist
     * @param {string} name - Watchlist name
     */
    async createWatchlist(name) {
        return WatchlistStorage.create(name);
    },

    /**
     * Get a watchlist by ID
     * @param {string} id - Watchlist ID
     */
    async getWatchlist(id) {
        const watchlist = WatchlistStorage.getById(id);
        if (!watchlist) {
            throw new Error('Watchlist not found');
        }
        return watchlist;
    },

    /**
     * Rename a watchlist
     * @param {string} id - Watchlist ID
     * @param {string} name - New name
     */
    async renameWatchlist(id, name) {
        const updated = WatchlistStorage.update(id, { name });
        if (!updated) {
            throw new Error('Failed to rename watchlist');
        }
        return updated;
    },

    /**
     * Delete a watchlist
     * @param {string} id - Watchlist ID
     */
    async deleteWatchlist(id) {
        const success = WatchlistStorage.delete(id);
        if (!success) {
            throw new Error('Failed to delete watchlist');
        }
        return true;
    },

    /**
     * Add a ticker to a watchlist
     * @param {string} id - Watchlist ID
     * @param {string} ticker - Ticker symbol
     */
    async addTickerToWatchlist(id, ticker) {
        const updated = WatchlistStorage.addTicker(id, ticker);
        if (!updated) {
            throw new Error('Failed to add ticker to watchlist');
        }
        return updated;
    },

    /**
     * Remove a ticker from a watchlist
     * @param {string} id - Watchlist ID
     * @param {string} ticker - Ticker symbol
     */
    async removeTickerFromWatchlist(id, ticker) {
        const updated = WatchlistStorage.removeTicker(id, ticker);
        if (!updated) {
            throw new Error('Failed to remove ticker from watchlist');
        }
        return updated;
    },

    /**
     * Get quotes for all tickers in a watchlist
     * Fetches current prices from the server for tickers stored locally
     * @param {string} id - Watchlist ID
     */
    async getWatchlistQuotes(id) {
        const watchlist = WatchlistStorage.getById(id);
        if (!watchlist) {
            throw new Error('Watchlist not found');
        }

        // Fetch quotes from server for each ticker
        const quotes = [];
        for (const ticker of watchlist.tickers) {
            try {
                const stockInfo = await this.getStockInfo(ticker);
                quotes.push({
                    symbol: ticker,
                    price: stockInfo.currentPrice,
                    change: stockInfo.dayChange,
                    changePercent: stockInfo.dayChangePercent
                });
            } catch (error) {
                console.warn(`Failed to fetch quote for ${ticker}:`, error);
                quotes.push({
                    symbol: ticker,
                    price: null,
                    change: null,
                    changePercent: null,
                    error: true
                });
            }
        }

        return {
            watchlistId: id,
            watchlistName: watchlist.name,
            quotes
        };
    },

    /**
     * Update holdings for a watchlist
     * @param {string} id - Watchlist ID
     * @param {string} weightingMode - "equal", "shares", or "dollars"
     * @param {Array} holdings - Array of {ticker, shares, dollarValue}
     */
    async updateWatchlistHoldings(id, weightingMode, holdings) {
        const updated = WatchlistStorage.updateHoldings(id, weightingMode, holdings);
        if (!updated) {
            throw new Error('Failed to update holdings');
        }
        return updated;
    },

    /**
     * Get combined portfolio performance for a watchlist
     * Uses server for historical data but reads watchlist from localStorage
     * @param {string} id - Watchlist ID
     * @param {string} period - Time period (1mo, 3mo, 6mo, 1y, 2y)
     * @param {string} benchmark - Optional benchmark ticker (SPY, QQQ)
     */
    async getCombinedPortfolio(id, period = '1y', benchmark = null) {
        const watchlist = WatchlistStorage.getById(id);
        if (!watchlist || watchlist.tickers.length === 0) {
            throw new Error('Watchlist not found or empty');
        }

        // Fetch historical data for each ticker from the server
        const tickerData = {};
        for (const ticker of watchlist.tickers) {
            try {
                const history = await this.getHistory(ticker, period);
                tickerData[ticker] = history;
            } catch (error) {
                console.warn(`Failed to fetch history for ${ticker}:`, error);
            }
        }

        // Calculate weights based on mode
        const weights = this.calculateWeights(watchlist, Object.keys(tickerData));

        // Aggregate portfolio performance
        const portfolioData = this.aggregatePortfolioData(tickerData, weights);

        // Fetch benchmark if requested
        let benchmarkData = null;
        if (benchmark) {
            try {
                const benchmarkHistory = await this.getHistory(benchmark, period);
                benchmarkData = this.normalizeToPercentChange(benchmarkHistory);
            } catch (error) {
                console.warn(`Failed to fetch benchmark ${benchmark}:`, error);
            }
        }

        // Find significant moves (±5%)
        const significantMoves = this.findSignificantMoves(portfolioData, 5);

        return {
            watchlistId: id,
            watchlistName: watchlist.name,
            period,
            weightingMode: watchlist.weightingMode || 'equal',
            tickerWeights: weights,
            totalReturn: portfolioData.length > 0 ? portfolioData[portfolioData.length - 1].percentChange : 0,
            data: portfolioData,
            benchmarkSymbol: benchmark,
            benchmarkData,
            significantMoves
        };
    },

    /**
     * Calculate ticker weights based on watchlist settings
     */
    calculateWeights(watchlist, availableTickers) {
        const mode = watchlist.weightingMode || 'equal';
        const holdings = watchlist.holdings || [];
        const weights = {};

        if (mode === 'equal' || availableTickers.length === 0) {
            const weight = 100 / availableTickers.length;
            availableTickers.forEach(t => weights[t] = weight);
        } else if (mode === 'shares') {
            let totalShares = 0;
            holdings.forEach(h => {
                if (availableTickers.includes(h.ticker)) {
                    totalShares += (h.shares || 0);
                }
            });
            if (totalShares > 0) {
                holdings.forEach(h => {
                    if (availableTickers.includes(h.ticker)) {
                        weights[h.ticker] = ((h.shares || 0) / totalShares) * 100;
                    }
                });
            }
            // Fill missing with equal
            availableTickers.forEach(t => {
                if (!(t in weights)) weights[t] = 0;
            });
        } else if (mode === 'dollars') {
            let totalDollars = 0;
            holdings.forEach(h => {
                if (availableTickers.includes(h.ticker)) {
                    totalDollars += (h.dollarValue || 0);
                }
            });
            if (totalDollars > 0) {
                holdings.forEach(h => {
                    if (availableTickers.includes(h.ticker)) {
                        weights[h.ticker] = ((h.dollarValue || 0) / totalDollars) * 100;
                    }
                });
            }
            availableTickers.forEach(t => {
                if (!(t in weights)) weights[t] = 0;
            });
        }

        return weights;
    },

    /**
     * Aggregate portfolio data from multiple tickers
     */
    aggregatePortfolioData(tickerData, weights) {
        // Get all dates from all tickers
        // Note: API returns 'data' array, not 'prices'
        const allDates = new Set();
        Object.values(tickerData).forEach(historyResponse => {
            const prices = historyResponse.data || historyResponse.prices || [];
            prices.forEach(p => allDates.add(p.date));
        });

        const sortedDates = Array.from(allDates).sort();
        if (sortedDates.length === 0) return [];

        // Normalize each ticker to percent change from start
        const normalizedData = {};
        Object.entries(tickerData).forEach(([ticker, historyResponse]) => {
            const prices = historyResponse.data || historyResponse.prices || [];
            if (prices.length > 0) {
                const firstPrice = prices[0].close;
                normalizedData[ticker] = {};
                prices.forEach(p => {
                    normalizedData[ticker][p.date] = ((p.close - firstPrice) / firstPrice) * 100;
                });
            }
        });

        // Calculate weighted portfolio return for each date
        return sortedDates.map(date => {
            let weightedReturn = 0;
            let totalWeight = 0;

            Object.entries(weights).forEach(([ticker, weight]) => {
                if (normalizedData[ticker] && normalizedData[ticker][date] !== undefined) {
                    weightedReturn += normalizedData[ticker][date] * (weight / 100);
                    totalWeight += weight;
                }
            });

            return {
                date,
                percentChange: totalWeight > 0 ? weightedReturn : 0
            };
        });
    },

    /**
     * Normalize history data to percent change
     */
    normalizeToPercentChange(historyData) {
        // Note: API returns 'data' array, not 'prices'
        const prices = historyData.data || historyData.prices || [];
        if (prices.length === 0) return [];

        const firstPrice = prices[0].close;
        return prices.map(p => ({
            date: p.date,
            percentChange: ((p.close - firstPrice) / firstPrice) * 100
        }));
    },

    /**
     * Find significant moves in portfolio data
     */
    findSignificantMoves(portfolioData, threshold) {
        const moves = [];
        for (let i = 1; i < portfolioData.length; i++) {
            const dayChange = portfolioData[i].percentChange - portfolioData[i - 1].percentChange;
            if (Math.abs(dayChange) >= threshold) {
                moves.push({
                    date: portfolioData[i].date,
                    percentChange: dayChange,
                    isPositive: dayChange >= 0
                });
            }
        }
        return moves;
    },

    /**
     * Export watchlists to JSON file
     */
    exportWatchlists() {
        WatchlistStorage.exportToFile();
    },

    /**
     * Import watchlists from JSON file
     * @param {File} file - JSON file to import
     */
    async importWatchlists(file) {
        return WatchlistStorage.importFromFile(file);
    },

    /**
     * Get general market news
     * @param {string} category - News category: general, forex, crypto, merger
     */
    async getMarketNews(category = 'general') {
        const response = await fetch(`${this.baseUrl}/news/market?category=${category}`);
        if (!response.ok) {
            throw new Error('Failed to fetch market news');
        }
        return response.json();
    }
};
