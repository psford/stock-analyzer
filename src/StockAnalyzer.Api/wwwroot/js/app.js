/**
 * Stock Analyzer Application
 * Main application logic
 */
const App = {
    currentTicker: null,
    endDatePreset: 'PBD',
    startDatePreset: '1y',
    resolvedEndDate: null,
    resolvedStartDate: null,
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

    // Desktop detection: use flatpickr instead of native date picker
    usesFlatpickr: window.matchMedia('(pointer: fine)').matches && typeof flatpickr !== 'undefined',
    flatpickrInstances: { end: null, start: null },

    // Cache for pre-fetched news (keyed by "TICKER:YYYY-MM-DD:up/down")
    newsCache: {},

    // Keyboard navigation state for search dropdowns
    searchSelectedIndex: -1,
    compareSelectedIndex: -1,

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
    IMAGE_CACHE_SIZE: 20,  // Reduced from 50 - refills as needed
    INITIAL_IMAGE_CACHE_SIZE: 5,  // Start with just 5 to avoid blocking chart data
    IMAGE_CACHE_THRESHOLD: 10,

    // Track user activity for idle-time operations
    lastUserActivity: Date.now(),
    idleImageLoaderId: null,

    // Application version (fetched from API)
    appVersion: null,

    /**
     * Parse a date string in various US-format styles into YYYY-MM-DD.
     * Supports: 3/3/2023, 03/03/23, 3-mar-2023, mar 3 2023, march 3, 2023,
     *           2023-03-03 (ISO), etc. Does NOT handle EU DD-MM-YYYY.
     * Returns null if unparseable.
     */
    parseDateFlexible(str) {
        if (!str || typeof str !== 'string') return null;
        str = str.trim();
        if (!str) return null;

        const monthNames = {
            jan: 0, january: 0, feb: 1, february: 1, mar: 2, march: 2,
            apr: 3, april: 3, may: 4, jun: 5, june: 5,
            jul: 6, july: 6, aug: 7, august: 7, sep: 8, september: 8,
            oct: 9, october: 9, nov: 10, november: 10, dec: 11, december: 11
        };

        const expandYear = (y) => {
            if (y < 100) return y < 50 ? 2000 + y : 1900 + y;
            return y;
        };

        const pad = (n) => String(n).padStart(2, '0');

        const toYMD = (year, month, day) => {
            year = expandYear(year);
            const d = new Date(year, month, day);
            if (isNaN(d.getTime()) || d.getMonth() !== month) return null;
            return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
        };

        // ISO: 2023-03-03 or 2023/03/03
        const iso = str.match(/^(\d{4})[-/](\d{1,2})[-/](\d{1,2})$/);
        if (iso) return toYMD(+iso[1], +iso[2] - 1, +iso[3]);

        // US numeric: M/D/YYYY or M-D-YYYY or M.D.YYYY (also 2-digit year)
        const us = str.match(/^(\d{1,2})[-/.](\d{1,2})[-/.](\d{2,4})$/);
        if (us) return toYMD(+us[3], +us[1] - 1, +us[2]);

        // Day-MonthName-Year: 3-mar-2023, 3 mar 2023
        const dmy = str.match(/^(\d{1,2})[-\s]+([a-z]+)[-,\s]+(\d{2,4})$/i);
        if (dmy) {
            const m = monthNames[dmy[2].toLowerCase()];
            if (m !== undefined) return toYMD(+dmy[3], m, +dmy[1]);
        }

        // MonthName Day Year: mar 3 2023, march 3, 2023
        const mdy = str.match(/^([a-z]+)[-\s]+(\d{1,2})[-,\s]+(\d{2,4})$/i);
        if (mdy) {
            const m = monthNames[mdy[1].toLowerCase()];
            if (m !== undefined) return toYMD(+mdy[3], m, +mdy[2]);
        }

        // MonthName Day, Year: "March 3, 2023"
        const mdyComma = str.match(/^([a-z]+)\s+(\d{1,2}),?\s+(\d{2,4})$/i);
        if (mdyComma) {
            const m = monthNames[mdyComma[1].toLowerCase()];
            if (m !== undefined) return toYMD(+mdyComma[3], m, +mdyComma[2]);
        }

        return null;
    },

    /**
     * Initialize the application
     */
    init() {
        this.initDarkMode();
        this.initMobileSidebar();
        this.bindEvents();
        this.checkApiHealth();
        this.trackUserActivity();
        this.fetchAppVersion();
        // Start idle-time image cache building (doesn't block anything)
        this.scheduleIdleImageLoad();
        // Initialize date range panel with resolved defaults
        this.initDateRangePanel();
    },

    /**
     * Initialize the End Date / Start Date panel on page load.
     * Resolves default dates and handles browser form restoration.
     */
    initDateRangePanel() {
        const endSelect = document.getElementById('end-date-preset');
        const startSelect = document.getElementById('start-date-preset');
        const endInput = document.getElementById('end-date-resolved');
        const startInput = document.getElementById('start-date-resolved');

        // Check browser-restored values
        this.endDatePreset = endSelect.value || 'PBD';
        this.startDatePreset = startSelect.value || '1y';

        if (this.endDatePreset === 'custom' && endInput.value) {
            // Browser restored "custom" end date
            this.setDateInputEditable(endInput, true);
            this.resolvedEndDate = endInput.value;
        } else {
            this.resolvedEndDate = this.resolveEndDate(this.endDatePreset);
            endInput.value = this.resolvedEndDate;
        }

        if (this.startDatePreset === 'custom' && startInput.value) {
            // Browser restored "custom" start date
            this.setDateInputEditable(startInput, true);
            this.resolvedStartDate = startInput.value;
        } else {
            this.resolvedStartDate = this.resolveStartDate(this.startDatePreset, this.resolvedEndDate);
            startInput.value = this.resolvedStartDate;
        }
    },

    // ── Date Resolution Functions ──────────────────────────────────────

    /**
     * Format a Date object as YYYY-MM-DD string (local timezone)
     */
    formatDateYMD(d) {
        const year = d.getFullYear();
        const month = String(d.getMonth() + 1).padStart(2, '0');
        const day = String(d.getDate()).padStart(2, '0');
        return `${year}-${month}-${day}`;
    },

    /**
     * Resolve an End Date preset to a YYYY-MM-DD string
     */
    resolveEndDate(preset) {
        const today = new Date();
        switch (preset) {
            case 'PBD': return this.priorBusinessDay(today);
            case 'LME': return this.lastMonthEnd(today);
            case 'LQE': return this.lastQuarterEnd(today);
            case 'LYE': return this.lastYearEnd(today);
            default: return this.priorBusinessDay(today);
        }
    },

    /**
     * Prior business day: walk back from date, skipping weekends
     */
    priorBusinessDay(date) {
        const d = new Date(date);
        d.setDate(d.getDate() - 1);
        while (d.getDay() === 0 || d.getDay() === 6) d.setDate(d.getDate() - 1);
        return this.formatDateYMD(d);
    },

    /**
     * Last month end: last day of the previous month
     */
    lastMonthEnd(date) {
        return this.formatDateYMD(new Date(date.getFullYear(), date.getMonth(), 0));
    },

    /**
     * Last quarter end: most recent Mar 31, Jun 30, Sep 30, or Dec 31 before today
     */
    lastQuarterEnd(date) {
        const year = date.getFullYear();
        const qEnds = [
            new Date(year - 1, 11, 31), // Dec 31 prior year
            new Date(year, 2, 31),      // Mar 31
            new Date(year, 5, 30),      // Jun 30
            new Date(year, 8, 30),      // Sep 30
            new Date(year, 11, 31)      // Dec 31
        ];
        const past = qEnds.filter(d => d < date).sort((a, b) => b - a);
        return this.formatDateYMD(past[0]);
    },

    /**
     * Last year end: Dec 31 of the prior year
     */
    lastYearEnd(date) {
        return this.formatDateYMD(new Date(date.getFullYear() - 1, 11, 31));
    },

    /**
     * Resolve a Start Date preset relative to a resolved end date (YYYY-MM-DD).
     * Periods are INCLUSIVE: 1Y ending 12/31/2025 = start 1/1/2025
     */
    resolveStartDate(preset, endDateStr) {
        const end = new Date(endDateStr + 'T00:00:00'); // local timezone

        // Year-based: subtract N years, +1 day (inclusive)
        const yearPeriods = { '1y': 1, '2y': 2, '5y': 5, '10y': 10, '15y': 15, '20y': 20, '30y': 30 };
        if (yearPeriods[preset]) {
            const d = new Date(end);
            d.setFullYear(d.getFullYear() - yearPeriods[preset]);
            d.setDate(d.getDate() + 1);
            return this.formatDateYMD(d);
        }

        // Month-based: subtract N months, +1 day (inclusive)
        const monthPeriods = { '1mo': 1, '3mo': 3, '6mo': 6 };
        if (monthPeriods[preset]) {
            const d = new Date(end);
            d.setMonth(d.getMonth() - monthPeriods[preset]);
            d.setDate(d.getDate() + 1);
            return this.formatDateYMD(d);
        }

        // Day-based (inclusive: 5 days = end - 4)
        if (preset === '1d') return endDateStr;
        if (preset === '5d') {
            const d = new Date(end);
            d.setDate(d.getDate() - 4);
            return this.formatDateYMD(d);
        }

        // MTD: first of the end date's month
        if (preset === 'mtd') return this.formatDateYMD(new Date(end.getFullYear(), end.getMonth(), 1));

        // YTD: first of the end date's year
        if (preset === 'ytd') return this.formatDateYMD(new Date(end.getFullYear(), 0, 1));

        // Max: earliest possible
        if (preset === 'max') return '1900-01-01';

        return endDateStr;
    },

    /**
     * Recalculate start date when end date changes (unless start is custom)
     */
    recalculateStartDate() {
        if (!this.resolvedEndDate) return;
        const startInput = document.getElementById('start-date-resolved');

        if (this.startDatePreset === 'custom') {
            // Validate custom start isn't after end
            if (this.resolvedStartDate > this.resolvedEndDate) {
                this.resolvedStartDate = this.resolvedEndDate;
                startInput.value = this.resolvedStartDate;
            }
            return;
        }

        this.resolvedStartDate = this.resolveStartDate(this.startDatePreset, this.resolvedEndDate);
        startInput.value = this.resolvedStartDate;
    },

    /**
     * Toggle a date input between read-only (gray) and editable (white).
     * On desktop (pointer: fine), uses flatpickr for custom date selection.
     * On mobile, falls back to the native date picker.
     */
    setDateInputEditable(input, editable) {
        const key = input.id === 'end-date-resolved' ? 'end' : 'start';

        if (editable) {
            input.removeAttribute('readonly');
            input.classList.remove('bg-gray-50', 'cursor-default');
            input.classList.add('bg-white', 'cursor-text');

            // Initialize flatpickr on desktop
            if (this.usesFlatpickr && !this.flatpickrInstances[key]) {
                // Switch to text input so native date picker doesn't conflict
                input.type = 'text';
                this.flatpickrInstances[key] = flatpickr(input, {
                    dateFormat: 'Y-m-d',
                    defaultDate: input.value || undefined,
                    allowInput: true,
                    parseDate: (dateStr) => {
                        const ymd = this.parseDateFlexible(dateStr);
                        return ymd ? new Date(ymd + 'T00:00:00') : null;
                    },
                    onChange: (selectedDates, dateStr) => {
                        input.value = dateStr;
                        input.dispatchEvent(new Event('change'));
                    }
                });
            }
        } else {
            // Destroy flatpickr instance if active
            if (this.flatpickrInstances[key]) {
                this.flatpickrInstances[key].destroy();
                this.flatpickrInstances[key] = null;
                // Restore native date input for mobile compatibility
                input.type = 'date';
            }
            input.setAttribute('readonly', true);
            input.classList.remove('bg-white', 'cursor-text');
            input.classList.add('bg-gray-50', 'cursor-default');
        }
    },

    /**
     * Re-fetch chart data when date range changes (if a stock is loaded)
     */
    async triggerReanalysis() {
        if (!this.currentTicker || !this.resolvedStartDate || !this.resolvedEndDate) return;
        if (this.resolvedStartDate > this.resolvedEndDate) return;

        if (this.comparisonTicker) {
            const comparisonTicker = this.comparisonTicker;
            this.comparisonTicker = null;
            this.comparisonHistoryData = null;
            await this.analyzeStock();
            await this.setComparison(comparisonTicker);
        } else {
            await this.analyzeStock();
        }
    },

    /**
     * Fetch and display application version from API
     */
    async fetchAppVersion() {
        try {
            const response = await fetch('/api/version');
            if (response.ok) {
                const data = await response.json();
                this.appVersion = data.version;
                const versionEl = document.getElementById('app-version');
                if (versionEl) {
                    versionEl.textContent = `v${data.version}`;
                }
            }
        } catch (e) {
            // Silent fail - version display is non-critical
            console.debug('Could not fetch app version:', e);
        }
    },

    /**
     * Track user activity to know when we're idle
     */
    trackUserActivity() {
        const updateActivity = () => { this.lastUserActivity = Date.now(); };
        document.addEventListener('click', updateActivity);
        document.addEventListener('keydown', updateActivity);
        document.addEventListener('mousemove', updateActivity, { passive: true });
        document.addEventListener('scroll', updateActivity, { passive: true });
    },

    /**
     * Check if the user has been idle for at least the specified duration
     */
    isUserIdle(idleMs = 2000) {
        return Date.now() - this.lastUserActivity > idleMs;
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
     * Schedule idle-time image cache building using requestIdleCallback.
     * Only loads images when user is idle, never blocks critical operations.
     */
    scheduleIdleImageLoad() {
        const loadOneImage = (deadline) => {
            // Check if we have time and user is idle
            const hasTime = deadline.timeRemaining() > 10;
            const isIdle = this.isUserIdle(1500);
            const needsImages = this.imageCache.dogs.length < this.IMAGE_CACHE_SIZE ||
                               this.imageCache.cats.length < this.IMAGE_CACHE_SIZE;

            if (hasTime && isIdle && needsImages && !this.imageCache.isRefilling.dogs && !this.imageCache.isRefilling.cats) {
                // Load one image at a time during idle
                const type = this.imageCache.dogs.length <= this.imageCache.cats.length ? 'dogs' : 'cats';
                this.loadSingleImage(type);
            }

            // Schedule next check (always keep checking)
            this.idleImageLoaderId = requestIdleCallback(loadOneImage, { timeout: 5000 });
        };

        // Start the idle loader
        if ('requestIdleCallback' in window) {
            this.idleImageLoaderId = requestIdleCallback(loadOneImage, { timeout: 5000 });
        } else {
            // Fallback for Safari - use setTimeout with longer delay
            const fallbackLoader = () => {
                if (this.isUserIdle(2000)) {
                    const needsImages = this.imageCache.dogs.length < this.IMAGE_CACHE_SIZE ||
                                       this.imageCache.cats.length < this.IMAGE_CACHE_SIZE;
                    if (needsImages && !this.imageCache.isRefilling.dogs && !this.imageCache.isRefilling.cats) {
                        const type = this.imageCache.dogs.length <= this.imageCache.cats.length ? 'dogs' : 'cats';
                        this.loadSingleImage(type);
                    }
                }
                setTimeout(fallbackLoader, 3000);
            };
            setTimeout(fallbackLoader, 3000);
        }
    },

    /**
     * Load a single image from backend (non-blocking, fire-and-forget).
     */
    loadSingleImage(type) {
        if (this.imageCache[type].length >= this.IMAGE_CACHE_SIZE) return;

        const endpoint = `/api/images/${type === 'dogs' ? 'dog' : 'cat'}?_=${Date.now()}`;

        fetch(endpoint, { cache: 'no-store' })
            .then(async (response) => {
                if (response.ok) {
                    const blob = await response.blob();
                    const url = URL.createObjectURL(blob);
                    this.imageCache[type].push(url);
                }
            })
            .catch(() => { /* Silently ignore - will retry on next idle */ });
    },

    sleep(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
    },

    /**
     * Fetch multiple images from backend (used for refills when cache runs low).
     * Non-blocking - uses setTimeout to yield to other operations.
     */
    async fetchImagesFromBackend(type, count) {
        if (this.imageCache.isRefilling[type]) return;
        this.imageCache.isRefilling[type] = true;

        try {
            const baseEndpoint = `/api/images/${type === 'dogs' ? 'dog' : 'cat'}`;

            // Load images one at a time with delays to avoid blocking
            for (let i = 0; i < count; i++) {
                const endpoint = `${baseEndpoint}?_=${Date.now()}-${i}`;

                try {
                    const response = await fetch(endpoint, { cache: 'no-store' });
                    if (response.ok) {
                        const blob = await response.blob();
                        this.imageCache[type].push(URL.createObjectURL(blob));
                    }
                } catch (e) {
                    // Continue with next image
                }

                // Yield to other operations between each image
                await this.sleep(100);
            }
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

        // Clear button
        document.getElementById('clear-btn').addEventListener('click', () => this.clearAll());

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

        // Keyboard navigation in ticker input (arrows, Enter, Escape)
        tickerInput.addEventListener('keydown', (e) => {
            const items = searchResults.querySelectorAll('.search-result');
            const isOpen = !searchResults.classList.contains('hidden') && items.length > 0;

            if (e.key === 'ArrowDown') {
                if (!isOpen) return;
                e.preventDefault();
                this.searchSelectedIndex = Math.min(this.searchSelectedIndex + 1, items.length - 1);
                this.highlightDropdownItem(items, this.searchSelectedIndex);
            } else if (e.key === 'ArrowUp') {
                if (!isOpen) return;
                e.preventDefault();
                this.searchSelectedIndex = Math.max(this.searchSelectedIndex - 1, -1);
                this.highlightDropdownItem(items, this.searchSelectedIndex);
            } else if (e.key === 'Enter') {
                e.preventDefault();
                if (isOpen && this.searchSelectedIndex >= 0) {
                    // Select highlighted item and analyze
                    const symbol = items[this.searchSelectedIndex].dataset.symbol;
                    tickerInput.value = symbol;
                    this.hideSearchResults();
                    this.analyzeStock();
                } else {
                    // No dropdown selection — trigger analysis
                    this.hideSearchResults();
                    this.analyzeStock();
                }
            } else if (e.key === 'Tab') {
                if (isOpen && this.searchSelectedIndex >= 0) {
                    // Select highlighted item, let Tab advance focus naturally
                    const symbol = items[this.searchSelectedIndex].dataset.symbol;
                    tickerInput.value = symbol;
                    this.hideSearchResults();
                }
            } else if (e.key === 'Escape') {
                this.hideSearchResults();
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

        // End Date preset change
        document.getElementById('end-date-preset').addEventListener('change', (e) => {
            const endInput = document.getElementById('end-date-resolved');
            this.endDatePreset = e.target.value;
            if (e.target.value === 'custom') {
                this.setDateInputEditable(endInput, true);
            } else {
                this.setDateInputEditable(endInput, false);
                this.resolvedEndDate = this.resolveEndDate(e.target.value);
                endInput.value = this.resolvedEndDate;
            }
            this.recalculateStartDate();
            this.triggerReanalysis();
        });

        // End Date resolved input change (custom mode) — debounced to avoid
        // rapid redraws while scrolling through the native date picker calendar.
        // Normalizes flexible date formats (e.g. "3-mar-2023") to YYYY-MM-DD.
        let endDateTimer = null;
        document.getElementById('end-date-resolved').addEventListener('change', (e) => {
            const normalized = this.parseDateFlexible(e.target.value) || e.target.value;
            if (normalized !== e.target.value) e.target.value = normalized;
            this.resolvedEndDate = normalized;
            this.recalculateStartDate();
            clearTimeout(endDateTimer);
            endDateTimer = setTimeout(() => this.triggerReanalysis(), 600);
        });

        // Start Date preset change
        document.getElementById('start-date-preset').addEventListener('change', (e) => {
            const startInput = document.getElementById('start-date-resolved');
            this.startDatePreset = e.target.value;
            if (e.target.value === 'custom') {
                this.setDateInputEditable(startInput, true);
            } else {
                this.setDateInputEditable(startInput, false);
                this.resolvedStartDate = this.resolveStartDate(e.target.value, this.resolvedEndDate);
                startInput.value = this.resolvedStartDate;
            }
            this.triggerReanalysis();
        });

        // Start Date resolved input change (custom mode) — debounced.
        // Normalizes flexible date formats to YYYY-MM-DD.
        let startDateTimer = null;
        document.getElementById('start-date-resolved').addEventListener('change', (e) => {
            const normalized = this.parseDateFlexible(e.target.value) || e.target.value;
            if (normalized !== e.target.value) e.target.value = normalized;
            this.resolvedStartDate = normalized;
            clearTimeout(startDateTimer);
            startDateTimer = setTimeout(() => this.triggerReanalysis(), 600);
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

        // Show markers toggle — also controls cat/dog toggle visibility
        document.getElementById('show-markers').addEventListener('change', (e) => {
            const animalContainer = document.getElementById('animal-toggle-container');
            if (animalContainer) animalContainer.classList.toggle('hidden', !e.target.checked);
            this.updateChart();
        });

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

        // Keyboard navigation in comparison input (arrows, Enter, Escape)
        compareInput.addEventListener('keydown', (e) => {
            const items = compareResults.querySelectorAll('.compare-result');
            const isOpen = !compareResults.classList.contains('hidden') && items.length > 0;

            if (e.key === 'ArrowDown') {
                if (!isOpen) return;
                e.preventDefault();
                this.compareSelectedIndex = Math.min(this.compareSelectedIndex + 1, items.length - 1);
                this.highlightDropdownItem(items, this.compareSelectedIndex);
            } else if (e.key === 'ArrowUp') {
                if (!isOpen) return;
                e.preventDefault();
                this.compareSelectedIndex = Math.max(this.compareSelectedIndex - 1, -1);
                this.highlightDropdownItem(items, this.compareSelectedIndex);
            } else if (e.key === 'Enter') {
                e.preventDefault();
                if (isOpen && this.compareSelectedIndex >= 0) {
                    // Select highlighted item, stay on field
                    const symbol = items[this.compareSelectedIndex].dataset.symbol;
                    compareInput.value = symbol;
                    this.hideCompareResults();
                } else {
                    // No dropdown selection — trigger comparison
                    this.hideCompareResults();
                    const ticker = compareInput.value.trim().toUpperCase();
                    if (ticker && this.currentTicker) {
                        this.setComparison(ticker);
                    }
                }
            } else if (e.key === 'Tab') {
                if (isOpen && this.compareSelectedIndex >= 0) {
                    // Select highlighted item, let Tab advance focus naturally
                    const symbol = items[this.compareSelectedIndex].dataset.symbol;
                    compareInput.value = symbol;
                    this.hideCompareResults();
                }
            } else if (e.key === 'Escape') {
                this.hideCompareResults();
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
        this.searchSelectedIndex = -1;

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
     * Highlight a dropdown item by index (keyboard navigation).
     * Removes highlight from all siblings, adds to the target, and scrolls into view.
     */
    highlightDropdownItem(items, index) {
        items.forEach((item, i) => {
            if (i === index) {
                item.classList.add('bg-gray-100', 'dark:bg-gray-700');
                item.scrollIntoView({ block: 'nearest' });
            } else {
                item.classList.remove('bg-gray-100', 'dark:bg-gray-700');
            }
        });
    },

    /**
     * Hide search results dropdown
     */
    hideSearchResults() {
        document.getElementById('search-results').classList.add('hidden');
        this.searchSelectedIndex = -1;
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
        this.compareSelectedIndex = -1;

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
        this.compareSelectedIndex = -1;
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
            this.comparisonHistoryData = await API.getHistory(
                ticker, null, this.resolvedStartDate, this.resolvedEndDate);
            this.comparisonTicker = ticker;

            // Disable technical indicators (they don't make sense for comparison)
            this.disableIndicators(true);

            // Show clear button
            document.getElementById('clear-compare').classList.remove('hidden');

            // Re-render chart with comparison
            this.renderChart();
            this.attachChartHoverListeners();
            this.attachDragMeasure();
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
            this.attachDragMeasure();
        }
    },

    /**
     * Reset the page to its initial state
     */
    clearAll() {
        // Clear data state
        this.currentTicker = null;
        this.endDatePreset = 'PBD';
        this.startDatePreset = '1y';
        this.historyData = null;
        this.analysisData = null;
        this.significantMovesData = null;
        this.newsCache = {};

        // Clear comparison
        this.comparisonTicker = null;
        this.comparisonHistoryData = null;
        document.getElementById('compare-input').value = '';
        document.getElementById('clear-compare').classList.add('hidden');

        // Reset form inputs
        document.getElementById('ticker-input').value = '';
        document.getElementById('ticker-input').focus();
        document.getElementById('end-date-preset').value = 'PBD';
        document.getElementById('start-date-preset').value = '1y';
        const endInput = document.getElementById('end-date-resolved');
        const startInput = document.getElementById('start-date-resolved');
        this.setDateInputEditable(endInput, false);
        this.setDateInputEditable(startInput, false);
        this.resolvedEndDate = this.resolveEndDate('PBD');
        endInput.value = this.resolvedEndDate;
        this.resolvedStartDate = this.resolveStartDate('1y', this.resolvedEndDate);
        startInput.value = this.resolvedStartDate;
        document.getElementById('chart-type').value = 'line';
        document.getElementById('threshold-slider').value = 5;
        document.getElementById('threshold-value').textContent = '5%';
        this.currentThreshold = 5;

        // Reset checkboxes to defaults
        document.getElementById('ma-20').checked = true;
        document.getElementById('ma-50').checked = true;
        document.getElementById('ma-200').checked = false;
        document.getElementById('show-rsi').checked = false;
        document.getElementById('show-macd').checked = false;
        document.getElementById('show-bollinger').checked = false;
        document.getElementById('show-stochastic').checked = false;
        document.getElementById('show-markers').checked = false;
        const animalContainer = document.getElementById('animal-toggle-container');
        if (animalContainer) animalContainer.classList.add('hidden');

        // Re-enable indicators (in case they were disabled by comparison mode)
        this.disableIndicators(false);

        // Hide results, show nothing
        document.getElementById('results-section').classList.add('hidden');
        document.getElementById('error-section').classList.add('hidden');
        document.getElementById('loading-section').classList.add('hidden');

        // Clear chart
        const chartEl = document.getElementById('stock-chart');
        if (chartEl && chartEl.data) {
            Plotly.purge(chartEl);
        }
        Charts.resetChart('stock-chart');
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
     * Main analysis function - optimized for fast chart display.
     * Priority: history + analysis (for chart) → show chart → load extras
     */
    async analyzeStock() {
        const ticker = document.getElementById('ticker-input').value.trim().toUpperCase();
        if (!ticker) {
            this.showError('Please enter a stock ticker');
            return;
        }

        this.currentTicker = ticker;
        const from = this.resolvedStartDate;
        const to = this.resolvedEndDate;
        this.newsCache = {}; // Clear news cache for new ticker
        if (typeof Charts !== 'undefined' && Charts.resetChart) {
            Charts.resetChart('stock-chart');
        }
        this.showLoading();

        try {
            // PHASE 1: Critical path - single request for history + analysis
            const chartData = await API.getChartData(ticker, null, from, to);

            // Split combined response into history and analysis shapes
            this.historyData = {
                symbol: chartData.symbol,
                period: chartData.period,
                startDate: chartData.startDate,
                endDate: chartData.endDate,
                data: chartData.data,
                minClose: chartData.minClose,
                maxClose: chartData.maxClose,
                averageClose: chartData.averageClose,
                averageVolume: chartData.averageVolume
            };
            this.analysisData = {
                symbol: chartData.symbol,
                period: chartData.period,
                performance: chartData.performance,
                movingAverages: chartData.movingAverages,
                rsi: chartData.rsi,
                macd: chartData.macd,
                bollingerBands: chartData.bollingerBands,
                stochastic: chartData.stochastic
            };

            const analysis = this.analysisData;

            // Render chart immediately - this is what the user is waiting for
            this.renderPerformance(analysis.performance);
            this.renderChart();
            this.showResults();

            // PHASE 2: Load secondary data in background (non-blocking)
            // Start all these requests but don't wait for them
            const stockInfoPromise = API.getStockInfo(ticker);
            // Always use chart data's actual date range (not UI state) to ensure moves match the chart
            const significantMovesPromise = API.getSignificantMoves(
                ticker, this.currentThreshold, null, chartData.startDate, chartData.endDate);
            const newsPromise = API.getAggregatedNews(ticker, 30, 10);

            // Handle stock info when ready
            stockInfoPromise.then(stockInfo => {
                this.renderStockInfo(stockInfo);
                this.renderKeyMetrics(stockInfo);
            }).catch(e => console.warn('Failed to load stock info:', e));

            // Handle significant moves when ready
            significantMovesPromise.then(significantMoves => {
                this.significantMovesData = significantMoves;
                this.renderSignificantMoves(significantMoves);
                // Re-render chart with markers now that we have significant moves
                this.renderChart();
                // Attach listeners AFTER chart is rendered (Plotly replaces the DOM element)
                this.attachChartHoverListeners();
                this.attachDragMeasure();
                // Pre-fetch news for moves AFTER chart is fully rendered
                this.scheduleNewsPrefetch();
            }).catch(e => console.warn('Failed to load significant moves:', e));

            // Handle news when ready
            newsPromise.then(news => {
                this.renderNews(news);
            }).catch(e => console.warn('Failed to load news:', e));

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
            this.attachDragMeasure();
        }
    },

    /**
     * Refresh significant moves with new threshold
     */
    async refreshSignificantMoves() {
        if (!this.currentTicker || !this.historyData) return;

        try {
            // Use chart data's actual date range (not UI state) to ensure moves match the chart
            this.significantMovesData = await API.getSignificantMoves(
                this.currentTicker,
                this.currentThreshold,
                null,
                this.historyData.startDate,
                this.historyData.endDate
            );
            this.renderChart();
            this.attachChartHoverListeners();
            this.attachDragMeasure();
            this.renderSignificantMoves(this.significantMovesData);

            // Pre-fetch news for the new set of moves (deferred)
            this.scheduleNewsPrefetch();
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
     * Attach drag-measure interaction to the stock chart.
     * Left-click drag: performance measurement; Right-click drag: zoom; Scroll: zoom; DblClick: reset.
     */
    attachDragMeasure() {
        if (typeof DragMeasure === 'undefined') return;

        DragMeasure.attach('stock-chart', {
            dataSource: () => this.historyData?.data || [],
            isComparisonMode: () => !!this.comparisonTicker,
            getComparisonData: () => this.comparisonHistoryData ? { data: this.comparisonHistoryData.data } : null,
            getPrimarySymbol: () => this.currentTicker || '',
            getComparisonSymbol: () => this.comparisonTicker || '',
            onRangeExtend: (fromDate, toDate) => this.extendChartRange(fromDate, toDate)
        });
    },

    /**
     * Fetch extended data when scroll zoom exceeds current data bounds.
     * Merges new data with existing, preserving the current zoom position.
     */
    async extendChartRange(fromDate, toDate) {
        if (!this.currentTicker || this._extendingRange) return;
        this._extendingRange = true;

        try {
            const chartData = await API.getChartData(this.currentTicker, null, fromDate, toDate);
            if (!chartData?.data || chartData.data.length === 0) return;

            // Save current zoom range before re-render
            const chartEl = document.getElementById('stock-chart');
            const currentRange = chartEl?._fullLayout?.xaxis?.range;

            // Replace history data with the extended set
            this.historyData = {
                symbol: chartData.symbol,
                period: chartData.period,
                startDate: chartData.startDate,
                endDate: chartData.endDate,
                data: chartData.data,
                minClose: chartData.minClose,
                maxClose: chartData.maxClose,
                averageClose: chartData.averageClose,
                averageVolume: chartData.averageVolume
            };
            this.analysisData = {
                symbol: chartData.symbol,
                period: chartData.period,
                performance: chartData.performance,
                movingAverages: chartData.movingAverages,
                rsi: chartData.rsi,
                macd: chartData.macd,
                bollingerBands: chartData.bollingerBands,
                stochastic: chartData.stochastic
            };

            // Also fetch comparison data for the extended range if comparing
            if (this.comparisonTicker) {
                try {
                    const compData = await API.getChartData(this.comparisonTicker, null, fromDate, toDate);
                    if (compData?.data) {
                        this.comparisonHistoryData = {
                            symbol: compData.symbol,
                            data: compData.data
                        };
                    }
                } catch { /* comparison extension is best-effort */ }
            }

            // Re-render chart with the full extended dataset
            this.renderChart();
            this.attachChartHoverListeners();
            this.attachDragMeasure();

            // Restore the zoom position the user was viewing
            if (currentRange && chartEl) {
                Plotly.relayout(chartEl, { 'xaxis.range': currentRange });
            }
        } catch (error) {
            console.warn('Failed to extend chart range:', error);
        } finally {
            this._extendingRange = false;
        }
    },

    /**
     * Schedule news pre-fetch to run during idle time.
     * Uses requestIdleCallback to avoid blocking user interactions.
     */
    scheduleNewsPrefetch() {
        if (!this.significantMovesData?.moves || !this.currentTicker) return;

        const prefetch = () => {
            // Small delay to ensure chart is fully rendered and visible
            setTimeout(() => this.prefetchNewsForMoves(), 500);
        };

        if ('requestIdleCallback' in window) {
            requestIdleCallback(prefetch, { timeout: 2000 });
        } else {
            // Fallback for Safari
            setTimeout(prefetch, 1000);
        }
    },

    /**
     * Pre-fetch news for all significant moves in the background.
     * Called after chart renders to have news ready before user hovers.
     * Staggers requests to avoid overwhelming the server.
     */
    prefetchNewsForMoves() {
        if (!this.significantMovesData?.moves || !this.currentTicker) return;

        const ticker = this.currentTicker;
        const moves = this.significantMovesData.moves;

        console.log(`Pre-fetching news for ${moves.length} significant moves...`);

        // Stagger requests to avoid overwhelming the server
        let delay = 0;
        const delayIncrement = 200; // 200ms between each request

        moves.forEach(move => {
            const moveDate = new Date(move.date);
            const dateParam = moveDate.toISOString().split('T')[0];
            const direction = move.percentChange >= 0 ? 'up' : 'down';
            const cacheKey = `${ticker}:${dateParam}:${direction}`;

            // Skip if already cached
            if (this.newsCache[cacheKey]) return;

            // Mark as pending to avoid duplicate fetches
            this.newsCache[cacheKey] = { pending: true };

            // Stagger each request
            setTimeout(() => {
                // Check ticker hasn't changed
                if (this.currentTicker !== ticker) return;

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
            }, delay);

            delay += delayIncrement;
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
