/**
 * Stock Analyzer Application
 * Main application logic
 */

// Okabe-Ito color palette for chart series (colorblind-safe)
const SERIES_PALETTE = [
    { color: '#0072B2', dash: 'solid' },     // Position 0: Primary stock (Blue)
    { color: '#E69F00', dash: 'dash' },       // Position 1: Comparison (Orange)
    { color: '#009E73', dash: 'dot' },        // Position 2: Benchmark 1 (Blue-Green)
    { color: '#56B4E9', dash: 'dashdot' },    // Position 3: Benchmark 2 (Sky Blue)
    { color: '#D55E00', dash: 'longdash' },   // Position 4: Benchmark 3 (Vermillion)
    { color: '#CC79A7', dash: 'solid' },      // Position 5: Benchmark 4 (Red-Purple)
    { color: '#F0E442', dash: 'dash' },       // Position 6: Benchmark 5 (Yellow)
];
const MAX_BENCHMARKS = 5;

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

    // Chart series state (replaces comparisonTicker/comparisonHistoryData)
    chartSeries: [],          // Array of {ticker, label, type, data, color, dash}
    compareSearchTimeout: null,
    benchmarkSearchTimeout: null,
    benchmarkSearchSelectedIndex: -1,
    _benchmarkFetchInProgress: false,
    // Preserved indicator state (saved when disabling for multi-series mode)
    savedIndicatorState: null,

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
        this.initMusicToggle();
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
            input.classList.remove('readonly');
            input.classList.add('editable');

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
            input.classList.remove('editable');
            input.classList.add('readonly');
        }
    },

    /**
     * Re-fetch chart data when date range changes (if a stock is loaded)
     */
    async triggerReanalysis() {
        if (!this.currentTicker || !this.resolvedStartDate || !this.resolvedEndDate) return;
        if (this.resolvedStartDate > this.resolvedEndDate) return;

        const comparisonSeries = this.chartSeries.find(s => s.type === 'comparison');
        if (comparisonSeries) {
            const comparisonTicker = comparisonSeries.ticker;
            this.clearComparison();
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
     * Initialize theme system using JSON-based ThemeLoader
     * Dynamically populates dropdown from manifest
     */
    async initDarkMode() {
        const themeBtn = document.getElementById('theme-toggle-btn');
        const themeDropdown = document.getElementById('theme-dropdown');
        const iconLight = document.getElementById('theme-icon-light');
        const iconDark = document.getElementById('theme-icon-dark');
        const iconNeon = document.getElementById('theme-icon-neon');

        // SVG icons for different theme types
        const iconSvgs = {
            sun: `<svg class="icon-md" fill="currentColor" viewBox="0 0 20 20"><path fill-rule="evenodd" d="M10 2a1 1 0 011 1v1a1 1 0 11-2 0V3a1 1 0 011-1zm4 8a4 4 0 11-8 0 4 4 0 018 0z" clip-rule="evenodd"></path></svg>`,
            moon: `<svg class="icon-md" fill="currentColor" viewBox="0 0 20 20"><path d="M17.293 13.293A8 8 0 016.707 2.707a8.001 8.001 0 1010.586 10.586z"></path></svg>`,
            bolt: `<svg class="icon-md" fill="currentColor" viewBox="0 0 20 20"><path fill-rule="evenodd" d="M11.3 1.046A1 1 0 0112 2v5h4a1 1 0 01.82 1.573l-7 10A1 1 0 018 18v-5H4a1 1 0 01-.82-1.573l7-10a1 1 0 011.12-.38z" clip-rule="evenodd"/></svg>`,
            star: `<svg class="icon-md" fill="currentColor" viewBox="0 0 20 20"><path d="M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.07 3.292a1 1 0 00.95.69h3.462c.969 0 1.371 1.24.588 1.81l-2.8 2.034a1 1 0 00-.364 1.118l1.07 3.292c.3.921-.755 1.688-1.54 1.118l-2.8-2.034a1 1 0 00-1.175 0l-2.8 2.034c-.784.57-1.838-.197-1.539-1.118l1.07-3.292a1 1 0 00-.364-1.118L2.98 8.72c-.783-.57-.38-1.81.588-1.81h3.461a1 1 0 00.951-.69l1.07-3.292z"></path></svg>`,
            palette: `<svg class="icon-md" fill="currentColor" viewBox="0 0 20 20"><path fill-rule="evenodd" d="M4 2a2 2 0 00-2 2v11a3 3 0 106 0V4a2 2 0 00-2-2H4zm1 14a1 1 0 100-2 1 1 0 000 2zm5-1.757l4.9-4.9a2 2 0 000-2.828L13.485 5.1a2 2 0 00-2.828 0L10 5.757v8.486zM16 18H9.071l6-6H16a2 2 0 012 2v2a2 2 0 01-2 2z" clip-rule="evenodd"></path></svg>`
        };

        // Initialize ThemeLoader (loads manifest, applies saved theme)
        if (typeof ThemeLoader !== 'undefined') {
            await ThemeLoader.init();
        }

        // Populate dropdown from manifest
        const themes = ThemeLoader?.getAvailableThemes() || [];
        if (themeDropdown && themes.length > 0) {
            const themeButtons = themes.map(theme => {
                const iconSvg = iconSvgs[theme.icon] || iconSvgs.palette;
                const iconColor = theme.iconColor || '#888888';
                return `<button data-theme="${theme.id}" class="theme-option">
                    <span style="color: ${iconColor}">${iconSvg}</span>
                    ${theme.name}
                </button>`;
            }).join('');

            // Add "Import Custom Theme" button at the bottom
            const importBtn = `<button id="theme-import-open-btn" class="theme-import-btn">
                <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12"/>
                </svg>
                Import Custom Theme...
            </button>`;

            themeDropdown.innerHTML = themeButtons + importBtn;
        }

        // Update header icon based on current theme's category
        const updateIcon = (themeId) => {
            iconLight?.classList.add('hidden');
            iconDark?.classList.add('hidden');
            iconNeon?.classList.add('hidden');

            const theme = themes.find(t => t.id === themeId);
            const icon = theme?.icon || 'sun';

            if (icon === 'moon') {
                iconDark?.classList.remove('hidden');
            } else if (icon === 'bolt') {
                iconNeon?.classList.remove('hidden');
            } else {
                iconLight?.classList.remove('hidden');
            }
        };

        // Set initial icon
        const currentThemeId = ThemeLoader?.getCurrentThemeId() || 'light';
        updateIcon(currentThemeId);

        // Listen for theme changes from ThemeLoader
        window.addEventListener('themechange', (e) => {
            updateIcon(e.detail.themeId);
        });

        // Toggle dropdown on button click
        themeBtn?.addEventListener('click', (e) => {
            e.stopPropagation();
            themeDropdown?.classList.toggle('hidden');
        });

        // Close dropdown on outside click
        document.addEventListener('click', () => {
            themeDropdown?.classList.add('hidden');
        });

        // Theme option click handlers (using event delegation for dynamic content)
        themeDropdown?.addEventListener('click', async (e) => {
            const btn = e.target.closest('.theme-option');
            if (!btn) return;
            e.stopPropagation();
            const theme = btn.dataset.theme;
            if (typeof ThemeLoader !== 'undefined') {
                await ThemeLoader.applyTheme(theme);
            }
            themeDropdown?.classList.add('hidden');
        });

        // Theme Import Modal functionality
        this.initThemeImportModal(themeDropdown);
    },

    /**
     * Initialize theme import modal functionality
     */
    initThemeImportModal(themeDropdown) {
        const modal = document.getElementById('theme-import-modal');
        const backdrop = modal?.querySelector('.modal-backdrop');
        const closeBtn = modal?.querySelector('.modal-close');
        const cancelBtn = document.getElementById('theme-import-cancel');
        const applyBtn = document.getElementById('theme-import-apply');
        const downloadBtn = document.getElementById('theme-import-download');
        const textarea = document.getElementById('theme-import-json');
        const errorDiv = document.getElementById('theme-import-error');

        if (!modal || !textarea) return;

        // Store applied custom theme for download
        let appliedTheme = null;

        const openModal = () => {
            modal.classList.remove('hidden');
            textarea.focus();
        };

        const closeModal = () => {
            modal.classList.add('hidden');
            errorDiv?.classList.add('hidden');
        };

        const showError = (msg) => {
            if (errorDiv) {
                errorDiv.textContent = msg;
                errorDiv.classList.remove('hidden');
            }
        };

        const hideError = () => {
            errorDiv?.classList.add('hidden');
        };

        // Open modal when import button clicked
        themeDropdown?.addEventListener('click', (e) => {
            const importBtn = e.target.closest('#theme-import-open-btn');
            if (importBtn) {
                e.stopPropagation();
                themeDropdown?.classList.add('hidden');
                openModal();
            }
        });

        // Close modal handlers
        backdrop?.addEventListener('click', closeModal);
        closeBtn?.addEventListener('click', closeModal);
        cancelBtn?.addEventListener('click', closeModal);

        // Close on Escape key
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && !modal.classList.contains('hidden')) {
                closeModal();
            }
        });

        // Apply theme button
        applyBtn?.addEventListener('click', async () => {
            hideError();
            const jsonStr = textarea.value.trim();

            if (!jsonStr) {
                showError('Please paste theme JSON to apply.');
                return;
            }

            let themeData;
            try {
                themeData = JSON.parse(jsonStr);
            } catch (e) {
                showError(`Invalid JSON: ${e.message}`);
                return;
            }

            // Basic validation
            if (!themeData.id && !themeData.name) {
                showError('Theme must have at least an "id" or "name" property.');
                return;
            }

            // Generate id from name if missing
            if (!themeData.id && themeData.name) {
                themeData.id = themeData.name.toLowerCase().replace(/\s+/g, '-');
            }

            // Apply the theme
            if (typeof ThemeLoader !== 'undefined' && ThemeLoader.applyThemeJson) {
                try {
                    await ThemeLoader.applyThemeJson(themeData);
                    appliedTheme = themeData;
                    downloadBtn.disabled = false;
                    // Show success - keep modal open so user can download
                    applyBtn.textContent = 'Applied!';
                    applyBtn.disabled = true;
                    setTimeout(() => {
                        applyBtn.textContent = 'Apply Theme';
                        applyBtn.disabled = false;
                    }, 2000);
                } catch (e) {
                    showError(`Failed to apply theme: ${e.message}`);
                }
            } else {
                showError('ThemeLoader.applyThemeJson not available.');
            }
        });

        // Download button - exports the applied theme as JSON file
        downloadBtn?.addEventListener('click', () => {
            if (!appliedTheme) return;

            const jsonStr = JSON.stringify(appliedTheme, null, 2);
            const blob = new Blob([jsonStr], { type: 'application/json' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `${appliedTheme.id || 'custom-theme'}.json`;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
        });

        // Ctrl+Enter to apply
        textarea?.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
                e.preventDefault();
                applyBtn?.click();
            }
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
     * Initialize the theme-driven ambient music toggle
     */
    initMusicToggle() {
        const musicBtn = document.getElementById('music-toggle');
        const visualizer = musicBtn?.querySelector('.music-visualizer');
        const volumeSlider = document.getElementById('music-volume');

        // Use ThemeAudio if available, fall back to VaporwaveAudio for compatibility
        const AudioEngine = (typeof ThemeAudio !== 'undefined') ? ThemeAudio :
                           (typeof VaporwaveAudio !== 'undefined') ? VaporwaveAudio : null;

        if (!musicBtn || !AudioEngine) {
            console.debug('Music toggle not initialized (button or audio engine not found)');
            return;
        }

        // Configure audio from current theme if ThemeAudio
        if (AudioEngine.configure && typeof ThemeLoader !== 'undefined') {
            const theme = ThemeLoader.getCurrentTheme();
            if (theme?.audio) {
                AudioEngine.configure(theme.audio);
                console.log('Music configured from theme:', theme.id);
            }
        }

        // Restore state from localStorage
        const savedState = localStorage.getItem('musicEnabled') === 'true';
        const savedVolume = parseInt(localStorage.getItem('musicVolume') || '40', 10);

        // Initialize volume slider with saved value
        if (volumeSlider) {
            volumeSlider.value = savedVolume;
        }

        const updateUI = (playing) => {
            if (playing) {
                musicBtn.classList.add('active');
                musicBtn.title = 'Toggle ambient music (on)';
                visualizer?.classList.remove('hidden');
                volumeSlider?.classList.remove('hidden');
            } else {
                musicBtn.classList.remove('active');
                musicBtn.title = 'Toggle ambient music (off)';
                visualizer?.classList.add('hidden');
                volumeSlider?.classList.add('hidden');
            }
        };

        // Auto-start if previously enabled (requires user interaction first due to browser policy)
        if (savedState) {
            musicBtn.title = 'Click to resume ambient music';
        }

        musicBtn.addEventListener('click', () => {
            if (AudioEngine.isPlaying()) {
                AudioEngine.stop();
                updateUI(false);
                localStorage.setItem('musicEnabled', 'false');
            } else {
                // Apply saved volume before starting
                if (AudioEngine.setVolume) {
                    AudioEngine.setVolume(savedVolume);
                }
                AudioEngine.start();
                updateUI(true);
                localStorage.setItem('musicEnabled', 'true');
            }
        });

        // Volume slider control
        if (volumeSlider && AudioEngine.setVolume) {
            volumeSlider.addEventListener('input', (e) => {
                const vol = parseInt(e.target.value, 10);
                AudioEngine.setVolume(vol);
                localStorage.setItem('musicVolume', vol.toString());
            });
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

        // Clear button
        document.getElementById('clear-btn').addEventListener('click', () => this.clearAll());

        // Track the ticker value when input gains focus (to detect changes on blur)
        let tickerValueOnFocus = '';

        // Autocomplete on input
        tickerInput.addEventListener('input', (e) => {
            const query = e.target.value.trim();
            if (this.searchTimeout) clearTimeout(this.searchTimeout);

            if (query.length < 2) {
                this.hideSearchResults();
                return;
            }

            // Debounce search — fast for Bloomberg-style responsiveness
            this.searchTimeout = setTimeout(() => this.performSearch(query), 150);
        });

        // Keyboard navigation in ticker input (arrows, Enter, Escape, Tab)
        // Bloomberg terminal behavior: Enter/Tab auto-selects top result and triggers analysis
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
            } else if (e.key === 'Enter' || e.key === 'Tab') {
                // Bloomberg behavior: commit selection and analyze
                if (isOpen) {
                    // Auto-select: use highlighted item, or top result (index 0) if nothing highlighted
                    const selectIndex = this.searchSelectedIndex >= 0 ? this.searchSelectedIndex : 0;
                    if (items[selectIndex]) {
                        const symbol = items[selectIndex].dataset.symbol;
                        tickerInput.value = symbol;
                    }
                    this.hideSearchResults();
                }

                if (e.key === 'Enter') {
                    e.preventDefault();
                    tickerInput.blur(); // Move focus out, like Tab
                }

                // Trigger analysis with whatever is in the input
                const ticker = tickerInput.value.trim();
                if (ticker) {
                    this._tickerCommittedByKeyboard = true;
                    this.analyzeStock();
                }
            } else if (e.key === 'Escape') {
                this.hideSearchResults();
            }
        });

        // Blur: auto-select top result and analyze (if value changed since focus)
        tickerInput.addEventListener('blur', () => {
            setTimeout(() => {
                // Auto-select top result from dropdown if still open
                const items = searchResults.querySelectorAll('.search-result');
                const isOpen = !searchResults.classList.contains('hidden') && items.length > 0;
                if (isOpen && items[0]) {
                    const selectIndex = this.searchSelectedIndex >= 0 ? this.searchSelectedIndex : 0;
                    if (items[selectIndex]) {
                        tickerInput.value = items[selectIndex].dataset.symbol;
                    }
                }
                this.hideSearchResults();

                // Analyze if the value changed and wasn't already handled by keydown
                const currentValue = tickerInput.value.trim();
                if (currentValue && currentValue !== tickerValueOnFocus && !this._tickerCommittedByKeyboard) {
                    this.analyzeStock();
                }
                this._tickerCommittedByKeyboard = false;
            }, 150);
        });

        // Track focus value for change detection
        tickerInput.addEventListener('focus', (e) => {
            tickerValueOnFocus = e.target.value.trim();
            this._tickerCommittedByKeyboard = false;
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

        // Comparison search input — same Bloomberg terminal behavior
        const compareInput = document.getElementById('compare-input');
        const compareResults = document.getElementById('compare-results');
        let compareValueOnFocus = '';

        compareInput.addEventListener('input', (e) => {
            const query = e.target.value.trim();
            if (this.compareSearchTimeout) clearTimeout(this.compareSearchTimeout);

            if (query.length < 2) {
                this.hideCompareResults();
                return;
            }

            // Debounce search
            this.compareSearchTimeout = setTimeout(() => this.performCompareSearch(query), 150);
        });

        // Keyboard navigation in comparison input — Enter/Tab auto-select + trigger
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
            } else if (e.key === 'Enter' || e.key === 'Tab') {
                // Bloomberg behavior: commit selection and trigger comparison
                if (isOpen) {
                    const selectIndex = this.compareSelectedIndex >= 0 ? this.compareSelectedIndex : 0;
                    if (items[selectIndex]) {
                        const symbol = items[selectIndex].dataset.symbol;
                        compareInput.value = symbol;
                    }
                    this.hideCompareResults();
                }

                if (e.key === 'Enter') {
                    e.preventDefault();
                    compareInput.blur();
                }

                const ticker = compareInput.value.trim().toUpperCase();
                if (ticker && this.currentTicker) {
                    this._compareCommittedByKeyboard = true;
                    this.setComparison(ticker);
                }
            } else if (e.key === 'Escape') {
                this.hideCompareResults();
            }
        });

        // Blur: auto-select top result and trigger comparison (if value changed)
        compareInput.addEventListener('blur', () => {
            setTimeout(() => {
                const items = compareResults.querySelectorAll('.compare-result');
                const isOpen = !compareResults.classList.contains('hidden') && items.length > 0;
                if (isOpen && items[0]) {
                    const selectIndex = this.compareSelectedIndex >= 0 ? this.compareSelectedIndex : 0;
                    if (items[selectIndex]) {
                        compareInput.value = items[selectIndex].dataset.symbol;
                    }
                }
                this.hideCompareResults();

                const currentValue = compareInput.value.trim().toUpperCase();
                if (currentValue && currentValue !== compareValueOnFocus && !this._compareCommittedByKeyboard && this.currentTicker) {
                    this.setComparison(currentValue);
                }
                this._compareCommittedByKeyboard = false;
            }, 150);
        });

        compareInput.addEventListener('focus', (e) => {
            compareValueOnFocus = e.target.value.trim().toUpperCase();
            this._compareCommittedByKeyboard = false;
            if (e.target.value.trim().length >= 2) {
                this.performCompareSearch(e.target.value.trim());
            }
        });

        // Clear comparison button
        document.getElementById('clear-compare').addEventListener('click', () => {
            this.clearComparison();
        });

        // Benchmark toggle chips
        document.querySelectorAll('[data-benchmark]').forEach(btn => {
            btn.addEventListener('click', () => {
                const ticker = btn.dataset.benchmark;
                const label = btn.textContent.trim();
                this.toggleBenchmark(ticker, label, btn);
            });
        });

        // Clear Benchmarks button
        document.getElementById('clear-benchmarks').addEventListener('click', () => {
            this.clearBenchmarkSeries();
            this.saveBenchmarkSelections();
            document.querySelectorAll('[data-benchmark]').forEach(btn => {
                btn.classList.remove('active');
            });
            document.querySelectorAll('.chip-benchmark-temp').forEach(el => el.remove());

            this.updateClearButtonVisibility();

            if (!this.isMultiSeriesMode()) {
                this.disableIndicators(false);
            }

            if (this.historyData) {
                this.renderChart();
                this.attachChartHoverListeners();
                this.attachDragMeasure();
            }
        });

        // All Clear (comparison + benchmarks)
        document.getElementById('clear-all-overlays').addEventListener('click', () => {
            this.clearComparison();
            this.clearBenchmarkSeries();
            this.saveBenchmarkSelections();
            document.querySelectorAll('[data-benchmark]').forEach(btn => {
                btn.classList.remove('active');
            });
            document.querySelectorAll('.chip-benchmark-temp').forEach(el => el.remove());

            this.updateClearButtonVisibility();
            this.disableIndicators(false);

            if (this.historyData) {
                this.renderChart();
                this.attachChartHoverListeners();
                this.attachDragMeasure();
            }
        });

        // Benchmark search input
        const benchmarkInput = document.getElementById('benchmark-search-input');
        benchmarkInput.addEventListener('input', (e) => {
            clearTimeout(this.benchmarkSearchTimeout);
            const query = e.target.value.trim();
            this.benchmarkSearchTimeout = setTimeout(
                () => this.performBenchmarkSearch(query), 300
            );
        });

        benchmarkInput.addEventListener('keydown', (e) => {
            const container = document.getElementById('benchmark-search-results');
            const items = container.querySelectorAll('.benchmark-result');
            const isOpen = !container.classList.contains('hidden') && items.length > 0;

            if (e.key === 'ArrowDown') {
                e.preventDefault();
                if (isOpen) {
                    this.benchmarkSearchSelectedIndex = Math.min(
                        this.benchmarkSearchSelectedIndex + 1, items.length - 1
                    );
                    this.highlightDropdownItem(items, this.benchmarkSearchSelectedIndex);
                }
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                if (isOpen) {
                    this.benchmarkSearchSelectedIndex = Math.max(
                        this.benchmarkSearchSelectedIndex - 1, 0
                    );
                    this.highlightDropdownItem(items, this.benchmarkSearchSelectedIndex);
                }
            } else if (e.key === 'Enter') {
                e.preventDefault();
                if (isOpen && this.benchmarkSearchSelectedIndex >= 0) {
                    items[this.benchmarkSearchSelectedIndex].click();
                }
            } else if (e.key === 'Escape') {
                this.hideBenchmarkSearchResults();
            }
        });

        benchmarkInput.addEventListener('blur', () => {
            setTimeout(() => this.hideBenchmarkSearchResults(), 200);
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
            container.innerHTML = `<div class="result-empty">${message}</div>`;
            container.classList.remove('hidden');
            return;
        }

        container.innerHTML = results.map(r => `
            <div class="search-result" data-symbol="${r.symbol}">
                <div class="result-symbol">${r.symbol}</div>
                <div class="result-name">${r.shortName || r.longName || ''}</div>
                <div class="result-meta">${r.exchange || ''} ${r.type ? `• ${r.type}` : ''}</div>
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

        // Bloomberg behavior: auto-highlight first result so Tab/Enter always has a selection
        this.searchSelectedIndex = 0;
        const firstItem = container.querySelector('.search-result');
        if (firstItem) firstItem.classList.add('highlighted');

        container.classList.remove('hidden');
    },

    /**
     * Highlight a dropdown item by index (keyboard navigation).
     * Removes highlight from all siblings, adds to the target, and scrolls into view.
     */
    highlightDropdownItem(items, index) {
        items.forEach((item, i) => {
            if (i === index) {
                item.classList.add('highlighted');
                item.scrollIntoView({ block: 'nearest' });
            } else {
                item.classList.remove('highlighted');
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
            container.innerHTML = '<div class="result-empty">No results found</div>';
            container.classList.remove('hidden');
            return;
        }

        container.innerHTML = results.map(r => `
            <div class="compare-result" data-symbol="${r.symbol}">
                <div class="result-symbol">${r.symbol}</div>
                <div class="result-name">${r.shortName || r.longName || ''}</div>
                <div class="result-meta">${r.exchange || ''} ${r.type ? `• ${r.type}` : ''}</div>
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

        // Bloomberg behavior: auto-highlight first result so Tab/Enter always has a selection
        this.compareSelectedIndex = 0;
        const firstItem = container.querySelector('.compare-result');
        if (firstItem) firstItem.classList.add('highlighted');

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
            const historyData = await API.getHistory(
                ticker, null, this.resolvedStartDate, this.resolvedEndDate);

            // Add to chartSeries (replaces any existing comparison)
            this.addSeries(ticker, ticker, 'comparison', historyData);

            // Disable technical indicators (they don't make sense for multi-series)
            this.disableIndicators(true);

            // Show clear button
            document.getElementById('clear-compare').classList.remove('hidden');
            this.updateClearButtonVisibility();

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
        // Remove comparison series
        const comparison = this.chartSeries.find(s => s.type === 'comparison');
        if (comparison) {
            this.removeSeries(comparison.ticker);
        }

        document.getElementById('compare-input').value = '';
        document.getElementById('clear-compare').classList.add('hidden');
        this.updateClearButtonVisibility();

        // Re-enable technical indicators if no longer in multi-series mode
        if (!this.isMultiSeriesMode()) {
            this.disableIndicators(false);
        }

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

        // Clear all non-primary series
        this.clearAllSeries();
        this.saveBenchmarkSelections();
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
     * Build the primary series entry from current stock data.
     * Called when a stock is first analyzed.
     */
    buildPrimarySeries() {
        if (!this.historyData || !this.currentTicker) return null;
        return {
            ticker: this.currentTicker,
            label: this.currentTicker,
            type: 'primary',
            data: this.historyData,
            color: SERIES_PALETTE[0].color,
            dash: SERIES_PALETTE[0].dash
        };
    },

    /**
     * Rebuild chartSeries from current state.
     * Ensures primary is always at index 0 with correct palette assignment.
     */
    rebuildChartSeries() {
        const primary = this.buildPrimarySeries();
        if (!primary) {
            this.chartSeries = [];
            return;
        }
        // Keep non-primary series, reassign palette positions
        const others = this.chartSeries.filter(s => s.type !== 'primary');
        this.chartSeries = [primary, ...others];
        this.assignPalettePositions();
    },

    /**
     * Assign palette colors/dashes based on position in chartSeries.
     */
    assignPalettePositions() {
        this.chartSeries.forEach((series, i) => {
            if (i < SERIES_PALETTE.length) {
                series.color = SERIES_PALETTE[i].color;
                series.dash = SERIES_PALETTE[i].dash;
            }
        });
    },

    /**
     * Add a series to the chart. Returns false if limit reached or duplicate.
     * @param {string} ticker
     * @param {string} label
     * @param {'comparison'|'benchmark'} type
     * @param {object} data - History data from API
     * @returns {boolean} true if added
     */
    addSeries(ticker, label, type, data) {
        // Prevent duplicates
        if (this.chartSeries.some(s => s.ticker === ticker)) {
            return false;
        }
        // Enforce max benchmarks
        const benchmarkCount = this.chartSeries.filter(s => s.type === 'benchmark').length;
        if (type === 'benchmark' && benchmarkCount >= MAX_BENCHMARKS) {
            alert(`Maximum ${MAX_BENCHMARKS} benchmarks allowed. Remove one before adding another.`);
            return false;
        }
        // Only one comparison allowed
        if (type === 'comparison') {
            this.chartSeries = this.chartSeries.filter(s => s.type !== 'comparison');
        }
        const idx = this.chartSeries.length;
        const palette = idx < SERIES_PALETTE.length ? SERIES_PALETTE[idx] : SERIES_PALETTE[SERIES_PALETTE.length - 1];
        this.chartSeries.push({
            ticker,
            label,
            type,
            data,
            color: palette.color,
            dash: palette.dash
        });
        this.assignPalettePositions();
        return true;
    },

    /**
     * Remove a series by ticker. Returns the removed series or null.
     */
    removeSeries(ticker) {
        const idx = this.chartSeries.findIndex(s => s.ticker === ticker);
        if (idx <= 0) return null; // Can't remove primary (index 0) or not found
        const removed = this.chartSeries.splice(idx, 1)[0];
        this.assignPalettePositions();
        return removed;
    },

    /**
     * Get all series of a given type.
     */
    getSeriesByType(type) {
        return this.chartSeries.filter(s => s.type === type);
    },

    /**
     * Remove all benchmark series. Comparison and primary remain.
     */
    clearBenchmarkSeries() {
        this.chartSeries = this.chartSeries.filter(s => s.type !== 'benchmark');
        this.assignPalettePositions();
    },

    /**
     * Remove all non-primary series (comparison + benchmarks).
     */
    clearAllSeries() {
        this.chartSeries = this.chartSeries.filter(s => s.type === 'primary');
    },

    /**
     * Save active benchmark tickers to localStorage.
     * Called on every benchmark toggle.
     */
    saveBenchmarkSelections() {
        try {
            const benchmarkTickers = this.getSeriesByType('benchmark')
                .map(s => s.ticker);
            localStorage.setItem('stockAnalyzer_benchmarks', JSON.stringify(benchmarkTickers));
        } catch (error) {
            console.error('Failed to save benchmark selections:', error);
        }
    },

    /**
     * Load saved benchmark tickers from localStorage.
     * Returns empty array on corrupted/missing data.
     * @returns {string[]} Array of ticker strings
     */
    loadBenchmarkSelections() {
        try {
            const data = localStorage.getItem('stockAnalyzer_benchmarks');
            if (!data) return [];
            const parsed = JSON.parse(data);
            if (!Array.isArray(parsed)) return [];
            return parsed.filter(t => typeof t === 'string' && t.length > 0);
        } catch (error) {
            console.warn('Corrupted benchmark data in localStorage, clearing:', error);
            localStorage.removeItem('stockAnalyzer_benchmarks');
            return [];
        }
    },

    /**
     * Restore saved benchmarks after stock analysis completes.
     * Fetches data for each saved ticker and adds to chart.
     * Silently skips tickers that fail to load (e.g., delisted).
     */
    async restoreBenchmarks() {
        const savedTickers = this.loadBenchmarkSelections();
        if (savedTickers.length === 0) return;

        // Fetch all benchmarks in parallel
        const results = await Promise.allSettled(
            savedTickers.map(async (ticker) => {
                const historyData = await API.getHistory(
                    ticker, null, this.resolvedStartDate, this.resolvedEndDate
                );
                return { ticker, historyData };
            })
        );

        let anyAdded = false;
        for (const result of results) {
            if (result.status !== 'fulfilled') {
                console.warn('Failed to restore benchmark, skipping:', result.reason);
                continue;
            }
            const { ticker, historyData } = result.value;
            const label = this.getBenchmarkLabel(ticker);
            const added = this.addSeries(ticker, label, 'benchmark', historyData);

            if (added) {
                anyAdded = true;
                const chip = document.querySelector(`[data-benchmark="${ticker}"]`);
                if (chip) chip.classList.add('active');
            }
        }

        if (anyAdded) {
            this.disableIndicators(true);
            this.updateClearButtonVisibility();
            this.renderChart();
            this.attachChartHoverListeners();
            this.attachDragMeasure();
        }
    },

    /**
     * Get display label for a benchmark ticker.
     * Checks static chips first, falls back to ticker string.
     */
    getBenchmarkLabel(ticker) {
        const chip = document.querySelector(`[data-benchmark="${ticker}"]`);
        if (chip) return chip.textContent.trim();
        return ticker;
    },

    /**
     * Whether the chart is in multi-series mode (more than just the primary stock).
     */
    isMultiSeriesMode() {
        return this.chartSeries.length > 1;
    },

    /**
     * Toggle a benchmark index on/off the chart.
     * If already active, removes it. If not active, fetches data and adds it.
     * @param {string} etfTicker - The ETF proxy ticker (e.g., 'SPY')
     * @param {string} label - Display label (e.g., 'S&P 500')
     * @param {HTMLElement} [chipEl] - The chip button element (for toggle styling)
     */
    async toggleBenchmark(etfTicker, label, chipEl) {
        etfTicker = etfTicker.toUpperCase();

        if (!this.currentTicker) {
            alert('Analyze a stock first before adding benchmarks.');
            return;
        }

        // Guard against concurrent fetches (rapid chip clicks)
        if (this._benchmarkFetchInProgress) return;
        this._benchmarkFetchInProgress = true;

        // If already in chartSeries, remove it (toggle off)
        const existing = this.chartSeries.find(
            s => s.ticker === etfTicker && s.type === 'benchmark'
        );
        if (existing) {
            this.removeSeries(etfTicker);
            if (chipEl) chipEl.classList.remove('active');

            // Remove temp chips
            const tempChip = document.querySelector(
                `.chip-benchmark-temp[data-benchmark="${etfTicker}"]`
            );
            if (tempChip) tempChip.remove();

            this.updateClearButtonVisibility();

            if (!this.isMultiSeriesMode()) {
                this.disableIndicators(false);
            }

            this.renderChart();
            this.attachChartHoverListeners();
            this.attachDragMeasure();
            this.saveBenchmarkSelections();
            this._benchmarkFetchInProgress = false;
            return;
        }

        // Adding a new benchmark — addSeries enforces max 5
        try {
            const historyData = await API.getHistory(
                etfTicker, null, this.resolvedStartDate, this.resolvedEndDate
            );

            const added = this.addSeries(etfTicker, label || etfTicker, 'benchmark', historyData);
            if (!added) return; // Max limit reached (addSeries already showed alert)

            if (chipEl) chipEl.classList.add('active');

            this.disableIndicators(true);
            this.updateClearButtonVisibility();

            this.renderChart();
            this.attachChartHoverListeners();
            this.attachDragMeasure();
            this.saveBenchmarkSelections();
        } catch (error) {
            console.error(`Failed to fetch benchmark data for ${LogSanitizer.sanitize(etfTicker)}:`, error);
            alert(`Failed to load benchmark data for ${etfTicker}`);
        } finally {
            this._benchmarkFetchInProgress = false;
        }
    },

    /**
     * Update visibility of Clear Benchmarks and All Clear buttons.
     */
    updateClearButtonVisibility() {
        const hasBenchmarks = this.getSeriesByType('benchmark').length > 0;
        const hasComparison = this.chartSeries.some(s => s.type === 'comparison');

        const clearBtn = document.getElementById('clear-benchmarks');
        const allClearBtn = document.getElementById('clear-all-overlays');

        if (clearBtn) {
            clearBtn.classList.toggle('hidden', !hasBenchmarks);
        }
        if (allClearBtn) {
            allClearBtn.classList.toggle('hidden', !hasBenchmarks && !hasComparison);
        }
    },

    /**
     * Perform debounced benchmark index search
     */
    performBenchmarkSearch(query) {
        const container = document.getElementById('benchmark-search-results');
        const loader = document.getElementById('benchmark-search-loader');

        if (!query || query.length < 2) {
            container.classList.add('hidden');
            return;
        }

        loader.classList.remove('hidden');

        API.searchIndices(query).then(results => {
            loader.classList.add('hidden');
            this.showBenchmarkSearchResults(results);
        }).catch(err => {
            loader.classList.add('hidden');
            console.error('Benchmark search failed:', err);
            container.classList.add('hidden');
        });
    },

    /**
     * Render benchmark search results dropdown
     */
    showBenchmarkSearchResults(results) {
        const container = document.getElementById('benchmark-search-results');
        if (!results || results.length === 0) {
            container.classList.add('hidden');
            return;
        }

        // Build results using DOM API (avoid innerHTML XSS with database-sourced strings)
        container.innerHTML = '';
        results.forEach(r => {
            const div = document.createElement('div');
            div.className = 'benchmark-result';
            div.dataset.ticker = r.proxyEtfTicker;
            div.dataset.name = r.indexName;

            const symbolEl = document.createElement('div');
            symbolEl.className = 'result-symbol';
            symbolEl.textContent = r.proxyEtfTicker;

            const nameEl = document.createElement('div');
            nameEl.className = 'result-name';
            nameEl.textContent = r.indexName;

            const metaEl = document.createElement('div');
            metaEl.className = 'result-meta';
            metaEl.textContent = [r.region, r.indexCode].filter(Boolean).join(' • ');

            div.appendChild(symbolEl);
            div.appendChild(nameEl);
            div.appendChild(metaEl);
            container.appendChild(div);
        });

        container.classList.remove('hidden');

        container.querySelectorAll('.benchmark-result').forEach(item => {
            item.addEventListener('click', () => {
                const ticker = item.dataset.ticker;
                const name = item.dataset.name;
                container.classList.add('hidden');
                document.getElementById('benchmark-search-input').value = '';
                this.addBenchmarkFromSearch(ticker, name);
            });
        });
    },

    /**
     * Add a benchmark from search results — creates a temporary chip if not a static chip
     */
    async addBenchmarkFromSearch(etfTicker, indexName) {
        const existingChip = document.querySelector(`[data-benchmark="${etfTicker}"]`);
        if (existingChip) {
            this.toggleBenchmark(etfTicker, existingChip.textContent.trim(), existingChip);
            return;
        }

        const chipsContainer = document.getElementById('benchmark-chips');
        const tempChip = document.createElement('button');
        tempChip.className = 'chip chip-benchmark-temp';
        tempChip.dataset.benchmark = etfTicker;
        tempChip.textContent = indexName || etfTicker;
        tempChip.addEventListener('click', () => {
            this.toggleBenchmark(etfTicker, indexName || etfTicker, tempChip);
        });
        chipsContainer.appendChild(tempChip);

        await this.toggleBenchmark(etfTicker, indexName || etfTicker, tempChip);
    },

    /**
     * Hide benchmark search results dropdown
     */
    hideBenchmarkSearchResults() {
        document.getElementById('benchmark-search-results').classList.add('hidden');
        this.benchmarkSearchSelectedIndex = -1;
    },

    /**
     * Disable or re-enable all indicator and chart-type controls.
     * When disabling: saves current checkbox states to this.savedIndicatorState.
     * When re-enabling: restores from saved state (so user doesn't lose their selections).
     * @param {boolean} disabled - true to disable, false to re-enable
     */
    disableIndicators(disabled) {
        const checkboxIds = ['show-rsi', 'show-macd', 'show-bollinger', 'show-stochastic', 'ma-20', 'ma-50', 'ma-200'];
        const labelIds = ['rsi-label', 'macd-label', 'bollinger-label', 'stochastic-label', 'ma-20-label', 'ma-50-label', 'ma-200-label'];
        const chartTypeSelect = document.getElementById('chart-type');
        const chartTypeLabel = document.getElementById('chart-type-label');
        const showMarkersCheckbox = document.getElementById('show-markers');

        if (disabled) {
            // Save current state before disabling (only if not already saved)
            if (!this.savedIndicatorState) {
                this.savedIndicatorState = {};
                checkboxIds.forEach(id => {
                    const el = document.getElementById(id);
                    if (el) this.savedIndicatorState[id] = el.checked;
                });
                if (chartTypeSelect) {
                    this.savedIndicatorState['chart-type'] = chartTypeSelect.value;
                }
                if (showMarkersCheckbox) {
                    this.savedIndicatorState['show-markers'] = showMarkersCheckbox.checked;
                }
            }

            // Disable and uncheck all indicator checkboxes
            checkboxIds.forEach(id => {
                const el = document.getElementById(id);
                if (el) {
                    el.disabled = true;
                    el.checked = false;
                }
            });

            // Dim all labels
            labelIds.forEach(id => {
                const el = document.getElementById(id);
                if (el) el.classList.add('opacity-50', 'cursor-not-allowed');
            });

            // Disable chart type selector (force to 'line' in multi-series)
            if (chartTypeSelect) {
                chartTypeSelect.disabled = true;
                chartTypeSelect.value = 'line';
            }
            if (chartTypeLabel) {
                chartTypeLabel.classList.add('opacity-50', 'cursor-not-allowed');
            }

            // Disable significant move markers
            if (showMarkersCheckbox) {
                showMarkersCheckbox.disabled = true;
                showMarkersCheckbox.checked = false;
            }
        } else {
            // Re-enable all checkboxes, restoring saved state
            checkboxIds.forEach(id => {
                const el = document.getElementById(id);
                if (el) {
                    el.disabled = false;
                    if (this.savedIndicatorState && id in this.savedIndicatorState) {
                        el.checked = this.savedIndicatorState[id];
                    }
                }
            });

            // Remove dim from labels
            labelIds.forEach(id => {
                const el = document.getElementById(id);
                if (el) el.classList.remove('opacity-50', 'cursor-not-allowed');
            });

            // Re-enable chart type selector
            if (chartTypeSelect) {
                chartTypeSelect.disabled = false;
                if (this.savedIndicatorState && 'chart-type' in this.savedIndicatorState) {
                    chartTypeSelect.value = this.savedIndicatorState['chart-type'];
                }
            }
            if (chartTypeLabel) {
                chartTypeLabel.classList.remove('opacity-50', 'cursor-not-allowed');
            }

            // Re-enable significant move markers
            if (showMarkersCheckbox) {
                showMarkersCheckbox.disabled = false;
                if (this.savedIndicatorState && 'show-markers' in this.savedIndicatorState) {
                    showMarkersCheckbox.checked = this.savedIndicatorState['show-markers'];
                }
            }

            // Clear saved state
            this.savedIndicatorState = null;
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

            // Initialize chartSeries with primary stock
            this.rebuildChartSeries();

            const analysis = this.analysisData;

            // Render chart immediately - this is what the user is waiting for
            this.renderPerformance(analysis.performance);
            this.renderChart();
            this.showResults();

            // PHASE 2: Load secondary data in background (non-blocking)
            // Start all these requests immediately — don't delay behind benchmark restore
            const stockInfoPromise = API.getStockInfo(ticker);
            // Always use chart data's actual date range (not UI state) to ensure moves match the chart
            const significantMovesPromise = API.getSignificantMoves(
                ticker, this.currentThreshold, null, chartData.startDate, chartData.endDate);
            const newsPromise = API.getAggregatedNews(ticker, 30, 10);

            // Restore saved benchmarks in background (don't block Phase 2)
            this.restoreBenchmarks().catch(e =>
                console.warn('Failed to restore benchmarks:', e)
            );

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
            ? `<div class="stock-identifiers">
                ${identifiers.map(id => `
                    <span class="stock-identifier">
                        <span class="label">${id.label}:</span> ${id.value}
                    </span>
                `).join('')}
               </div>`
            : '';

        // Smart truncation: only truncate long descriptions, and cut at sentence boundaries
        const descriptionHtml = info.description
            ? `<p class="stock-description">${this.truncateAtSentence(info.description, 500)}</p>`
            : '';

        document.getElementById('stock-info').innerHTML = `
            <div class="stock-info-content">
                <div class="stock-header">
                    <div>
                        <h2 class="stock-name">${info.longName || info.shortName || info.symbol}</h2>
                        <p class="stock-meta">${info.exchange || ''} ${info.currency ? `• ${info.currency}` : ''}${info.sector ? ` • ${info.sector}` : ''}</p>
                        ${identifiersHtml}
                    </div>
                    <div class="stock-price">
                        <div class="stock-price-value">
                            $${this.formatNumber(info.currentPrice)}
                        </div>
                        <div class="stock-price-change ${isPositive ? 'text-success' : 'text-danger'}">
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
            <div class="metric-row">
                <span class="metric-label">${m.label}</span>
                <span class="metric-value">${m.value || 'N/A'}</span>
            </div>
        `).join('');
    },

    /**
     * Render performance metrics
     */
    renderPerformance(performance) {
        if (!performance) {
            document.getElementById('performance-metrics').innerHTML = '<p class="status-message">No performance data available</p>';
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
            <div class="metric-row">
                <span class="metric-label">${m.label}</span>
                <span class="metric-value ${m.color || ''}">${m.value || 'N/A'}</span>
            </div>
        `).join('');
    },

    /**
     * Render significant moves
     */
    renderSignificantMoves(data) {
        if (!data || !data.moves || data.moves.length === 0) {
            document.getElementById('significant-moves').innerHTML = '<p class="status-message">No significant moves found</p>';
            return;
        }

        document.getElementById('significant-moves').innerHTML = data.moves.slice(0, 10).map(move => {
            const isPositive = move.percentChange >= 0;
            const date = new Date(move.date).toLocaleDateString();
            return `
                <div class="move-row">
                    <span class="move-date">${date}</span>
                    <span class="move-value ${isPositive ? 'text-success' : 'text-danger'}">
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
            document.getElementById('news-list').innerHTML = '<p class="status-message">No recent news available</p>';
            return;
        }

        // Build source breakdown header if we have multiple sources
        let sourceHeader = '';
        if (data.sourceBreakdown && Object.keys(data.sourceBreakdown).length > 0) {
            const sources = Object.entries(data.sourceBreakdown)
                .map(([source, count]) => `${source}: ${count}`)
                .join(', ');
            sourceHeader = `<p class="news-sources-header">Sources: ${sources}</p>`;
        }

        const articlesHtml = data.articles.slice(0, 5).map(article => {
            const date = new Date(article.publishedAt).toLocaleDateString();
            // Show the API source (Finnhub, Marketaux) alongside the publisher
            const apiSource = article.sourceApi ? `[${article.sourceApi}]` : '';
            return `
                <div class="news-article">
                    <a href="${article.url}" target="_blank" rel="noopener noreferrer" class="news-headline">
                        ${article.headline}
                    </a>
                    <p class="news-meta">
                        ${article.source} • ${date}
                        <span class="news-source-tag">${apiSource}</span>
                    </p>
                    ${article.summary ? `<p class="news-summary">${article.summary.substring(0, 150)}...</p>` : ''}
                </div>
            `;
        }).join('');

        document.getElementById('news-list').innerHTML = sourceHeader + articlesHtml;
    },

    /**
     * Render chart
     */
    renderChart() {
        // Ensure primary series is current
        this.rebuildChartSeries();

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
            // Pass chartSeries directly (charts.js iterates for multi-series rendering)
            chartSeries: this.chartSeries,
            comparisonData: null,
            comparisonTicker: null
        };

        // Adjust chart height based on enabled indicators (only when not in multi-series mode)
        const chartEl = document.getElementById('stock-chart');
        const baseHeight = 400;
        const indicatorHeight = 150;
        let totalHeight = baseHeight;

        if (!this.isMultiSeriesMode()) {
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
            isComparisonMode: () => this.isMultiSeriesMode(),
            getComparisonData: () => {
                const s = this.chartSeries.find(c => c.type === 'comparison');
                return s ? { data: s.data.data } : null;
            },
            getPrimarySymbol: () => this.currentTicker || '',
            getComparisonSymbol: () => {
                const s = this.chartSeries.find(c => c.type === 'comparison');
                return s ? s.ticker : '';
            },
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
            // Track previous earliest date to detect growth
            const prevEarliestDate = this.historyData?.data?.[0]?.date;

            const chartData = await API.getChartData(this.currentTicker, null, fromDate, toDate);
            if (!chartData?.data || chartData.data.length === 0) return;

            // Capture current zoom AFTER fetch (user may have scrolled further during fetch)
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

            // Also fetch comparison and benchmark data for the extended range
            const overlays = this.chartSeries.filter(s => s.type === 'comparison' || s.type === 'benchmark');
            if (overlays.length > 0) {
                const fetches = overlays.map(async (series) => {
                    try {
                        const overlayData = await API.getHistory(series.ticker, null, fromDate, toDate);
                        if (overlayData?.data) {
                            series.data = {
                                symbol: overlayData.symbol || series.ticker,
                                data: overlayData.data
                            };
                        }
                    } catch { /* overlay extension is best-effort */ }
                });
                await Promise.allSettled(fetches);
            }

            // Re-render chart with the full extended dataset
            this.renderChart();
            this.attachChartHoverListeners();
            this.attachDragMeasure();

            // Restore the zoom position the user was viewing
            if (currentRange && chartEl) {
                Plotly.relayout(chartEl, { 'xaxis.range': currentRange });
            }

            // If visible range still extends past data AND data actually grew, retry
            if (currentRange && this.historyData?.data?.length > 0) {
                const dataStartMs = new Date(this.historyData.data[0].date).getTime();
                const visibleStartMs = new Date(currentRange[0]).getTime();
                const dataGrew = this.historyData.data[0].date !== prevEarliestDate;
                if (visibleStartMs < dataStartMs && dataGrew) {
                    const fmt = (ms) => new Date(ms).toISOString().split('T')[0];
                    setTimeout(() => this.extendChartRange(fmt(visibleStartMs), toDate), 500);
                }
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

// Export for testing (Node.js / Jest)
if (typeof module !== 'undefined' && module.exports) {
    module.exports = { App, SERIES_PALETTE, MAX_BENCHMARKS };
}
