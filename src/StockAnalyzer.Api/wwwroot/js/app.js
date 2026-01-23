/**
 * Stock Analyzer Application
 * Main application logic
 */
const App = {
    currentTicker: null,
    currentPeriod: '1y',
    currentThreshold: 5,
    currentAnimal: 'cats',
    historyData: null,
    analysisData: null,
    significantMovesData: null,
    searchTimeout: null,
    serverFallbackTimeout: null,  // 5-second debounce for server fallback on empty results
    hoverTimeout: null,
    hideTimeout: null,
    isHoverCardHovered: false,

    // Cache for pre-fetched news (keyed by "TICKER:YYYY-MM-DD:up/down")
    newsCache: {},

    // Comparison feature state
    comparisonTicker: null,
    comparisonHistoryData: null,
    compareSearchTimeout: null,

    // Image cache for pre-loaded animal images
    imageCache: {
        cats: [],
        dogs: [],
        isRefilling: { cats: false, dogs: false }
    },
    IMAGE_CACHE_SIZE: 50,
    IMAGE_CACHE_THRESHOLD: 10,

    /**
     * Initialize the application
     */
    init() {
        this.initDarkMode();
        this.initMobileSidebar();
        this.bindEvents();
        this.checkApiHealth();
        this.prefetchImages();
    },

    /**
     * Initialize mobile sidebar toggle functionality
     */
    initMobileSidebar() {
        const toggleBtn = document.getElementById('mobile-watchlist-toggle');
        const sidebar = document.getElementById('watchlist-sidebar');
        const overlay = document.getElementById('sidebar-overlay');

        if (!toggleBtn || !sidebar || !overlay) return;

        const openSidebar = () => {
            sidebar.classList.remove('translate-x-full');
            sidebar.classList.add('translate-x-0');
            overlay.classList.remove('hidden');
            document.body.classList.add('overflow-hidden', 'lg:overflow-auto');
        };

        const closeSidebar = () => {
            sidebar.classList.add('translate-x-full');
            sidebar.classList.remove('translate-x-0');
            overlay.classList.add('hidden');
            document.body.classList.remove('overflow-hidden');
        };

        toggleBtn.addEventListener('click', () => {
            if (sidebar.classList.contains('translate-x-full')) {
                openSidebar();
            } else {
                closeSidebar();
            }
        });

        overlay.addEventListener('click', closeSidebar);

        // Close sidebar on escape key
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && !sidebar.classList.contains('translate-x-full')) {
                closeSidebar();
            }
        });
    },

    /**
     * Initialize dark mode from localStorage preference
     */
    initDarkMode() {
        const darkModeToggle = document.getElementById('dark-mode-toggle');
        const sunIcon = document.getElementById('sun-icon');
        const moonIcon = document.getElementById('moon-icon');

        // Check for saved preference or system preference
        const savedPreference = localStorage.getItem('darkMode');
        const systemPrefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;

        const isDark = savedPreference === 'true' || (savedPreference === null && systemPrefersDark);

        // Apply initial state
        if (isDark) {
            document.documentElement.classList.add('dark');
            sunIcon.classList.remove('hidden');
            moonIcon.classList.add('hidden');
        } else {
            document.documentElement.classList.remove('dark');
            sunIcon.classList.add('hidden');
            moonIcon.classList.remove('hidden');
        }

        // Toggle handler
        darkModeToggle.addEventListener('click', () => {
            const isDarkNow = document.documentElement.classList.toggle('dark');
            localStorage.setItem('darkMode', isDarkNow);

            if (isDarkNow) {
                sunIcon.classList.remove('hidden');
                moonIcon.classList.add('hidden');
            } else {
                sunIcon.classList.add('hidden');
                moonIcon.classList.remove('hidden');
            }

            // Update Plotly chart colors if chart exists
            this.updateChartTheme();
        });
    },

    /**
     * Update chart theme when dark mode changes
     */
    updateChartTheme() {
        const chartEl = document.getElementById('stock-chart');
        if (chartEl && chartEl.data) {
            this.renderChart();
        }
    },

    /**
     * Prefetch animal images on page load.
     * Images are processed server-side with ML detection for better cropping.
     */
    async prefetchImages() {
        console.log('Prefetching animal images from backend...');
        // Backend cache fills in background - just start the frontend cache fill
        await Promise.all([
            this.fetchImagesFromBackend('dogs', this.IMAGE_CACHE_SIZE),
            this.fetchImagesFromBackend('cats', this.IMAGE_CACHE_SIZE)
        ]);
        console.log(`Image cache ready: ${this.imageCache.dogs.length} dogs, ${this.imageCache.cats.length} cats`);
    },

    /**
     * Fetch processed images from backend API.
     * Backend handles ML-based detection and cropping for optimal thumbnails.
     */
    async fetchImagesFromBackend(type, count) {
        if (this.imageCache.isRefilling[type]) return;
        this.imageCache.isRefilling[type] = true;

        try {
            const baseEndpoint = `/api/images/${type === 'dogs' ? 'dog' : 'cat'}`;

            // Fetch in actual batches to avoid overwhelming the server
            const batchSize = 10;
            for (let batch = 0; batch < count; batch += batchSize) {
                const batchCount = Math.min(batchSize, count - batch);
                const fetches = [];

                for (let i = 0; i < batchCount; i++) {
                    // Add cache-buster to prevent browser caching
                    const endpoint = `${baseEndpoint}?_=${Date.now()}-${batch + i}`;
                    fetches.push(
                        fetch(endpoint, { cache: 'no-store' })
                            .then(async (response) => {
                                if (response.ok) {
                                    const blob = await response.blob();
                                    return URL.createObjectURL(blob);
                                }
                                return null;
                            })
                            .catch(() => null)
                    );
                }

                const results = await Promise.all(fetches);
                const validUrls = results.filter(url => url !== null);
                this.imageCache[type].push(...validUrls);
            }

            console.log(`Fetched ${this.imageCache[type].length} ${type} images from backend`);
        } catch (e) {
            console.error(`Failed to fetch ${type} images:`, e);
        } finally {
            this.imageCache.isRefilling[type] = false;
        }
    },

    /**
     * Legacy method names for backward compatibility
     */
    async fetchDogImages(count) {
        return this.fetchImagesFromBackend('dogs', count);
    },

    async fetchCatImages(count) {
        return this.fetchImagesFromBackend('cats', count);
    },

    /**
     * Get an image from cache, removing it so it won't be reused
     * Triggers background refill if cache is running low
     */
    getImageFromCache(type) {
        const cache = this.imageCache[type];
        console.log(`getImageFromCache(${type}): cache has ${cache.length} images`);
        if (cache.length === 0) {
            console.warn(`${type} cache EMPTY!`);
            return null;
        }

        // Take the first image and remove it from cache
        const url = cache.shift();
        console.log(`Got image from ${type} cache, ${cache.length} remaining, url:`, url?.substring(0, 50));

        // Check if we need to refill
        if (cache.length < this.IMAGE_CACHE_THRESHOLD) {
            console.log(`${type} cache low (${cache.length}), refilling...`);
            if (type === 'dogs') {
                this.fetchDogImages(this.IMAGE_CACHE_SIZE);
            } else {
                this.fetchCatImages(this.IMAGE_CACHE_SIZE);
            }
        }

        return url;
    },

    /**
     * Bind UI event handlers
     */
    bindEvents() {
        const tickerInput = document.getElementById('ticker-input');
        const searchResults = document.getElementById('search-results');

        // Search button
        document.getElementById('search-btn').addEventListener('click', () => this.analyzeStock());

        // Autocomplete on input
        tickerInput.addEventListener('input', (e) => {
            const query = e.target.value.trim();
            if (this.searchTimeout) clearTimeout(this.searchTimeout);

            if (query.length < 2) {
                this.hideSearchResults();
                return;
            }

            // Debounce search
            this.searchTimeout = setTimeout(() => this.performSearch(query), 300);
        });

        // Enter key in ticker input
        tickerInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') {
                this.hideSearchResults();
                this.analyzeStock();
            }
        });

        // Hide results on blur (with delay to allow click)
        tickerInput.addEventListener('blur', () => {
            setTimeout(() => this.hideSearchResults(), 200);
        });

        // Show results on focus if there's a query
        tickerInput.addEventListener('focus', (e) => {
            if (e.target.value.trim().length >= 2) {
                this.performSearch(e.target.value.trim());
            }
        });

        // Period change
        document.getElementById('period-select').addEventListener('change', async (e) => {
            this.currentPeriod = e.target.value;
            if (this.currentTicker) {
                // If comparing, re-fetch comparison data with new period
                if (this.comparisonTicker) {
                    const comparisonTicker = this.comparisonTicker;
                    // Clear comparison temporarily (analyzeStock will fetch primary data)
                    this.comparisonTicker = null;
                    this.comparisonHistoryData = null;
                    await this.analyzeStock();
                    // Re-fetch comparison with new period
                    await this.setComparison(comparisonTicker);
                } else {
                    await this.analyzeStock();
                }
            }
        });

        // Chart type change
        document.getElementById('chart-type').addEventListener('change', () => this.updateChart());

        // Moving average toggles
        ['ma-20', 'ma-50', 'ma-200'].forEach(id => {
            document.getElementById(id).addEventListener('change', () => this.updateChart());
        });

        // Technical indicator toggles (RSI, MACD, Bollinger, Stochastic)
        ['show-rsi', 'show-macd', 'show-bollinger', 'show-stochastic'].forEach(id => {
            document.getElementById(id).addEventListener('change', () => this.updateChart());
        });

        // Threshold slider
        const thresholdSlider = document.getElementById('threshold-slider');
        const thresholdValue = document.getElementById('threshold-value');
        thresholdSlider.addEventListener('input', (e) => {
            this.currentThreshold = parseInt(e.target.value);
            thresholdValue.textContent = `${this.currentThreshold}%`;
        });
        thresholdSlider.addEventListener('change', async () => {
            if (this.currentTicker) {
                await this.refreshSignificantMoves();
            }
        });

        // Show markers toggle
        document.getElementById('show-markers').addEventListener('change', () => this.updateChart());

        // Animal type toggle (cats vs dogs)
        document.querySelectorAll('input[name="animal-type"]').forEach(radio => {
            radio.addEventListener('change', (e) => {
                this.currentAnimal = e.target.value;
            });
        });

        // Comparison search input
        const compareInput = document.getElementById('compare-input');
        const compareResults = document.getElementById('compare-results');

        compareInput.addEventListener('input', (e) => {
            const query = e.target.value.trim();
            if (this.compareSearchTimeout) clearTimeout(this.compareSearchTimeout);

            if (query.length < 2) {
                this.hideCompareResults();
                return;
            }

            // Debounce search
            this.compareSearchTimeout = setTimeout(() => this.performCompareSearch(query), 300);
        });

        compareInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') {
                this.hideCompareResults();
                const ticker = compareInput.value.trim().toUpperCase();
                if (ticker && this.currentTicker) {
                    this.setComparison(ticker);
                }
            }
        });

        compareInput.addEventListener('blur', () => {
            setTimeout(() => this.hideCompareResults(), 200);
        });

        compareInput.addEventListener('focus', (e) => {
            if (e.target.value.trim().length >= 2) {
                this.performCompareSearch(e.target.value.trim());
            }
        });

        // Quick compare benchmark buttons
        document.querySelectorAll('[data-compare]').forEach(btn => {
            btn.addEventListener('click', () => {
                if (!this.currentTicker) {
                    alert('Please analyze a stock first before comparing.');
                    return;
                }
                const ticker = btn.dataset.compare;
                this.setComparison(ticker);
            });
        });

        // Clear comparison button
        document.getElementById('clear-compare').addEventListener('click', () => {
            this.clearComparison();
        });

        // Window resize handler for chart responsiveness
        let resizeTimeout;
        window.addEventListener('resize', () => {
            clearTimeout(resizeTimeout);
            resizeTimeout = setTimeout(() => {
                const chartEl = document.getElementById('stock-chart');
                if (chartEl && chartEl.data) {
                    Plotly.Plots.resize(chartEl);
                }
            }, 150);
        });

        // Mobile watchlist drawer handlers
        const mobileDrawer = document.getElementById('mobile-watchlist-drawer');
        const mobileBackdrop = document.getElementById('mobile-watchlist-backdrop');
        const mobileToggle = document.getElementById('mobile-watchlist-toggle');
        const mobileClose = document.getElementById('mobile-watchlist-close');
        const mobileContent = document.getElementById('mobile-watchlist-content');
        const desktopContent = document.getElementById('watchlist-container');

        if (mobileToggle && mobileDrawer) {
            // Open drawer
            mobileToggle.addEventListener('click', () => {
                // Sync content from desktop to mobile
                if (desktopContent && mobileContent) {
                    mobileContent.innerHTML = desktopContent.innerHTML;
                    // Re-bind click events for the cloned watchlist items
                    this.bindMobileWatchlistEvents();
                }
                mobileDrawer.classList.remove('hidden');
                document.body.style.overflow = 'hidden'; // Prevent background scroll
            });

            // Close drawer via X button
            if (mobileClose) {
                mobileClose.addEventListener('click', () => {
                    mobileDrawer.classList.add('hidden');
                    document.body.style.overflow = '';
                });
            }

            // Close drawer via backdrop click
            if (mobileBackdrop) {
                mobileBackdrop.addEventListener('click', () => {
                    mobileDrawer.classList.add('hidden');
                    document.body.style.overflow = '';
                });
            }
        }

        // Mobile create watchlist button
        const mobileCreateBtn = document.getElementById('mobile-create-watchlist-btn');
        if (mobileCreateBtn) {
            mobileCreateBtn.addEventListener('click', () => {
                // Close drawer and trigger create
                if (mobileDrawer) {
                    mobileDrawer.classList.add('hidden');
                    document.body.style.overflow = '';
                }
                this.showCreateWatchlistModal();
            });
        }

        // Mobile period select sync
        const mobilePeriodSelect = document.getElementById('mobile-period-select');
        const desktopPeriodSelect = document.getElementById('period-select');
        if (mobilePeriodSelect && desktopPeriodSelect) {
            mobilePeriodSelect.addEventListener('change', async (e) => {
                this.currentPeriod = e.target.value;
                desktopPeriodSelect.value = e.target.value; // Sync desktop
                if (this.currentTicker) {
                    if (this.comparisonTicker) {
                        const comparisonTicker = this.comparisonTicker;
                        this.comparisonTicker = null;
                        this.comparisonHistoryData = null;
                        await this.analyzeStock();
                        await this.setComparison(comparisonTicker);
                    } else {
                        await this.analyzeStock();
                    }
                }
            });
        }

        // Mobile chart type sync
        const mobileChartType = document.getElementById('mobile-chart-type');
        const desktopChartType = document.getElementById('chart-type');
        if (mobileChartType && desktopChartType) {
            mobileChartType.addEventListener('change', (e) => {
                desktopChartType.value = e.target.value; // Sync desktop
                this.updateChart();
            });
        }
    },

    /**
     * Bind click events for mobile watchlist items (after cloning from desktop)
     */
    bindMobileWatchlistEvents() {
        const mobileContent = document.getElementById('mobile-watchlist-content');
        if (!mobileContent) return;

        // Re-bind combined view buttons (uses .combined-view-btn class)
        mobileContent.querySelectorAll('.combined-view-btn:not([disabled])').forEach(btn => {
            btn.addEventListener('click', async (e) => {
                e.stopPropagation();
                const watchlistId = btn.dataset.watchlistId;
                // Close mobile drawer first
                const mobileDrawer = document.getElementById('mobile-watchlist-drawer');
                if (mobileDrawer) {
                    mobileDrawer.classList.add('hidden');
                    document.body.style.overflow = '';
                }
                // Use Watchlist object to open combined view
                if (typeof Watchlist !== 'undefined') {
                    await Watchlist.openCombinedView(watchlistId);
                }
            });
        });

        // Re-bind watchlist item clicks (expand/collapse)
        mobileContent.querySelectorAll('.watchlist-item').forEach(item => {
            item.addEventListener('click', (e) => {
                // Toggle expansion if clicking on the header area
                if (!e.target.closest('button') && !e.target.closest('a')) {
                    const tickers = item.querySelector('.watchlist-tickers');
                    if (tickers) {
                        tickers.classList.toggle('hidden');
                    }
                }
            });
        });

        // Re-bind ticker links
        mobileContent.querySelectorAll('.ticker-link').forEach(link => {
            link.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                const ticker = link.dataset.ticker;
                if (ticker) {
                    // Close mobile drawer
                    const mobileDrawer = document.getElementById('mobile-watchlist-drawer');
                    if (mobileDrawer) {
                        mobileDrawer.classList.add('hidden');
                        document.body.style.overflow = '';
                    }
                    document.getElementById('ticker-input').value = ticker;
                    this.analyzeStock();
                }
            });
        });
    },

    /**
     * Perform search for autocomplete
     * Uses client-side search for instant results.
     * If no results found locally, waits 5 seconds then tries server.
     */
    async performSearch(query) {
        const loader = document.getElementById('search-loader');
        loader.classList.remove('hidden');

        // Cancel any pending server fallback
        if (this.serverFallbackTimeout) {
            clearTimeout(this.serverFallbackTimeout);
            this.serverFallbackTimeout = null;
        }

        try {
            const data = await API.search(query);
            this.showSearchResults(data.results, query);

            // If local search returned empty, schedule server fallback after 5 seconds
            if (data.pendingServerFallback && data.results.length === 0) {
                this.serverFallbackTimeout = setTimeout(async () => {
                    try {
                        loader.classList.remove('hidden');
                        const serverData = await API.searchServerFallback(query);
                        // Only update if user hasn't changed the query
                        const currentQuery = document.getElementById('ticker-input').value.trim();
                        if (currentQuery === query && serverData.results && serverData.results.length > 0) {
                            this.showSearchResults(serverData.results, query);
                        }
                    } catch (error) {
                        console.error('Server fallback search failed:', error);
                    } finally {
                        loader.classList.add('hidden');
                    }
                }, 5000);
            }
        } catch (error) {
            console.error('Search failed:', error);
            this.hideSearchResults();
        } finally {
            loader.classList.add('hidden');
        }
    },

    /**
     * Show search results dropdown
     * @param {Array} results - Search results
     * @param {string} query - Original search query (for server fallback message)
     */
    showSearchResults(results, query = '') {
        const container = document.getElementById('search-results');

        if (!results || results.length === 0) {
            // Show message with server fallback hint if client-side returned empty
            const hasPendingFallback = this.serverFallbackTimeout !== null;
            const message = hasPendingFallback
                ? 'No local results. Checking server in a few seconds...'
                : 'No results found';
            container.innerHTML = `<div class="px-4 py-3 text-gray-500 dark:text-gray-400 text-sm">${message}</div>`;
            container.classList.remove('hidden');
            return;
        }

        container.innerHTML = results.map(r => `
            <div class="search-result px-4 py-3 hover:bg-gray-100 dark:hover:bg-gray-700 cursor-pointer border-b border-gray-100 dark:border-gray-700 last:border-0"
                 data-symbol="${r.symbol}">
                <div class="font-medium text-gray-900 dark:text-white">${r.symbol}</div>
                <div class="text-sm text-gray-600 dark:text-gray-300">${r.shortName || r.longName || ''}</div>
                <div class="text-xs text-gray-400 dark:text-gray-500">${r.exchange || ''} ${r.type ? `• ${r.type}` : ''}</div>
            </div>
        `).join('');

        // Add click handlers to results
        container.querySelectorAll('.search-result').forEach(el => {
            el.addEventListener('click', () => {
                const symbol = el.dataset.symbol;
                document.getElementById('ticker-input').value = symbol;
                this.hideSearchResults();
                this.analyzeStock();
            });
        });

        container.classList.remove('hidden');
    },

    /**
     * Hide search results dropdown
     */
    hideSearchResults() {
        document.getElementById('search-results').classList.add('hidden');
    },

    /**
     * Perform search for comparison autocomplete
     */
    async performCompareSearch(query) {
        const loader = document.getElementById('compare-loader');
        loader.classList.remove('hidden');

        try {
            const data = await API.search(query);
            this.showCompareResults(data.results);
        } catch (error) {
            console.error('Comparison search failed:', error);
            this.hideCompareResults();
        } finally {
            loader.classList.add('hidden');
        }
    },

    /**
     * Show comparison search results dropdown
     */
    showCompareResults(results) {
        const container = document.getElementById('compare-results');

        if (!results || results.length === 0) {
            container.innerHTML = '<div class="px-4 py-3 text-gray-500 dark:text-gray-400 text-sm">No results found</div>';
            container.classList.remove('hidden');
            return;
        }

        container.innerHTML = results.map(r => `
            <div class="compare-result px-4 py-3 hover:bg-gray-100 dark:hover:bg-gray-700 cursor-pointer border-b border-gray-100 dark:border-gray-700 last:border-0"
                 data-symbol="${r.symbol}">
                <div class="font-medium text-gray-900 dark:text-white">${r.symbol}</div>
                <div class="text-sm text-gray-600 dark:text-gray-300">${r.shortName || r.longName || ''}</div>
                <div class="text-xs text-gray-400 dark:text-gray-500">${r.exchange || ''} ${r.type ? `• ${r.type}` : ''}</div>
            </div>
        `).join('');

        // Add click handlers to results
        container.querySelectorAll('.compare-result').forEach(el => {
            el.addEventListener('click', () => {
                const symbol = el.dataset.symbol;
                document.getElementById('compare-input').value = symbol;
                this.hideCompareResults();
                if (this.currentTicker) {
                    this.setComparison(symbol);
                }
            });
        });

        container.classList.remove('hidden');
    },

    /**
     * Hide comparison search results dropdown
     */
    hideCompareResults() {
        document.getElementById('compare-results').classList.add('hidden');
    },

    /**
     * Set comparison stock and fetch its data
     */
    async setComparison(ticker) {
        ticker = ticker.toUpperCase();

        // Prevent comparing stock to itself
        if (ticker === this.currentTicker) {
            alert('Cannot compare a stock to itself. Please choose a different symbol.');
            return;
        }

        try {
            document.getElementById('compare-input').value = ticker;

            // Fetch comparison history data
            this.comparisonHistoryData = await API.getHistory(ticker, this.currentPeriod);
            this.comparisonTicker = ticker;

            // Disable technical indicators (they don't make sense for comparison)
            this.disableIndicators(true);

            // Show clear button
            document.getElementById('clear-compare').classList.remove('hidden');

            // Re-render chart with comparison
            this.renderChart();
            this.attachChartHoverListeners();
        } catch (error) {
            console.error('Failed to fetch comparison data:', error);
            alert(`Failed to load comparison data for ${ticker}`);
            this.clearComparison();
        }
    },

    /**
     * Clear comparison and restore single-stock view
     */
    clearComparison() {
        this.comparisonTicker = null;
        this.comparisonHistoryData = null;
        document.getElementById('compare-input').value = '';
        document.getElementById('clear-compare').classList.add('hidden');

        // Re-enable technical indicators
        this.disableIndicators(false);

        // Re-render chart without comparison
        if (this.historyData) {
            this.renderChart();
            this.attachChartHoverListeners();
        }
    },

    /**
     * Enable or disable technical indicator checkboxes
     */
    disableIndicators(disabled) {
        const rsiCheckbox = document.getElementById('show-rsi');
        const macdCheckbox = document.getElementById('show-macd');
        const bollingerCheckbox = document.getElementById('show-bollinger');
        const stochasticCheckbox = document.getElementById('show-stochastic');
        const rsiLabel = document.getElementById('rsi-label');
        const macdLabel = document.getElementById('macd-label');
        const bollingerLabel = document.getElementById('bollinger-label');
        const stochasticLabel = document.getElementById('stochastic-label');

        rsiCheckbox.disabled = disabled;
        macdCheckbox.disabled = disabled;
        bollingerCheckbox.disabled = disabled;
        stochasticCheckbox.disabled = disabled;

        if (disabled) {
            // Uncheck and dim the indicators
            rsiCheckbox.checked = false;
            macdCheckbox.checked = false;
            bollingerCheckbox.checked = false;
            stochasticCheckbox.checked = false;
            rsiLabel.classList.add('opacity-50', 'cursor-not-allowed');
            macdLabel.classList.add('opacity-50', 'cursor-not-allowed');
            bollingerLabel.classList.add('opacity-50', 'cursor-not-allowed');
            stochasticLabel.classList.add('opacity-50', 'cursor-not-allowed');
        } else {
            rsiLabel.classList.remove('opacity-50', 'cursor-not-allowed');
            macdLabel.classList.remove('opacity-50', 'cursor-not-allowed');
            bollingerLabel.classList.remove('opacity-50', 'cursor-not-allowed');
            stochasticLabel.classList.remove('opacity-50', 'cursor-not-allowed');
        }
    },

    /**
     * Check if API is healthy
     */
    async checkApiHealth() {
        try {
            await API.healthCheck();
            console.log('API is healthy');
        } catch (error) {
            console.error('API health check failed:', error);
        }
    },

    /**
     * Main analysis function
     */
    async analyzeStock() {
        const ticker = document.getElementById('ticker-input').value.trim().toUpperCase();
        if (!ticker) {
            this.showError('Please enter a stock ticker');
            return;
        }

        this.currentTicker = ticker;
        this.currentPeriod = document.getElementById('period-select').value;
        this.newsCache = {}; // Clear news cache for new ticker
        this.showLoading();

        try {
            // Fetch all data in parallel
            const [stockInfo, history, analysis, significantMoves, news] = await Promise.all([
                API.getStockInfo(ticker),
                API.getHistory(ticker, this.currentPeriod),
                API.getAnalysis(ticker, this.currentPeriod),
                API.getSignificantMoves(ticker, this.currentThreshold, this.currentPeriod),
                API.getAggregatedNews(ticker, 30, 10)
            ]);

            this.historyData = history;
            this.analysisData = analysis;
            this.significantMovesData = significantMoves;

            this.renderStockInfo(stockInfo);
            this.renderKeyMetrics(stockInfo);
            this.renderPerformance(analysis.performance);
            this.renderChart();
            this.attachChartHoverListeners();
            this.renderSignificantMoves(significantMoves);
            this.renderNews(news);

            this.showResults();

            // Pre-fetch news for significant moves in background
            this.prefetchNewsForMoves();

            // Show "Add to Watchlist" button
            if (typeof Watchlist !== 'undefined') {
                Watchlist.showAddToWatchlistButton();
            }
        } catch (error) {
            this.showError(error.message);
        }
    },

    /**
     * Render stock info section
     */
    renderStockInfo(info) {
        const priceChange = info.dayChange || 0;
        const priceChangePercent = info.dayChangePercent || 0;
        const isPositive = priceChange >= 0;

        // Build identifiers list
        const identifiers = [
            { label: 'Ticker', value: info.symbol },
            { label: 'ISIN', value: info.isin },
            { label: 'CUSIP', value: info.cusip },
            { label: 'SEDOL', value: info.sedol }
        ].filter(id => id.value);

        const identifiersHtml = identifiers.length > 0
            ? `<div class="flex flex-wrap gap-x-4 gap-y-1 mt-2">
                ${identifiers.map(id => `
                    <span class="text-xs text-gray-500 dark:text-gray-400">
                        <span class="font-medium">${id.label}:</span> ${id.value}
                    </span>
                `).join('')}
               </div>`
            : '';

        // Smart truncation: only truncate long descriptions, and cut at sentence boundaries
        const descriptionHtml = info.description
            ? `<p class="text-sm text-gray-600 dark:text-gray-300 mt-3">${this.truncateAtSentence(info.description, 500)}</p>`
            : '';

        document.getElementById('stock-info').innerHTML = `
            <div class="flex-1">
                <div class="flex items-start justify-between">
                    <div>
                        <h2 class="text-2xl font-bold text-gray-900 dark:text-white">${info.longName || info.shortName || info.symbol}</h2>
                        <p class="text-sm text-gray-500 dark:text-gray-400">${info.exchange || ''} ${info.currency ? `• ${info.currency}` : ''}${info.sector ? ` • ${info.sector}` : ''}</p>
                        ${identifiersHtml}
                    </div>
                    <div class="text-right ml-4">
                        <div class="text-3xl font-bold text-gray-900 dark:text-white">
                            $${this.formatNumber(info.currentPrice)}
                        </div>
                        <div class="text-lg ${isPositive ? 'text-success' : 'text-danger'}">
                            ${isPositive ? '+' : ''}${this.formatNumber(priceChange)} (${isPositive ? '+' : ''}${this.formatNumber(priceChangePercent)}%)
                        </div>
                    </div>
                </div>
                ${descriptionHtml}
            </div>
        `;
    },

    /**
     * Render key metrics
     */
    renderKeyMetrics(info) {
        const metrics = [
            { label: 'Market Cap', value: this.formatLargeNumber(info.marketCap) },
            { label: 'P/E Ratio', value: this.formatNumber(info.peRatio) },
            { label: '52W High', value: `$${this.formatNumber(info.fiftyTwoWeekHigh)}` },
            { label: '52W Low', value: `$${this.formatNumber(info.fiftyTwoWeekLow)}` },
            { label: 'Avg Volume', value: this.formatLargeNumber(info.averageVolume) },
            { label: 'Dividend Yield', value: info.dividendYield ? `${(info.dividendYield * 100).toFixed(2)}%` : 'N/A' }
        ];

        document.getElementById('key-metrics').innerHTML = metrics.map(m => `
            <div class="flex justify-between">
                <span class="text-gray-600 dark:text-gray-400">${m.label}</span>
                <span class="font-medium text-gray-900 dark:text-white">${m.value || 'N/A'}</span>
            </div>
        `).join('');
    },

    /**
     * Render performance metrics
     */
    renderPerformance(performance) {
        if (!performance) {
            document.getElementById('performance-metrics').innerHTML = '<p class="text-gray-500 dark:text-gray-400">No performance data available</p>';
            return;
        }

        const metrics = [
            { label: 'Total Return', value: `${performance.totalReturn >= 0 ? '+' : ''}${this.formatNumber(performance.totalReturn)}%`, color: performance.totalReturn >= 0 ? 'text-success' : 'text-danger' },
            { label: 'Volatility (Ann.)', value: `${this.formatNumber(performance.volatility)}%` },
            { label: 'Highest Close', value: `$${this.formatNumber(performance.highestClose)}` },
            { label: 'Lowest Close', value: `$${this.formatNumber(performance.lowestClose)}` },
            { label: 'Avg Volume', value: this.formatLargeNumber(performance.averageVolume) }
        ];

        document.getElementById('performance-metrics').innerHTML = metrics.map(m => `
            <div class="flex justify-between">
                <span class="text-gray-600 dark:text-gray-400">${m.label}</span>
                <span class="font-medium ${m.color || 'text-gray-900 dark:text-white'}">${m.value || 'N/A'}</span>
            </div>
        `).join('');
    },

    /**
     * Render significant moves
     */
    renderSignificantMoves(data) {
        if (!data || !data.moves || data.moves.length === 0) {
            document.getElementById('significant-moves').innerHTML = '<p class="text-gray-500 dark:text-gray-400">No significant moves found</p>';
            return;
        }

        document.getElementById('significant-moves').innerHTML = data.moves.slice(0, 10).map(move => {
            const isPositive = move.percentChange >= 0;
            const date = new Date(move.date).toLocaleDateString();
            return `
                <div class="flex justify-between items-center py-2 border-b border-gray-100 dark:border-gray-700">
                    <span class="text-gray-600 dark:text-gray-400">${date}</span>
                    <span class="font-medium ${isPositive ? 'text-success' : 'text-danger'}">
                        ${isPositive ? '+' : ''}${this.formatNumber(move.percentChange)}%
                    </span>
                </div>
            `;
        }).join('');
    },

    /**
     * Render news from aggregated news API
     */
    renderNews(data) {
        if (!data || !data.articles || data.articles.length === 0) {
            document.getElementById('news-list').innerHTML = '<p class="text-gray-500 dark:text-gray-400">No recent news available</p>';
            return;
        }

        // Build source breakdown header if we have multiple sources
        let sourceHeader = '';
        if (data.sourceBreakdown && Object.keys(data.sourceBreakdown).length > 0) {
            const sources = Object.entries(data.sourceBreakdown)
                .map(([source, count]) => `${source}: ${count}`)
                .join(', ');
            sourceHeader = `<p class="text-xs text-gray-400 dark:text-gray-500 mb-3">Sources: ${sources}</p>`;
        }

        const articlesHtml = data.articles.slice(0, 5).map(article => {
            const date = new Date(article.publishedAt).toLocaleDateString();
            // Show the API source (Finnhub, Marketaux) alongside the publisher
            const apiSource = article.sourceApi ? `[${article.sourceApi}]` : '';
            return `
                <div class="border-b border-gray-100 dark:border-gray-700 pb-4">
                    <a href="${article.url}" target="_blank" rel="noopener noreferrer"
                       class="text-primary hover:text-blue-700 dark:hover:text-blue-400 font-medium">
                        ${article.headline}
                    </a>
                    <p class="text-sm text-gray-500 dark:text-gray-400 mt-1">
                        ${article.source} • ${date}
                        <span class="text-xs text-gray-400 dark:text-gray-500 ml-1">${apiSource}</span>
                    </p>
                    ${article.summary ? `<p class="text-gray-600 dark:text-gray-300 mt-2 text-sm">${article.summary.substring(0, 150)}...</p>` : ''}
                </div>
            `;
        }).join('');

        document.getElementById('news-list').innerHTML = sourceHeader + articlesHtml;
    },

    /**
     * Render chart
     */
    renderChart() {
        const options = {
            chartType: document.getElementById('chart-type').value,
            showMa20: document.getElementById('ma-20').checked,
            showMa50: document.getElementById('ma-50').checked,
            showMa200: document.getElementById('ma-200').checked,
            significantMoves: this.significantMovesData,
            showMarkers: document.getElementById('show-markers').checked,
            showRsi: document.getElementById('show-rsi').checked,
            showMacd: document.getElementById('show-macd').checked,
            showBollinger: document.getElementById('show-bollinger').checked,
            showStochastic: document.getElementById('show-stochastic').checked,
            // Comparison data
            comparisonData: this.comparisonHistoryData,
            comparisonTicker: this.comparisonTicker
        };

        // Adjust chart height based on enabled indicators (only when not comparing)
        const chartEl = document.getElementById('stock-chart');
        const baseHeight = 400;
        const indicatorHeight = 150;
        let totalHeight = baseHeight;

        // Only add indicator height if not comparing (indicators disabled during comparison)
        if (!this.comparisonTicker) {
            if (options.showRsi) totalHeight += indicatorHeight;
            if (options.showMacd) totalHeight += indicatorHeight;
            if (options.showStochastic) totalHeight += indicatorHeight;
        }
        chartEl.style.height = `${totalHeight}px`;

        Charts.renderStockChart('stock-chart', this.historyData, this.analysisData, options);
    },

    /**
     * Update chart with new options
     */
    updateChart() {
        if (this.historyData) {
            this.renderChart();
            this.attachChartHoverListeners();
        }
    },

    /**
     * Refresh significant moves with new threshold
     */
    async refreshSignificantMoves() {
        if (!this.currentTicker) return;

        try {
            this.significantMovesData = await API.getSignificantMoves(
                this.currentTicker,
                this.currentThreshold,
                this.currentPeriod
            );
            this.renderChart();
            this.attachChartHoverListeners();
            this.renderSignificantMoves(this.significantMovesData);

            // Pre-fetch news for the new set of moves
            this.prefetchNewsForMoves();
        } catch (error) {
            console.error('Failed to refresh significant moves:', error);
        }
    },

    /**
     * Attach Plotly hover event listeners for significant move markers
     */
    attachChartHoverListeners() {
        const plot = document.getElementById('stock-chart');
        if (!plot) return;

        // Remove existing listeners by getting fresh reference
        plot.removeAllListeners?.('plotly_hover');
        plot.removeAllListeners?.('plotly_unhover');

        plot.on('plotly_hover', (data) => {
            const point = data.points[0];
            // Check if this is a marker trace (significant move)
            if (point.data.name && point.data.name.includes('Move') && point.customdata) {
                // Cancel any pending hide immediately
                if (this.hideTimeout) {
                    clearTimeout(this.hideTimeout);
                    this.hideTimeout = null;
                }
                // Cancel any pending show and reschedule
                if (this.hoverTimeout) clearTimeout(this.hoverTimeout);
                this.hoverTimeout = setTimeout(() => {
                    this.showHoverCard(data.event, point.customdata);
                }, 150); // Reduced from 200ms for snappier response
            }
        });

        plot.on('plotly_unhover', () => {
            if (this.hoverTimeout) {
                clearTimeout(this.hoverTimeout);
                this.hoverTimeout = null;
            }
            // Delay hiding to allow moving to the card
            this.scheduleHideHoverCard();
        });

        // Setup hover card mouse events (only once)
        const card = document.getElementById('wiki-hover-card');
        if (card && !card.dataset.listenersAttached) {
            card.dataset.listenersAttached = 'true';
            card.addEventListener('mouseenter', () => {
                this.isHoverCardHovered = true;
                if (this.hideTimeout) {
                    clearTimeout(this.hideTimeout);
                    this.hideTimeout = null;
                }
            });
            card.addEventListener('mouseleave', () => {
                this.isHoverCardHovered = false;
                this.scheduleHideHoverCard();
            });
        }
    },

    /**
     * Pre-fetch news for all significant moves in the background.
     * Called after chart renders to have news ready before user hovers.
     */
    prefetchNewsForMoves() {
        if (!this.significantMovesData?.moves || !this.currentTicker) return;

        const ticker = this.currentTicker;
        const moves = this.significantMovesData.moves;

        console.log(`Pre-fetching news for ${moves.length} significant moves...`);

        // Fetch news for each move in parallel (but don't await - run in background)
        moves.forEach(move => {
            const moveDate = new Date(move.date);
            const dateParam = moveDate.toISOString().split('T')[0];
            const direction = move.percentChange >= 0 ? 'up' : 'down';
            const cacheKey = `${ticker}:${dateParam}:${direction}`;

            // Skip if already cached
            if (this.newsCache[cacheKey]) return;

            // Mark as pending to avoid duplicate fetches
            this.newsCache[cacheKey] = { pending: true };

            fetch(`/api/stock/${ticker}/news/move?date=${dateParam}&change=${move.percentChange}`)
                .then(response => response.json())
                .then(data => {
                    this.newsCache[cacheKey] = {
                        articles: data.articles || [],
                        fetchedAt: Date.now()
                    };
                })
                .catch(() => {
                    // Cache the failure so we don't retry immediately
                    this.newsCache[cacheKey] = {
                        articles: [],
                        error: true,
                        fetchedAt: Date.now()
                    };
                });
        });
    },

    /**
     * Show Wikipedia-style hover card for significant move
     * News is lazy-loaded when the card is shown
     */
    showHoverCard(event, moveData) {
        // Cancel any pending hide when showing
        if (this.hideTimeout) {
            clearTimeout(this.hideTimeout);
            this.hideTimeout = null;
        }

        const card = document.getElementById('wiki-hover-card');
        const image = document.getElementById('wiki-hover-image');
        const placeholder = document.getElementById('wiki-hover-placeholder');
        const dateEl = document.getElementById('wiki-hover-date');
        const returnEl = document.getElementById('wiki-hover-return');
        const headlineEl = document.getElementById('wiki-hover-headline');
        const summaryEl = document.getElementById('wiki-hover-summary');
        const sourceEl = document.getElementById('wiki-hover-source');

        // Format date
        const moveDate = new Date(moveData.date);
        const formattedDate = moveDate.toLocaleDateString('en-US', {
            weekday: 'long',
            year: 'numeric',
            month: 'long',
            day: 'numeric'
        });
        dateEl.textContent = formattedDate;

        // Format return
        const isPositive = moveData.percentChange >= 0;
        returnEl.textContent = `${isPositive ? '+' : ''}${moveData.percentChange.toFixed(2)}% ${moveData.magnitude} move`;
        returnEl.className = `text-sm font-bold mb-2 ${isPositive ? 'text-green-600' : 'text-red-600'}`;

        // Get image URL from cache (instant, no fetch delay)
        const setAnimalImage = () => {
            // Revoke previous blob URL to free memory
            if (image.src && image.src.startsWith('blob:')) {
                URL.revokeObjectURL(image.src);
            }

            // Set up error handler
            image.onerror = () => {
                image.classList.add('hidden');
                placeholder.classList.remove('hidden');
            };

            // Try to get image from cache (shift removes and returns, so each call gets a new image)
            const cachedUrl = this.getImageFromCache(this.currentAnimal);

            if (cachedUrl) {
                // Use cached image (already preloaded, so instant)
                image.src = cachedUrl;
                image.classList.remove('hidden');
                placeholder.classList.add('hidden');
            } else {
                // Cache empty - fallback to backend fetch (shouldn't happen often)
                console.warn(`${this.currentAnimal} cache empty, fetching from backend...`);
                const endpoint = this.currentAnimal === 'dogs' ? '/api/images/dog' : '/api/images/cat';
                fetch(endpoint)
                    .then(async (response) => {
                        if (response.ok) {
                            const blob = await response.blob();
                            image.src = URL.createObjectURL(blob);
                            image.classList.remove('hidden');
                            placeholder.classList.add('hidden');
                        } else {
                            image.classList.add('hidden');
                            placeholder.classList.remove('hidden');
                        }
                    })
                    .catch(() => {
                        image.classList.add('hidden');
                        placeholder.classList.remove('hidden');
                    });
            }
        };

        // Show animal image immediately
        setAnimalImage();

        // Build cache key
        const ticker = this.currentTicker;
        const dateParam = moveDate.toISOString().split('T')[0];
        const direction = moveData.percentChange >= 0 ? 'up' : 'down';
        const cacheKey = `${ticker}:${dateParam}:${direction}`;

        // Helper to display news content
        const displayNews = (articles) => {
            const news = articles && articles.length > 0 ? articles[0] : null;
            if (news) {
                headlineEl.textContent = news.headline;
                headlineEl.href = news.url || '#';
                summaryEl.textContent = news.summary || '';
                summaryEl.style.display = news.summary ? 'block' : 'none';
                const newsDate = new Date(news.publishedAt);
                sourceEl.textContent = `${news.source} • ${newsDate.toLocaleDateString()}`;
            } else {
                headlineEl.textContent = 'No related news found';
                headlineEl.href = '#';
                summaryEl.textContent = 'No news articles were found for this date range.';
                summaryEl.style.display = 'block';
                sourceEl.textContent = '';
            }
        };

        // Check cache first
        const cached = this.newsCache[cacheKey];
        if (cached && !cached.pending) {
            // Cache hit - display immediately
            displayNews(cached.articles);
        } else {
            // Cache miss or still pending - show loading and fetch
            headlineEl.textContent = 'Loading news...';
            headlineEl.href = '#';
            headlineEl.style.display = 'block';
            summaryEl.textContent = '';
            summaryEl.style.display = 'none';
            sourceEl.textContent = '';

            // If pending, poll for completion; otherwise fetch fresh
            if (cached?.pending) {
                // Poll for cache completion
                const pollInterval = setInterval(() => {
                    const updated = this.newsCache[cacheKey];
                    if (updated && !updated.pending) {
                        clearInterval(pollInterval);
                        displayNews(updated.articles);
                    }
                }, 50);
                // Timeout after 5 seconds
                setTimeout(() => clearInterval(pollInterval), 5000);
            } else {
                // Fetch fresh
                fetch(`/api/stock/${ticker}/news/move?date=${dateParam}&change=${moveData.percentChange}`)
                    .then(response => response.json())
                    .then(data => {
                        this.newsCache[cacheKey] = {
                            articles: data.articles || [],
                            fetchedAt: Date.now()
                        };
                        displayNews(data.articles);
                    })
                    .catch(() => {
                        headlineEl.textContent = 'Unable to load news';
                        headlineEl.href = '#';
                        summaryEl.textContent = 'News service temporarily unavailable.';
                        summaryEl.style.display = 'block';
                        sourceEl.textContent = '';
                    });
            }
        }

        // Position the card near the cursor
        const x = event.clientX || event.pageX;
        const y = event.clientY || event.pageY;
        const cardWidth = 320;
        const padding = 15;

        // Use visualViewport for accurate viewport dimensions
        const viewportHeight = window.visualViewport?.height || window.innerHeight;

        // Set max-height dynamically to ensure it fits in viewport
        card.style.maxHeight = `${viewportHeight - 2 * padding}px`;

        // Show card off-screen first to measure actual height
        card.style.left = '-9999px';
        card.style.top = '-9999px';
        card.classList.remove('hidden');

        // Use requestAnimationFrame to ensure DOM has updated
        requestAnimationFrame(() => {
            const cardHeight = card.offsetHeight;
            // Use visualViewport for accurate viewport dimensions (accounts for browser chrome)
            const viewportHeight = window.visualViewport?.height || window.innerHeight;
            const viewportWidth = window.visualViewport?.width || window.innerWidth;

            let left = x + padding;
            let top = y - cardHeight / 2;

            // Keep within viewport - right edge
            if (left + cardWidth > viewportWidth - padding) {
                left = x - cardWidth - padding;
            }
            // Left edge
            if (left < padding) {
                left = padding;
            }
            // Top edge
            if (top < padding) {
                top = padding;
            }
            // Bottom edge - ensure card fits entirely in viewport
            if (top + cardHeight > viewportHeight - padding) {
                top = Math.max(padding, viewportHeight - cardHeight - padding);
            }

            card.style.left = `${left}px`;
            card.style.top = `${top}px`;
        });
    },

    /**
     * Schedule hiding the hover card with delay
     */
    scheduleHideHoverCard() {
        if (this.hideTimeout) {
            clearTimeout(this.hideTimeout);
        }
        this.hideTimeout = setTimeout(() => {
            if (!this.isHoverCardHovered) {
                this.hideHoverCard();
            }
        }, 400); // 400ms delay to allow moving to card
    },

    /**
     * Hide the hover card
     */
    hideHoverCard() {
        const card = document.getElementById('wiki-hover-card');
        const image = document.getElementById('wiki-hover-image');
        const placeholder = document.getElementById('wiki-hover-placeholder');

        card.classList.add('hidden');

        // Clear the image to prevent flash of old image on next show
        image.src = '';
        image.classList.add('hidden');
        placeholder.classList.remove('hidden');
    },

    /**
     * Show loading state
     */
    showLoading() {
        document.getElementById('results-section').classList.add('hidden');
        document.getElementById('error-section').classList.add('hidden');
        document.getElementById('loading-section').classList.remove('hidden');
    },

    /**
     * Show results
     */
    showResults() {
        document.getElementById('loading-section').classList.add('hidden');
        document.getElementById('error-section').classList.add('hidden');
        document.getElementById('results-section').classList.remove('hidden');
    },

    /**
     * Show error
     */
    showError(message) {
        document.getElementById('loading-section').classList.add('hidden');
        document.getElementById('results-section').classList.add('hidden');
        document.getElementById('error-section').classList.remove('hidden');
        document.getElementById('error-message').textContent = message;
    },

    /**
     * Format number
     */
    formatNumber(value) {
        if (value == null) return 'N/A';
        return Number(value).toFixed(2);
    },

    /**
     * Format large number (millions, billions)
     */
    formatLargeNumber(value) {
        if (value == null) return 'N/A';
        if (value >= 1e12) return `$${(value / 1e12).toFixed(2)}T`;
        if (value >= 1e9) return `$${(value / 1e9).toFixed(2)}B`;
        if (value >= 1e6) return `$${(value / 1e6).toFixed(2)}M`;
        if (value >= 1e3) return `${(value / 1e3).toFixed(2)}K`;
        return value.toString();
    },

    /**
     * Truncate text at sentence boundary if it exceeds maxLength.
     * Only truncates if necessary, and always ends at a complete sentence.
     */
    truncateAtSentence(text, maxLength = 500) {
        if (!text || text.length <= maxLength) {
            return text;
        }

        // Find sentence boundaries (., !, ?) followed by space or end
        const sentenceEnders = /[.!?](?:\s|$)/g;
        let lastGoodEnd = 0;
        let match;

        while ((match = sentenceEnders.exec(text)) !== null) {
            const endPos = match.index + 1; // Include the punctuation
            if (endPos <= maxLength) {
                lastGoodEnd = endPos;
            } else {
                break;
            }
        }

        // If we found a sentence boundary, use it
        if (lastGoodEnd > 0) {
            return text.substring(0, lastGoodEnd).trim();
        }

        // Fallback: no sentence boundary found within limit, just return full text
        // (better to show full text than cut mid-sentence)
        return text;
    }
};

// Expose App globally for other modules (like Watchlist)
window.App = App;

// Initialize when DOM is ready
document.addEventListener('DOMContentLoaded', () => App.init());
