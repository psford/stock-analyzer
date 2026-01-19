/**
 * Watchlist UI Manager
 * Handles all watchlist-related UI functionality
 */
const Watchlist = {
    watchlists: [],
    expandedWatchlists: new Set(),
    editingWatchlistId: null,
    pendingTickerToAdd: null, // Ticker to add after creating a new watchlist from dropdown

    // Combined view state
    combinedView: {
        isOpen: false,
        watchlistId: null,
        period: '1y',
        benchmark: null,
        data: null,
        showMarkers: true,
        currentAnimal: 'cats', // 'cats' or 'dogs'
        marketNews: [],
        hoverTimeout: null,
        hideTimeout: null,
        isHoverCardHovered: false
    },

    // Holdings editor state
    holdingsEditor: {
        watchlistId: null,
        quotes: null,
        returnToCombinedView: false,
        localTickers: [], // Local copy of tickers for editing
        searchTimeout: null
    },

    /**
     * Initialize watchlist functionality
     */
    init() {
        this.bindEvents();
        this.loadWatchlists();
        this.updateStorageInfo();
    },

    /**
     * Bind all event listeners
     */
    bindEvents() {
        // Create watchlist buttons
        document.getElementById('create-watchlist-btn')?.addEventListener('click', () => this.openCreateModal());
        document.getElementById('create-first-watchlist')?.addEventListener('click', () => this.openCreateModal());
        document.getElementById('create-watchlist-from-dropdown')?.addEventListener('click', () => {
            // Capture the current ticker before hiding dropdown
            this.pendingTickerToAdd = window.App?.currentTicker || null;
            this.hideWatchlistDropdown();
            this.openCreateModal();
        });

        // Modal events
        document.getElementById('watchlist-modal-close')?.addEventListener('click', () => this.closeModal());
        document.getElementById('watchlist-modal-cancel')?.addEventListener('click', () => this.closeModal());
        document.getElementById('watchlist-modal-overlay')?.addEventListener('click', () => this.closeModal());
        document.getElementById('watchlist-modal-save')?.addEventListener('click', () => this.saveWatchlist());

        // Enter key in modal input
        document.getElementById('watchlist-name-input')?.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                this.saveWatchlist();
            }
        });

        // Add to watchlist button
        document.getElementById('add-to-watchlist-btn')?.addEventListener('click', (e) => {
            e.stopPropagation();
            this.toggleWatchlistDropdown();
        });

        // Close dropdown when clicking outside
        document.addEventListener('click', (e) => {
            const dropdown = document.getElementById('watchlist-dropdown');
            const btn = document.getElementById('add-to-watchlist-btn');
            if (dropdown && !dropdown.contains(e.target) && !btn?.contains(e.target)) {
                this.hideWatchlistDropdown();
            }
        });

        // Holdings modal events
        document.getElementById('holdings-modal-close')?.addEventListener('click', () => this.closeHoldingsModal());
        document.getElementById('holdings-modal-cancel')?.addEventListener('click', () => this.closeHoldingsModal());
        document.getElementById('holdings-modal-overlay')?.addEventListener('click', () => this.closeHoldingsModal());
        document.getElementById('holdings-modal-save')?.addEventListener('click', () => this.saveHoldings());

        // Weighting mode change
        document.querySelectorAll('input[name="weighting-mode"]').forEach(radio => {
            radio.addEventListener('change', () => this.updateHoldingsInputs());
        });

        // Holdings ticker search
        const holdingsSearch = document.getElementById('holdings-ticker-search');
        if (holdingsSearch) {
            holdingsSearch.addEventListener('input', (e) => this.handleHoldingsSearch(e.target.value));
            holdingsSearch.addEventListener('blur', () => {
                // Delay hiding to allow click on results
                setTimeout(() => this.hideHoldingsSearchResults(), 200);
            });
        }

        // Combined view modal events
        document.getElementById('combined-view-back')?.addEventListener('click', () => this.closeCombinedView());
        document.getElementById('combined-edit-holdings')?.addEventListener('click', () => {
            const watchlistId = this.combinedView.watchlistId;
            this.holdingsEditor.returnToCombinedView = true;
            // Don't close combined view - holdings modal overlays on top (z-50 vs z-40)
            this.openHoldingsModal(watchlistId);
        });

        // Period buttons
        document.querySelectorAll('#combined-period-buttons button').forEach(btn => {
            btn.addEventListener('click', () => {
                const period = btn.dataset.period;
                if (period) this.changeCombinedPeriod(period);
            });
        });

        // Benchmark buttons
        document.querySelectorAll('#combined-benchmark-buttons button[data-benchmark]').forEach(btn => {
            btn.addEventListener('click', () => {
                const benchmark = btn.dataset.benchmark;
                if (benchmark) this.toggleBenchmark(benchmark);
            });
        });

        document.getElementById('combined-clear-benchmark')?.addEventListener('click', () => this.clearBenchmark());

        // Significant moves toggle
        document.getElementById('combined-show-markers')?.addEventListener('change', (e) => {
            this.combinedView.showMarkers = e.target.checked;
            if (this.combinedView.data) {
                this.renderPortfolioChart(this.combinedView.data);
            }
        });

        // Animal toggle (cats/dogs)
        document.getElementById('combined-animal-toggle')?.addEventListener('change', (e) => {
            this.combinedView.currentAnimal = e.target.checked ? 'dogs' : 'cats';
        });

        // Export/Import buttons
        document.getElementById('export-watchlists-btn')?.addEventListener('click', () => this.exportWatchlists());
        document.getElementById('import-watchlists-input')?.addEventListener('change', (e) => this.importWatchlists(e));

        // Mobile export/import (if present)
        document.getElementById('mobile-export-watchlists-btn')?.addEventListener('click', () => this.exportWatchlists());
        document.getElementById('mobile-import-watchlists-input')?.addEventListener('change', (e) => this.importWatchlists(e));

        // Global Escape key handler for modals
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                this.handleEscapeKey();
            }
        });
    },

    /**
     * Handle Escape key - close the topmost open modal
     * Priority order: Holdings modal > Watchlist modal > Combined view > Dropdown
     */
    handleEscapeKey() {
        // Check modals in z-index order (highest first)
        const holdingsModal = document.getElementById('holdings-modal');
        if (holdingsModal && !holdingsModal.classList.contains('hidden')) {
            this.closeHoldingsModal();
            return;
        }

        const watchlistModal = document.getElementById('watchlist-modal');
        if (watchlistModal && !watchlistModal.classList.contains('hidden')) {
            this.closeModal();
            return;
        }

        const combinedView = document.getElementById('combined-view-modal');
        if (combinedView && !combinedView.classList.contains('hidden')) {
            this.closeCombinedView();
            return;
        }

        // Close dropdown if open
        const dropdown = document.getElementById('watchlist-dropdown');
        if (dropdown && !dropdown.classList.contains('hidden')) {
            this.hideWatchlistDropdown();
            return;
        }
    },

    /**
     * Update storage info display
     */
    updateStorageInfo() {
        const infoEl = document.getElementById('storage-info');
        if (!infoEl || typeof WatchlistStorage === 'undefined') return;

        const info = WatchlistStorage.getStorageInfo();
        infoEl.textContent = `${info.percentage}%`;
        infoEl.title = `localStorage usage: ${(info.used / 1024).toFixed(1)}KB of ${(info.total / 1024 / 1024).toFixed(0)}MB`;
    },

    /**
     * Export watchlists to JSON file
     */
    exportWatchlists() {
        if (typeof WatchlistStorage === 'undefined') {
            console.error('WatchlistStorage not available');
            return;
        }

        const watchlists = WatchlistStorage.getAll();
        if (watchlists.length === 0) {
            alert('No watchlists to export');
            return;
        }

        WatchlistStorage.exportToFile();
    },

    /**
     * Import watchlists from JSON file
     */
    async importWatchlists(event) {
        const file = event.target.files?.[0];
        if (!file) return;

        if (typeof WatchlistStorage === 'undefined') {
            console.error('WatchlistStorage not available');
            return;
        }

        const result = await WatchlistStorage.importFromFile(file);

        if (result.success) {
            alert(result.message);
            await this.loadWatchlists();
            this.updateStorageInfo();
        } else {
            alert(`Import failed: ${result.message}`);
        }

        // Reset file input so same file can be selected again
        event.target.value = '';
    },

    /**
     * Load all watchlists from API
     */
    async loadWatchlists() {
        const loadingEl = document.getElementById('watchlist-loading');
        const emptyEl = document.getElementById('watchlist-empty');
        const containerEl = document.getElementById('watchlist-container');
        const sidebarEl = document.getElementById('watchlist-sidebar');

        if (loadingEl) loadingEl.classList.remove('hidden');
        if (emptyEl) emptyEl.classList.add('hidden');
        if (containerEl) containerEl.innerHTML = '';

        try {
            this.watchlists = await API.getWatchlists();

            if (loadingEl) loadingEl.classList.add('hidden');

            if (this.watchlists.length === 0) {
                if (emptyEl) emptyEl.classList.remove('hidden');
            } else {
                this.renderWatchlists();
            }

            // Show sidebar
            if (sidebarEl) sidebarEl.classList.remove('hidden');

            // Update storage info
            this.updateStorageInfo();

        } catch (error) {
            console.error('Failed to load watchlists:', error);
            if (loadingEl) loadingEl.classList.add('hidden');
            if (emptyEl) emptyEl.classList.remove('hidden');
        }
    },

    /**
     * Render all watchlists
     */
    renderWatchlists() {
        const containerEl = document.getElementById('watchlist-container');
        const emptyEl = document.getElementById('watchlist-empty');

        if (!containerEl) return;

        if (this.watchlists.length === 0) {
            containerEl.innerHTML = '';
            if (emptyEl) emptyEl.classList.remove('hidden');
            return;
        }

        if (emptyEl) emptyEl.classList.add('hidden');

        containerEl.innerHTML = this.watchlists.map(watchlist => this.renderWatchlistItem(watchlist)).join('');

        // Bind watchlist-specific events
        this.bindWatchlistEvents();
    },

    /**
     * Render a single watchlist item
     */
    renderWatchlistItem(watchlist) {
        const isExpanded = this.expandedWatchlists.has(watchlist.id);
        const tickerList = watchlist.tickers.map(ticker => `
            <div class="flex items-center justify-between py-1.5 px-2 hover:bg-gray-100 dark:hover:bg-gray-700 rounded group">
                <button class="text-sm font-medium text-gray-700 dark:text-gray-300 hover:text-primary dark:hover:text-primary watchlist-ticker-btn" data-ticker="${ticker}">
                    ${ticker}
                </button>
                <button class="opacity-0 group-hover:opacity-100 text-gray-400 hover:text-red-500 transition-opacity remove-ticker-btn" data-watchlist-id="${watchlist.id}" data-ticker="${ticker}" title="Remove ${ticker}">
                    <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"></path>
                    </svg>
                </button>
            </div>
        `).join('');

        return `
            <div class="border-b border-gray-200 dark:border-gray-700 last:border-b-0" data-watchlist-id="${watchlist.id}">
                <div class="flex items-center justify-between p-3 cursor-pointer hover:bg-gray-50 dark:hover:bg-gray-700/50 watchlist-header" data-watchlist-id="${watchlist.id}">
                    <div class="flex items-center gap-2 flex-1 min-w-0">
                        <svg class="w-4 h-4 text-gray-400 transition-transform ${isExpanded ? 'rotate-90' : ''} expand-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7"></path>
                        </svg>
                        <span class="font-medium text-gray-900 dark:text-white truncate">${this.escapeHtml(watchlist.name)}</span>
                        <span class="text-xs text-gray-500 dark:text-gray-400">(${watchlist.tickers.length})</span>
                    </div>
                    <div class="flex items-center gap-1">
                        <button class="p-1 text-gray-400 hover:text-primary combined-view-btn ${watchlist.tickers.length === 0 ? 'opacity-30 cursor-not-allowed' : ''}" data-watchlist-id="${watchlist.id}" title="Combined View" ${watchlist.tickers.length === 0 ? 'disabled' : ''}>
                            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 12l3-3 3 3 4-4M8 21l4-4 4 4M3 4h18M4 4h16v12a1 1 0 01-1 1H5a1 1 0 01-1-1V4z"></path>
                            </svg>
                        </button>
                        <button class="p-1 text-gray-400 hover:text-gray-600 dark:hover:text-gray-300 rename-watchlist-btn" data-watchlist-id="${watchlist.id}" title="Rename">
                            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"></path>
                            </svg>
                        </button>
                        <button class="p-1 text-gray-400 hover:text-red-500 delete-watchlist-btn" data-watchlist-id="${watchlist.id}" title="Delete">
                            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"></path>
                            </svg>
                        </button>
                    </div>
                </div>
                <div class="watchlist-tickers ${isExpanded ? '' : 'hidden'} px-3 pb-2">
                    ${watchlist.tickers.length > 0 ? tickerList : '<p class="text-sm text-gray-500 dark:text-gray-400 italic py-2">No tickers yet</p>'}
                </div>
            </div>
        `;
    },

    /**
     * Bind events for watchlist items
     */
    bindWatchlistEvents() {
        // Toggle expand/collapse
        document.querySelectorAll('.watchlist-header').forEach(header => {
            header.addEventListener('click', (e) => {
                // Don't toggle if clicking buttons
                if (e.target.closest('button')) return;

                const watchlistId = header.dataset.watchlistId;
                this.toggleWatchlist(watchlistId);
            });
        });

        // Combined view buttons
        document.querySelectorAll('.combined-view-btn:not([disabled])').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                const watchlistId = btn.dataset.watchlistId;
                this.openCombinedView(watchlistId);
            });
        });

        // Rename buttons
        document.querySelectorAll('.rename-watchlist-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                const watchlistId = btn.dataset.watchlistId;
                this.openRenameModal(watchlistId);
            });
        });

        // Delete buttons
        document.querySelectorAll('.delete-watchlist-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                const watchlistId = btn.dataset.watchlistId;
                this.deleteWatchlist(watchlistId);
            });
        });

        // Ticker click to analyze
        document.querySelectorAll('.watchlist-ticker-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                const ticker = btn.dataset.ticker;
                if (window.App && typeof window.App.analyzeStock === 'function') {
                    window.App.analyzeStock(ticker);
                } else {
                    // Fallback: set input and trigger search
                    const input = document.getElementById('ticker-input');
                    if (input) {
                        input.value = ticker;
                        document.getElementById('search-btn')?.click();
                    }
                }
            });
        });

        // Remove ticker buttons
        document.querySelectorAll('.remove-ticker-btn').forEach(btn => {
            btn.addEventListener('click', async (e) => {
                e.stopPropagation();
                const watchlistId = btn.dataset.watchlistId;
                const ticker = btn.dataset.ticker;
                await this.removeTicker(watchlistId, ticker);
            });
        });
    },

    /**
     * Toggle watchlist expand/collapse
     */
    toggleWatchlist(watchlistId) {
        const container = document.querySelector(`[data-watchlist-id="${watchlistId}"]`);
        if (!container) return;

        const tickersEl = container.querySelector('.watchlist-tickers');
        const iconEl = container.querySelector('.expand-icon');

        if (this.expandedWatchlists.has(watchlistId)) {
            this.expandedWatchlists.delete(watchlistId);
            tickersEl?.classList.add('hidden');
            iconEl?.classList.remove('rotate-90');
        } else {
            this.expandedWatchlists.add(watchlistId);
            tickersEl?.classList.remove('hidden');
            iconEl?.classList.add('rotate-90');
        }
    },

    /**
     * Open create watchlist modal
     */
    openCreateModal() {
        this.editingWatchlistId = null;
        const modal = document.getElementById('watchlist-modal');
        const title = document.getElementById('watchlist-modal-title');
        const input = document.getElementById('watchlist-name-input');

        if (title) title.textContent = 'Create Watchlist';
        if (input) input.value = '';
        if (modal) modal.classList.remove('hidden');

        setTimeout(() => input?.focus(), 100);
    },

    /**
     * Open rename watchlist modal
     */
    openRenameModal(watchlistId) {
        const watchlist = this.watchlists.find(w => w.id === watchlistId);
        if (!watchlist) return;

        this.editingWatchlistId = watchlistId;
        const modal = document.getElementById('watchlist-modal');
        const title = document.getElementById('watchlist-modal-title');
        const input = document.getElementById('watchlist-name-input');

        if (title) title.textContent = 'Rename Watchlist';
        if (input) input.value = watchlist.name;
        if (modal) modal.classList.remove('hidden');

        setTimeout(() => {
            input?.focus();
            input?.select();
        }, 100);
    },

    /**
     * Close modal
     */
    closeModal() {
        const modal = document.getElementById('watchlist-modal');
        if (modal) modal.classList.add('hidden');
        this.editingWatchlistId = null;
        this.pendingTickerToAdd = null; // Clear pending ticker on cancel
    },

    /**
     * Save watchlist (create or rename)
     */
    async saveWatchlist() {
        const input = document.getElementById('watchlist-name-input');
        const name = input?.value?.trim();

        if (!name) {
            input?.focus();
            return;
        }

        // Capture pending ticker BEFORE closeModal() clears it
        const tickerToAdd = this.pendingTickerToAdd;

        try {
            let newWatchlistId = null;

            if (this.editingWatchlistId) {
                await API.renameWatchlist(this.editingWatchlistId, name);
            } else {
                // Creating a new watchlist
                const newWatchlist = await API.createWatchlist(name);
                newWatchlistId = newWatchlist?.id;
            }

            this.closeModal();
            await this.loadWatchlists();

            // If we have a pending ticker to add (from dropdown creation), add it now
            if (tickerToAdd && newWatchlistId) {
                try {
                    await API.addTickerToWatchlist(newWatchlistId, tickerToAdd);
                    await this.loadWatchlists();
                    // Expand the watchlist we just added to
                    this.expandedWatchlists.add(newWatchlistId);
                    this.renderWatchlists();
                } catch (addError) {
                    console.error('Failed to add ticker to new watchlist:', addError);
                }
            }
        } catch (error) {
            console.error('Failed to save watchlist:', error);
            alert('Failed to save watchlist. Please try again.');
        }
    },

    /**
     * Delete a watchlist
     */
    async deleteWatchlist(watchlistId) {
        const watchlist = this.watchlists.find(w => w.id === watchlistId);
        if (!watchlist) return;

        if (!confirm(`Delete "${watchlist.name}"? This cannot be undone.`)) {
            return;
        }

        try {
            await API.deleteWatchlist(watchlistId);
            this.expandedWatchlists.delete(watchlistId);
            await this.loadWatchlists();
        } catch (error) {
            console.error('Failed to delete watchlist:', error);
            alert('Failed to delete watchlist. Please try again.');
        }
    },

    /**
     * Add current stock to a watchlist
     */
    async addCurrentStockToWatchlist(watchlistId) {
        const currentTicker = window.App?.currentTicker;
        if (!currentTicker) {
            alert('No stock selected. Please analyze a stock first.');
            return;
        }

        try {
            await API.addTickerToWatchlist(watchlistId, currentTicker);
            this.hideWatchlistDropdown();
            await this.loadWatchlists();

            // Expand the watchlist we just added to
            this.expandedWatchlists.add(watchlistId);
            this.renderWatchlists();
        } catch (error) {
            console.error('Failed to add ticker to watchlist:', error);
            alert('Failed to add ticker to watchlist. Please try again.');
        }
    },

    /**
     * Remove a ticker from a watchlist
     */
    async removeTicker(watchlistId, ticker) {
        try {
            await API.removeTickerFromWatchlist(watchlistId, ticker);
            await this.loadWatchlists();

            // Keep the watchlist expanded
            this.expandedWatchlists.add(watchlistId);
            this.renderWatchlists();
        } catch (error) {
            console.error('Failed to remove ticker:', error);
            alert('Failed to remove ticker. Please try again.');
        }
    },

    /**
     * Toggle the "Add to Watchlist" dropdown
     */
    toggleWatchlistDropdown() {
        const dropdown = document.getElementById('watchlist-dropdown');
        if (!dropdown) return;

        if (dropdown.classList.contains('hidden')) {
            this.showWatchlistDropdown();
        } else {
            this.hideWatchlistDropdown();
        }
    },

    /**
     * Show the "Add to Watchlist" dropdown
     */
    showWatchlistDropdown() {
        const dropdown = document.getElementById('watchlist-dropdown');
        const itemsContainer = document.getElementById('watchlist-dropdown-items');

        if (!dropdown || !itemsContainer) return;

        // Populate dropdown with watchlists
        if (this.watchlists.length === 0) {
            itemsContainer.innerHTML = '<p class="px-3 py-2 text-sm text-gray-500 dark:text-gray-400">No watchlists yet</p>';
        } else {
            const currentTicker = window.App?.currentTicker?.toUpperCase();

            itemsContainer.innerHTML = this.watchlists.map(watchlist => {
                const hasTicker = watchlist.tickers.some(t => t.toUpperCase() === currentTicker);
                return `
                    <button class="w-full flex items-center justify-between px-3 py-2 text-sm text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700 ${hasTicker ? 'opacity-50 cursor-not-allowed' : ''}"
                            data-watchlist-id="${watchlist.id}"
                            ${hasTicker ? 'disabled' : ''}>
                        <span>${this.escapeHtml(watchlist.name)}</span>
                        ${hasTicker ? '<span class="text-green-500 text-xs">Added</span>' : ''}
                    </button>
                `;
            }).join('');

            // Bind click events
            itemsContainer.querySelectorAll('button:not([disabled])').forEach(btn => {
                btn.addEventListener('click', () => {
                    const watchlistId = btn.dataset.watchlistId;
                    this.addCurrentStockToWatchlist(watchlistId);
                });
            });
        }

        dropdown.classList.remove('hidden');
    },

    /**
     * Hide the "Add to Watchlist" dropdown
     */
    hideWatchlistDropdown() {
        const dropdown = document.getElementById('watchlist-dropdown');
        if (dropdown) dropdown.classList.add('hidden');
    },

    /**
     * Show the "Add to Watchlist" button (called when a stock is loaded)
     */
    showAddToWatchlistButton() {
        const container = document.getElementById('add-to-watchlist-container');
        if (container) container.classList.remove('hidden');
    },

    /**
     * Hide the "Add to Watchlist" button
     */
    hideAddToWatchlistButton() {
        const container = document.getElementById('add-to-watchlist-container');
        if (container) container.classList.add('hidden');
    },

    /**
     * Escape HTML to prevent XSS
     */
    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    },

    // ============================================
    // Combined View Methods
    // ============================================

    /**
     * Open combined view for a watchlist
     */
    async openCombinedView(watchlistId) {
        const watchlist = this.watchlists.find(w => w.id === watchlistId);
        if (!watchlist || watchlist.tickers.length === 0) return;

        this.combinedView.watchlistId = watchlistId;
        this.combinedView.isOpen = true;
        this.combinedView.period = '1y';
        this.combinedView.benchmark = null;

        // Show the modal
        const modal = document.getElementById('combined-view-modal');
        if (modal) modal.classList.remove('hidden');

        // Update title
        const titleEl = document.getElementById('combined-view-title');
        if (titleEl) titleEl.textContent = watchlist.name;

        // Reset UI state
        this.updatePeriodButtons('1y');
        this.updateBenchmarkButtons(null);

        // Reset markers checkbox
        this.combinedView.showMarkers = true;
        const markersCheckbox = document.getElementById('combined-show-markers');
        if (markersCheckbox) markersCheckbox.checked = true;

        // Reset animal toggle to cats
        this.combinedView.currentAnimal = 'cats';
        const animalToggle = document.getElementById('combined-animal-toggle');
        if (animalToggle) animalToggle.checked = false;

        // Fetch and display data
        await this.loadCombinedData();

        // Also load market news
        this.loadMarketNews();
    },

    /**
     * Close combined view
     */
    closeCombinedView() {
        const modal = document.getElementById('combined-view-modal');
        if (modal) modal.classList.add('hidden');
        this.combinedView.isOpen = false;
        // Hide hover card if open
        this.hideCombinedHoverCard();
    },

    /**
     * Load combined portfolio data
     */
    async loadCombinedData() {
        const { watchlistId, period, benchmark } = this.combinedView;
        if (!watchlistId) return;

        try {
            const data = await API.getCombinedPortfolio(watchlistId, period, benchmark);
            this.combinedView.data = data;
            this.renderCombinedView(data);
        } catch (error) {
            console.error('Failed to load combined portfolio:', error);
            const chartEl = document.getElementById('portfolio-chart');
            if (chartEl) {
                chartEl.innerHTML = '<div class="flex items-center justify-center h-full text-gray-500">Failed to load portfolio data</div>';
            }
        }
    },

    /**
     * Render combined view with data
     */
    renderCombinedView(data) {
        // Update return display
        const returnEl = document.getElementById('combined-view-return');
        if (returnEl) {
            const isPositive = data.totalReturn >= 0;
            returnEl.textContent = `${isPositive ? '+' : ''}${data.totalReturn.toFixed(2)}%`;
            returnEl.className = `text-xl font-bold ${isPositive ? 'text-green-500' : 'text-red-500'}`;
        }

        // Render weights
        this.renderWeights(data.tickerWeights);

        // Render chart
        this.renderPortfolioChart(data);
    },

    /**
     * Render portfolio weights
     */
    renderWeights(weights) {
        const container = document.getElementById('combined-weights');
        if (!container) return;

        container.innerHTML = Object.entries(weights)
            .sort((a, b) => b[1] - a[1])
            .map(([ticker, weight]) => `
                <span class="px-2 py-1 bg-gray-100 dark:bg-gray-700 rounded text-sm">
                    <span class="font-medium">${ticker}</span>
                    <span class="text-gray-500 dark:text-gray-400">${weight.toFixed(1)}%</span>
                </span>
            `).join('');
    },

    /**
     * Render portfolio chart using Plotly
     */
    renderPortfolioChart(data) {
        const chartEl = document.getElementById('portfolio-chart');
        if (!chartEl) return;

        const isDark = document.documentElement.classList.contains('dark');
        const themeColors = {
            background: isDark ? '#1F2937' : '#FFFFFF',
            paper: isDark ? '#1F2937' : '#FFFFFF',
            text: isDark ? '#FFFFFF' : '#111827',
            gridColor: isDark ? '#374151' : '#E5E7EB',
            axisColor: isDark ? '#9CA3AF' : '#6B7280'
        };

        const traces = [];

        // Portfolio line
        traces.push({
            type: 'scatter',
            mode: 'lines',
            x: data.data.map(d => d.date),
            y: data.data.map(d => d.percentChange),
            name: data.watchlistName,
            line: { color: '#3B82F6', width: 2.5 },
            fill: 'tozeroy',
            fillcolor: 'rgba(59, 130, 246, 0.1)'
        });

        // Benchmark if available
        if (data.benchmarkData && data.benchmarkSymbol) {
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: data.benchmarkData.map(d => d.date),
                y: data.benchmarkData.map(d => d.percentChange),
                name: data.benchmarkSymbol,
                line: { color: '#F59E0B', width: 2, dash: 'dash' }
            });
        }

        // Zero baseline
        const dates = data.data.map(d => d.date);
        traces.push({
            type: 'scatter',
            mode: 'lines',
            x: [dates[0], dates[dates.length - 1]],
            y: [0, 0],
            name: 'Baseline',
            line: { color: themeColors.gridColor, width: 1, dash: 'dot' },
            showlegend: false,
            hoverinfo: 'skip'
        });

        // Significant move markers (if enabled and data exists)
        if (this.combinedView.showMarkers && data.significantMoves && data.significantMoves.length > 0) {
            // Create a lookup for percentChange by date
            const dataByDate = {};
            data.data.forEach(d => {
                dataByDate[d.date] = d.percentChange;
            });

            // Positive moves (green triangles pointing up)
            const positiveMoves = data.significantMoves.filter(m => m.isPositive);
            if (positiveMoves.length > 0) {
                traces.push({
                    type: 'scatter',
                    mode: 'markers+text',
                    x: positiveMoves.map(m => m.date),
                    y: positiveMoves.map(m => dataByDate[m.date] || 0),
                    text: positiveMoves.map(m => `+${m.percentChange.toFixed(1)}%`),
                    textposition: 'top center',
                    textfont: { size: 10, color: '#10B981' },
                    name: 'Gains ≥5%',
                    marker: {
                        symbol: 'triangle-up',
                        size: 12,
                        color: '#10B981',
                        line: { color: '#FFFFFF', width: 1 }
                    },
                    hovertemplate: '%{x}<br>+%{customdata:.1f}%<extra>Significant Gain</extra>',
                    customdata: positiveMoves.map(m => m.percentChange)
                });
            }

            // Negative moves (red triangles pointing down)
            const negativeMoves = data.significantMoves.filter(m => !m.isPositive);
            if (negativeMoves.length > 0) {
                traces.push({
                    type: 'scatter',
                    mode: 'markers+text',
                    x: negativeMoves.map(m => m.date),
                    y: negativeMoves.map(m => dataByDate[m.date] || 0),
                    text: negativeMoves.map(m => `${m.percentChange.toFixed(1)}%`),
                    textposition: 'bottom center',
                    textfont: { size: 10, color: '#EF4444' },
                    name: 'Losses ≥5%',
                    marker: {
                        symbol: 'triangle-down',
                        size: 12,
                        color: '#EF4444',
                        line: { color: '#FFFFFF', width: 1 }
                    },
                    hovertemplate: '%{x}<br>%{customdata:.1f}%<extra>Significant Loss</extra>',
                    customdata: negativeMoves.map(m => m.percentChange)
                });
            }
        }

        const layout = {
            title: {
                text: `${data.watchlistName} - ${data.period.toUpperCase()}`,
                font: { size: 16, color: themeColors.text }
            },
            xaxis: {
                rangeslider: { visible: false },
                gridcolor: themeColors.gridColor,
                tickfont: { color: themeColors.axisColor }
            },
            yaxis: {
                title: { text: '% Change', font: { color: themeColors.axisColor } },
                gridcolor: themeColors.gridColor,
                tickfont: { color: themeColors.axisColor },
                ticksuffix: '%'
            },
            plot_bgcolor: themeColors.background,
            paper_bgcolor: themeColors.paper,
            showlegend: true,
            legend: {
                orientation: 'h',
                yanchor: 'top',
                y: -0.1,
                xanchor: 'center',
                x: 0.5,
                font: { color: themeColors.text }
            },
            margin: { t: 50, r: 30, b: 80, l: 60 }
        };

        Plotly.newPlot(chartEl, traces, layout, { responsive: true });

        // Set up hover events for significant move markers
        if (this.combinedView.showMarkers && data.significantMoves && data.significantMoves.length > 0) {
            const plot = chartEl;

            plot.on('plotly_hover', (eventData) => {
                const point = eventData.points[0];
                // Check if this is a significant move marker (by trace name)
                if (point.data.name === 'Gains ≥5%' || point.data.name === 'Losses ≥5%') {
                    // Cancel any pending hide
                    if (this.combinedView.hideTimeout) {
                        clearTimeout(this.combinedView.hideTimeout);
                        this.combinedView.hideTimeout = null;
                    }
                    // Cancel any pending show and reschedule
                    if (this.combinedView.hoverTimeout) clearTimeout(this.combinedView.hoverTimeout);
                    this.combinedView.hoverTimeout = setTimeout(() => {
                        const moveData = {
                            date: point.x,
                            percentChange: point.customdata
                        };
                        this.showCombinedHoverCard(eventData.event, moveData);
                    }, 150);
                }
            });

            plot.on('plotly_unhover', () => {
                if (this.combinedView.hoverTimeout) {
                    clearTimeout(this.combinedView.hoverTimeout);
                    this.combinedView.hoverTimeout = null;
                }
                this.scheduleCombinedHideHoverCard();
            });

            // Setup hover card mouse events (only once)
            this.setupCombinedHoverCardListeners();
        }
    },

    /**
     * Setup hover card mouse event listeners
     */
    setupCombinedHoverCardListeners() {
        const card = document.getElementById('wiki-hover-card');
        if (card && !card.dataset.combinedListenersAttached) {
            card.dataset.combinedListenersAttached = 'true';
            card.addEventListener('mouseenter', () => {
                this.combinedView.isHoverCardHovered = true;
                if (this.combinedView.hideTimeout) {
                    clearTimeout(this.combinedView.hideTimeout);
                    this.combinedView.hideTimeout = null;
                }
            });
            card.addEventListener('mouseleave', () => {
                this.combinedView.isHoverCardHovered = false;
                this.scheduleCombinedHideHoverCard();
            });
        }
    },

    /**
     * Show hover card for combined view significant move
     */
    showCombinedHoverCard(event, moveData) {
        // Cancel any pending hide
        if (this.combinedView.hideTimeout) {
            clearTimeout(this.combinedView.hideTimeout);
            this.combinedView.hideTimeout = null;
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
        dateEl.textContent = moveDate.toLocaleDateString('en-US', {
            weekday: 'long',
            year: 'numeric',
            month: 'long',
            day: 'numeric'
        });

        // Format return
        const isPositive = moveData.percentChange >= 0;
        const magnitude = Math.abs(moveData.percentChange) >= 10 ? 'extreme' :
                          Math.abs(moveData.percentChange) >= 5 ? 'major' : 'significant';
        returnEl.textContent = `${isPositive ? '+' : ''}${moveData.percentChange.toFixed(2)}% ${magnitude} move`;
        returnEl.className = `text-sm font-bold mb-2 ${isPositive ? 'text-green-600' : 'text-red-600'}`;

        // Get a random market news article
        const news = this.combinedView.marketNews.length > 0
            ? this.combinedView.marketNews[Math.floor(Math.random() * this.combinedView.marketNews.length)]
            : null;

        // Get image from App's cache if available
        const getAnimalImage = () => {
            image.onerror = () => {
                image.classList.add('hidden');
                placeholder.classList.remove('hidden');
            };

            // Use combined view's animal preference
            const animal = this.combinedView.currentAnimal;

            // Try to use App's image cache
            if (window.App && typeof window.App.getImageFromCache === 'function') {
                const cachedUrl = window.App.getImageFromCache(animal);
                if (cachedUrl) {
                    image.src = cachedUrl;
                    image.classList.remove('hidden');
                    placeholder.classList.add('hidden');
                    return;
                }
            }

            // Fallback to backend fetch
            const endpoint = animal === 'dogs' ? '/api/images/dog' : '/api/images/cat';
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
        };

        if (news) {
            getAnimalImage();

            headlineEl.textContent = news.headline;
            headlineEl.href = news.url || '#';
            headlineEl.style.display = 'block';

            summaryEl.textContent = news.summary || '';
            summaryEl.style.display = news.summary ? 'block' : 'none';

            const newsDate = new Date(news.publishedAt);
            sourceEl.textContent = `${news.source} • ${newsDate.toLocaleDateString()}`;
        } else {
            getAnimalImage();

            headlineEl.textContent = 'Market news unavailable';
            headlineEl.href = '#';
            headlineEl.style.display = 'block';

            summaryEl.textContent = 'No market news articles are currently available.';
            summaryEl.style.display = 'block';

            sourceEl.textContent = '';
        }

        // Position the card near the cursor
        const x = event.clientX || event.pageX;
        const y = event.clientY || event.pageY;
        const cardWidth = 320;
        const cardHeight = 280;
        const padding = 15;

        let left = x + padding;
        let top = y - cardHeight / 2;

        // Keep within viewport
        if (left + cardWidth > window.innerWidth) {
            left = x - cardWidth - padding;
        }
        if (top < padding) {
            top = padding;
        }
        if (top + cardHeight > window.innerHeight) {
            top = window.innerHeight - cardHeight - padding;
        }

        card.style.left = `${left}px`;
        card.style.top = `${top}px`;
        card.classList.remove('hidden');
    },

    /**
     * Schedule hiding the hover card
     */
    scheduleCombinedHideHoverCard() {
        if (this.combinedView.hideTimeout) {
            clearTimeout(this.combinedView.hideTimeout);
        }
        this.combinedView.hideTimeout = setTimeout(() => {
            if (!this.combinedView.isHoverCardHovered) {
                this.hideCombinedHoverCard();
            }
        }, 300);
    },

    /**
     * Hide the hover card
     */
    hideCombinedHoverCard() {
        const card = document.getElementById('wiki-hover-card');
        if (card) {
            card.classList.add('hidden');
            // Clear image to prevent flash of old image
            const image = document.getElementById('wiki-hover-image');
            if (image) image.src = '';
        }
    },

    /**
     * Change combined view period
     */
    async changeCombinedPeriod(period) {
        this.combinedView.period = period;
        this.updatePeriodButtons(period);
        await this.loadCombinedData();
    },

    /**
     * Toggle benchmark comparison
     */
    async toggleBenchmark(benchmark) {
        if (this.combinedView.benchmark === benchmark) {
            this.clearBenchmark();
        } else {
            this.combinedView.benchmark = benchmark;
            this.updateBenchmarkButtons(benchmark);
            await this.loadCombinedData();
        }
    },

    /**
     * Clear benchmark comparison
     */
    async clearBenchmark() {
        this.combinedView.benchmark = null;
        this.updateBenchmarkButtons(null);
        await this.loadCombinedData();
    },

    /**
     * Update period button styles
     */
    updatePeriodButtons(activePeriod) {
        document.querySelectorAll('#combined-period-buttons button').forEach(btn => {
            const period = btn.dataset.period;
            if (period === activePeriod) {
                btn.className = 'px-3 py-1 text-sm rounded-lg border border-gray-300 dark:border-gray-600 bg-primary text-white';
            } else {
                btn.className = 'px-3 py-1 text-sm rounded-lg border border-gray-300 dark:border-gray-600 hover:bg-gray-100 dark:hover:bg-gray-700 transition-colors';
            }
        });
    },

    /**
     * Update benchmark button styles
     */
    updateBenchmarkButtons(activeBenchmark) {
        const clearBtn = document.getElementById('combined-clear-benchmark');

        document.querySelectorAll('#combined-benchmark-buttons button[data-benchmark]').forEach(btn => {
            const benchmark = btn.dataset.benchmark;
            if (benchmark === activeBenchmark) {
                btn.className = 'px-3 py-1 text-sm rounded-lg border border-orange-400 bg-orange-100 dark:bg-orange-900/30 text-orange-600 dark:text-orange-400';
            } else {
                btn.className = 'px-3 py-1 text-sm rounded-lg border border-gray-300 dark:border-gray-600 hover:bg-gray-100 dark:hover:bg-gray-700 transition-colors';
            }
        });

        if (clearBtn) {
            clearBtn.classList.toggle('hidden', !activeBenchmark);
        }
    },

    /**
     * Load and display market news
     */
    async loadMarketNews() {
        const container = document.getElementById('market-news-list');
        if (!container) return;

        container.innerHTML = '<div class="text-center text-gray-500">Loading market news...</div>';

        try {
            const result = await API.getMarketNews('general');
            // Store news in state for hover cards
            this.combinedView.marketNews = result.articles || [];

            if (result.articles && result.articles.length > 0) {
                container.innerHTML = result.articles.slice(0, 5).map(article => `
                    <div class="border-b border-gray-200 dark:border-gray-700 last:border-b-0 pb-4 last:pb-0">
                        <a href="${article.url}" target="_blank" rel="noopener noreferrer"
                           class="text-sm font-medium text-gray-900 dark:text-white hover:text-primary line-clamp-2">
                            ${this.escapeHtml(article.headline)}
                        </a>
                        <div class="flex items-center gap-2 mt-1 text-xs text-gray-500 dark:text-gray-400">
                            <span>${article.source}</span>
                            <span>&bull;</span>
                            <span>${this.formatRelativeTime(article.publishedAt)}</span>
                        </div>
                        ${article.summary ? `<p class="text-sm text-gray-600 dark:text-gray-300 mt-1 line-clamp-2">${this.escapeHtml(article.summary)}</p>` : ''}
                    </div>
                `).join('');
            } else {
                container.innerHTML = '<div class="text-center text-gray-500">No market news available</div>';
            }
        } catch (error) {
            console.error('Failed to load market news:', error);
            this.combinedView.marketNews = [];
            container.innerHTML = '<div class="text-center text-gray-500">Failed to load market news</div>';
        }
    },

    /**
     * Format relative time
     */
    formatRelativeTime(dateStr) {
        const date = new Date(dateStr);
        const now = new Date();
        const diffMs = now - date;
        const diffHours = Math.floor(diffMs / (1000 * 60 * 60));
        const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24));

        if (diffHours < 1) return 'Just now';
        if (diffHours < 24) return `${diffHours}h ago`;
        if (diffDays < 7) return `${diffDays}d ago`;
        return date.toLocaleDateString();
    },

    // ============================================
    // Holdings Editor Methods
    // ============================================

    /**
     * Open holdings editor modal
     */
    async openHoldingsModal(watchlistId) {
        const watchlist = this.watchlists.find(w => w.id === watchlistId);
        if (!watchlist) return;

        this.holdingsEditor.watchlistId = watchlistId;
        // Create local copy of tickers for editing
        this.holdingsEditor.localTickers = [...watchlist.tickers];

        // Update modal title
        const title = document.getElementById('holdings-modal-title');
        if (title) title.textContent = `Edit Holdings - ${watchlist.name}`;

        // Set weighting mode radio
        const mode = watchlist.weightingMode || 'equal';
        const radio = document.querySelector(`input[name="weighting-mode"][value="${mode}"]`);
        if (radio) radio.checked = true;

        // Clear search input
        const searchInput = document.getElementById('holdings-ticker-search');
        if (searchInput) searchInput.value = '';
        this.hideHoldingsSearchResults();

        // Fetch current quotes
        try {
            const quotes = await API.getWatchlistQuotes(watchlistId);
            this.holdingsEditor.quotes = quotes;
        } catch (error) {
            console.error('Failed to fetch quotes:', error);
            this.holdingsEditor.quotes = null;
        }

        // Render holdings inputs
        this.renderHoldingsInputs(watchlist);

        // Show modal
        const modal = document.getElementById('holdings-modal');
        if (modal) modal.classList.remove('hidden');
    },

    /**
     * Close holdings modal
     */
    closeHoldingsModal() {
        const modal = document.getElementById('holdings-modal');
        if (modal) modal.classList.add('hidden');

        // Combined view is already visible underneath if we came from there
        // Just clean up state
        this.holdingsEditor.returnToCombinedView = false;
        this.holdingsEditor.watchlistId = null;
        this.holdingsEditor.quotes = null;
        this.holdingsEditor.localTickers = [];
    },

    /**
     * Render holdings input fields
     */
    renderHoldingsInputs(watchlist) {
        const container = document.getElementById('holdings-inputs');
        if (!container) return;

        const mode = document.querySelector('input[name="weighting-mode"]:checked')?.value || 'equal';
        const quotes = this.holdingsEditor.quotes?.quotes || [];
        const tickers = this.holdingsEditor.localTickers;

        if (tickers.length === 0) {
            container.innerHTML = '<p class="text-sm text-gray-500 dark:text-gray-400 italic py-4 text-center">No tickers. Use the search above to add some.</p>';
            return;
        }

        container.innerHTML = tickers.map(ticker => {
            const quote = quotes.find(q => q.symbol === ticker);
            const holding = watchlist.holdings?.find(h => h.ticker === ticker);
            const price = quote?.price;
            const priceStr = price ? `$${price.toFixed(2)}` : 'N/A';

            let inputHtml = '';
            if (mode === 'shares') {
                const shares = holding?.shares ?? '';
                inputHtml = `
                    <input type="number" class="holding-input w-20 px-2 py-1 text-sm border border-gray-300 dark:border-gray-600 dark:bg-gray-700 rounded"
                           data-ticker="${ticker}" data-type="shares" value="${shares}" min="0" step="any" placeholder="Shares">
                `;
            } else if (mode === 'dollars') {
                const dollars = holding?.dollarValue ?? '';
                inputHtml = `
                    <input type="number" class="holding-input w-20 px-2 py-1 text-sm border border-gray-300 dark:border-gray-600 dark:bg-gray-700 rounded"
                           data-ticker="${ticker}" data-type="dollars" value="${dollars}" min="0" step="any" placeholder="$">
                `;
            } else {
                inputHtml = `<span class="text-sm text-gray-500">Equal</span>`;
            }

            return `
                <div class="flex items-center justify-between py-2 px-2 border-b border-gray-100 dark:border-gray-700 last:border-b-0 hover:bg-gray-50 dark:hover:bg-gray-700/50 rounded">
                    <div class="flex items-center gap-2">
                        <button class="holdings-remove-ticker text-gray-400 hover:text-red-500 transition-colors" data-ticker="${ticker}" title="Remove ${ticker}">
                            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"></path>
                            </svg>
                        </button>
                        <span class="font-medium text-gray-900 dark:text-white">${ticker}</span>
                        <span class="text-sm text-gray-500 dark:text-gray-400">${priceStr}</span>
                    </div>
                    ${inputHtml}
                </div>
            `;
        }).join('');

        // Bind remove ticker events
        container.querySelectorAll('.holdings-remove-ticker').forEach(btn => {
            btn.addEventListener('click', () => {
                const ticker = btn.dataset.ticker;
                this.removeTickerFromHoldings(ticker);
            });
        });
    },

    /**
     * Update holdings inputs when weighting mode changes
     */
    updateHoldingsInputs() {
        const watchlist = this.watchlists.find(w => w.id === this.holdingsEditor.watchlistId);
        if (watchlist) {
            this.renderHoldingsInputs(watchlist);
        }
    },

    /**
     * Save holdings from modal
     */
    async saveHoldings() {
        const watchlistId = this.holdingsEditor.watchlistId;
        if (!watchlistId) return;

        const watchlist = this.watchlists.find(w => w.id === watchlistId);
        if (!watchlist) return;

        const mode = document.querySelector('input[name="weighting-mode"]:checked')?.value || 'equal';
        const holdings = [];
        const localTickers = this.holdingsEditor.localTickers;
        const shouldReturnToCombined = this.holdingsEditor.returnToCombinedView;

        // Build holdings from inputs
        if (mode !== 'equal') {
            document.querySelectorAll('.holding-input').forEach(input => {
                const ticker = input.dataset.ticker;
                const type = input.dataset.type;
                const value = parseFloat(input.value) || 0;

                if (value > 0) {
                    holdings.push({
                        ticker,
                        shares: type === 'shares' ? value : null,
                        dollarValue: type === 'dollars' ? value : null
                    });
                }
            });
        }

        try {
            // First, sync tickers (add new, remove deleted)
            const originalTickers = new Set(watchlist.tickers);
            const newTickers = new Set(localTickers);

            // Add new tickers
            for (const ticker of localTickers) {
                if (!originalTickers.has(ticker)) {
                    await API.addTickerToWatchlist(watchlistId, ticker);
                }
            }

            // Remove deleted tickers
            for (const ticker of watchlist.tickers) {
                if (!newTickers.has(ticker)) {
                    await API.removeTickerFromWatchlist(watchlistId, ticker);
                }
            }

            // Update holdings
            await API.updateWatchlistHoldings(watchlistId, mode, holdings);

            // Close modal
            const modal = document.getElementById('holdings-modal');
            if (modal) modal.classList.add('hidden');

            // Reload watchlists
            await this.loadWatchlists();

            // Refresh combined view if it's open (it's visible underneath the modal)
            if (shouldReturnToCombined && this.combinedView.isOpen) {
                // Update the title in case watchlist name changed
                const updatedWatchlist = this.watchlists.find(w => w.id === watchlistId);
                if (updatedWatchlist) {
                    const titleEl = document.getElementById('combined-view-title');
                    if (titleEl) titleEl.textContent = updatedWatchlist.name;
                }
                // Reload the chart data
                await this.loadCombinedData();
            }

            // Clear holdings editor state
            this.holdingsEditor.returnToCombinedView = false;
            this.holdingsEditor.watchlistId = null;
            this.holdingsEditor.quotes = null;
            this.holdingsEditor.localTickers = [];
        } catch (error) {
            console.error('Failed to save holdings:', error);
            alert('Failed to save holdings. Please try again.');
        }
    },

    /**
     * Handle ticker search in holdings modal
     */
    handleHoldingsSearch(query) {
        // Debounce search
        if (this.holdingsEditor.searchTimeout) {
            clearTimeout(this.holdingsEditor.searchTimeout);
        }

        if (!query || query.length < 1) {
            this.hideHoldingsSearchResults();
            return;
        }

        this.holdingsEditor.searchTimeout = setTimeout(async () => {
            try {
                const results = await API.search(query);
                this.showHoldingsSearchResults(results);
            } catch (error) {
                console.error('Search failed:', error);
                this.hideHoldingsSearchResults();
            }
        }, 300);
    },

    /**
     * Show search results dropdown in holdings modal
     */
    showHoldingsSearchResults(results) {
        const container = document.getElementById('holdings-search-results');
        if (!container) return;

        if (!results || results.length === 0) {
            container.innerHTML = '<div class="px-3 py-2 text-sm text-gray-500">No results found</div>';
            container.classList.remove('hidden');
            return;
        }

        // Filter out tickers already in the list
        const existingTickers = new Set(this.holdingsEditor.localTickers.map(t => t.toUpperCase()));
        const filteredResults = results.filter(r => !existingTickers.has(r.symbol.toUpperCase()));

        if (filteredResults.length === 0) {
            container.innerHTML = '<div class="px-3 py-2 text-sm text-gray-500">All matching tickers already added</div>';
            container.classList.remove('hidden');
            return;
        }

        container.innerHTML = filteredResults.slice(0, 5).map(result => `
            <button class="holdings-search-result w-full text-left px-3 py-2 hover:bg-gray-100 dark:hover:bg-gray-600 flex justify-between items-center"
                    data-symbol="${result.symbol}">
                <span class="font-medium text-gray-900 dark:text-white">${result.symbol}</span>
                <span class="text-sm text-gray-500 dark:text-gray-400 truncate ml-2">${result.name || ''}</span>
            </button>
        `).join('');

        // Bind click events
        container.querySelectorAll('.holdings-search-result').forEach(btn => {
            btn.addEventListener('click', () => {
                const symbol = btn.dataset.symbol;
                this.addTickerToHoldings(symbol);
            });
        });

        container.classList.remove('hidden');
    },

    /**
     * Hide holdings search results
     */
    hideHoldingsSearchResults() {
        const container = document.getElementById('holdings-search-results');
        if (container) container.classList.add('hidden');
    },

    /**
     * Add a ticker to the local holdings list
     */
    async addTickerToHoldings(ticker) {
        if (!ticker) return;

        const upperTicker = ticker.toUpperCase();
        if (this.holdingsEditor.localTickers.includes(upperTicker)) {
            return; // Already in list
        }

        // Add to local tickers
        this.holdingsEditor.localTickers.push(upperTicker);

        // Clear search
        const searchInput = document.getElementById('holdings-ticker-search');
        if (searchInput) searchInput.value = '';
        this.hideHoldingsSearchResults();

        // Fetch quote for new ticker and update quotes
        try {
            const stockInfo = await API.getStockInfo(upperTicker);
            if (stockInfo && this.holdingsEditor.quotes) {
                this.holdingsEditor.quotes.quotes = this.holdingsEditor.quotes.quotes || [];
                this.holdingsEditor.quotes.quotes.push({
                    symbol: upperTicker,
                    price: stockInfo.currentPrice,
                    change: stockInfo.dayChange,
                    changePercent: stockInfo.dayChangePercent
                });
            }
        } catch (error) {
            console.error('Failed to fetch quote for new ticker:', error);
        }

        // Re-render the holdings inputs
        const watchlist = this.watchlists.find(w => w.id === this.holdingsEditor.watchlistId);
        if (watchlist) {
            this.renderHoldingsInputs(watchlist);
        }
    },

    /**
     * Remove a ticker from the local holdings list
     */
    removeTickerFromHoldings(ticker) {
        const index = this.holdingsEditor.localTickers.indexOf(ticker);
        if (index > -1) {
            this.holdingsEditor.localTickers.splice(index, 1);
        }

        // Re-render the holdings inputs
        const watchlist = this.watchlists.find(w => w.id === this.holdingsEditor.watchlistId);
        if (watchlist) {
            this.renderHoldingsInputs(watchlist);
        }
    }
};

// Initialize when DOM is ready
document.addEventListener('DOMContentLoaded', () => {
    Watchlist.init();
});
