/**
 * JSON-BASED THEME LOADER
 *
 * Loads theme definitions from JSON files and applies them dynamically.
 * Themes define CSS variables, visual effects (scanlines, bloom, rain),
 * and other style parameters without requiring code changes.
 *
 * Usage:
 *   await ThemeLoader.init();             // Load manifest, apply saved theme
 *   await ThemeLoader.applyTheme('dark'); // Switch themes
 *   ThemeLoader.getThemeColors();         // Get chart colors for Plotly
 */

const ThemeLoader = (() => {
    'use strict';

    // ===== CONFIGURATION =====
    const STORAGE_KEY = 'stockanalyzer_theme';
    // Use Azure Blob Storage for themes (allows updates without code deploys)
    // Falls back to local /themes/ if Azure is unreachable
    const AZURE_THEMES_URL = 'https://stockanalyzerblob.z13.web.core.windows.net/themes/';
    const LOCAL_THEMES_PATH = '/themes/';
    const CACHE_TTL = 60 * 60 * 1000; // 1 hour
    let themesPath = AZURE_THEMES_URL; // Start with Azure, fallback to local if needed

    // ===== STATE =====
    let currentTheme = null;
    let themeCache = {};
    let manifest = null;
    let effectsContainer = null;
    let dynamicStyleSheet = null;
    let initialized = false;

    // ===== PUBLIC API =====

    /**
     * Initialize the theme system. Call once on page load.
     */
    async function init() {
        if (initialized) return;

        createEffectsContainer();
        createDynamicStyleSheet();

        try {
            manifest = await fetchManifest();
            const savedThemeId = localStorage.getItem(STORAGE_KEY) || manifest.default || 'light';
            await applyTheme(savedThemeId);
            initialized = true;
        } catch (error) {
            console.error('ThemeLoader init failed, falling back to CSS:', error);
            fallbackToCSSThemes();
            initialized = true;
        }
    }

    /**
     * Apply a theme by ID.
     * @param {string} themeId - Theme identifier (e.g., 'light', 'dark', 'neon-noir')
     */
    async function applyTheme(themeId) {
        const theme = await loadTheme(themeId);
        if (!theme) {
            console.error('Theme "' + themeId + '" not found, falling back to light');
            if (themeId !== 'light') {
                return applyTheme('light');
            }
            return;
        }

        currentTheme = theme;
        localStorage.setItem(STORAGE_KEY, themeId);

        // Apply in order: variables -> effects -> fonts -> animations -> overrides
        applyVariables(theme.variables);
        applyEffects(theme.effects);
        applyFonts(theme.fonts);
        applyAnimations(theme.animations);
        applyOverrideCSS(theme.overrideCSS);
        updateHtmlClass(theme);

        // Notify listeners
        dispatchThemeChangeEvent(theme);

        console.log('Theme applied: ' + (theme.name || themeId));
    }

    /**
     * Get the currently active theme object.
     */
    function getCurrentTheme() {
        return currentTheme;
    }

    /**
     * Get the current theme ID.
     */
    function getCurrentThemeId() {
        return currentTheme?.id || localStorage.getItem(STORAGE_KEY) || 'light';
    }

    /**
     * Get chart colors for Plotly rendering.
     * Returns an object with all chart-related color values.
     */
    function getThemeColors() {
        const vars = currentTheme?.variables || {};

        // Helper to get variable with CSS fallback
        const getVar = (key, fallback) => {
            if (vars[key] !== undefined) return vars[key];
            // Fallback to CSS variable
            const cssVal = getComputedStyle(document.documentElement)
                .getPropertyValue('--' + key).trim();
            return cssVal || fallback;
        };

        return {
            // Core colors
            background: getVar('chart-bg', getVar('bg-primary', '#ffffff')),
            paper: getVar('chart-bg', getVar('bg-primary', '#ffffff')),
            text: getVar('chart-text', getVar('text-primary', '#1f2937')),
            gridColor: getVar('chart-grid', getVar('border-primary', '#e5e7eb')),
            axisColor: getVar('chart-axis', getVar('text-muted', '#6b7280')),

            // Line colors
            linePrimary: getVar('chart-line-primary', '#3b82f6'),
            lineSecondary: getVar('chart-line-secondary', '#f59e0b'),
            lineSma20: getVar('chart-line-sma20', '#f59e0b'),
            lineSma50: getVar('chart-line-sma50', '#8b5cf6'),
            lineSma200: getVar('chart-line-sma200', '#ec4899'),

            // Candles
            candleUp: getVar('chart-candle-up', '#10b981'),
            candleDown: getVar('chart-candle-down', '#ef4444'),
            volumeUp: getVar('chart-volume-up', '#065f46'),
            volumeDown: getVar('chart-volume-down', '#991b1b'),

            // Indicators
            rsi: getVar('chart-rsi', '#8b5cf6'),
            macd: getVar('chart-macd', '#3b82f6'),
            macdSignal: getVar('chart-macd-signal', '#f59e0b'),
            stochastic: getVar('chart-stochastic', '#14b8a6'),
            stochasticD: getVar('chart-stochastic-d', '#f59e0b'),
            overbought: getVar('chart-overbought', '#ef4444'),
            oversold: getVar('chart-oversold', '#10b981'),
            bollinger: getVar('chart-bollinger', '#6366f1'),

            // Markers
            markerUp: getVar('chart-marker-up', '#10b981'),
            markerDown: getVar('chart-marker-down', '#ef4444'),
            markerUpOutline: getVar('chart-marker-up-outline', '#065f46'),
            markerDownOutline: getVar('chart-marker-down-outline', '#991b1b'),
            markerSymbol: getVar('chart-marker-symbol', 'triangle'),
            markerSize: parseInt(getVar('chart-marker-size', '22')) || 22,

            // Glow effects
            glowEnabled: getVar('chart-line-glow', 'none') === 'enabled',
            glowColor: getVar('chart-line-glow-color', 'transparent'),
            glowWidth: parseInt(getVar('chart-line-glow-width', '0')) || 0,
        };
    }

    /**
     * Get available themes from manifest.
     */
    function getAvailableThemes() {
        return manifest?.themes || [];
    }

    /**
     * Check if a theme is the current theme.
     */
    function isCurrentTheme(themeId) {
        return getCurrentThemeId() === themeId;
    }

    /**
     * Apply a theme directly from a JSON object (for theme editor/preview).
     * This bypasses file loading and applies the theme immediately.
     * Supports inheritance via "extends" if manifest is loaded.
     * @param {Object} themeJson - Theme object with id, variables, effects, etc.
     */
    async function applyThemeJson(themeJson) {
        if (!themeJson) return;

        let theme = themeJson;

        // Handle inheritance if extends is specified
        if (themeJson.extends && manifest) {
            const baseTheme = await loadTheme(themeJson.extends);
            if (baseTheme) {
                theme = mergeThemes(baseTheme, themeJson);
            }
        }

        currentTheme = theme;

        // Apply in order: variables -> effects -> fonts -> animations -> overrides
        applyVariables(theme.variables);
        applyEffects(theme.effects);
        applyFonts(theme.fonts);
        applyAnimations(theme.animations);
        applyOverrideCSS(theme.overrideCSS);
        updateHtmlClass(theme);

        // Notify listeners
        dispatchThemeChangeEvent(theme);

        console.log('Theme applied from JSON: ' + (theme.name || theme.id || 'custom'));
    }

    // ===== PRIVATE METHODS =====

    async function fetchManifest() {
        const cacheBuster = '?v=' + Date.now();

        // Try Azure first
        try {
            const res = await fetch(AZURE_THEMES_URL + 'manifest.json' + cacheBuster);
            if (res.ok) {
                themesPath = AZURE_THEMES_URL;
                console.log('ThemeLoader: Using Azure themes');
                return res.json();
            }
        } catch (e) {
            console.warn('ThemeLoader: Azure unreachable, trying local');
        }

        // Fallback to local
        const res = await fetch(LOCAL_THEMES_PATH + 'manifest.json' + cacheBuster);
        if (!res.ok) throw new Error('Manifest fetch failed: ' + res.status);
        themesPath = LOCAL_THEMES_PATH;
        console.log('ThemeLoader: Using local themes');
        return res.json();
    }

    /**
     * Load a theme by ID, resolving inheritance if needed.
     * Themes can extend other themes via "extends": "baseThemeId"
     * @param {string} themeId - Theme identifier
     * @param {Set} _loadingChain - Internal: tracks loading chain to prevent loops
     */
    async function loadTheme(themeId, _loadingChain = new Set()) {
        // Circular inheritance check
        if (_loadingChain.has(themeId)) {
            console.error('Circular theme inheritance detected: ' + themeId);
            return null;
        }

        // Check memory cache
        const cached = themeCache[themeId];
        const now = Date.now();
        if (cached?.data && (now - cached.timestamp) < CACHE_TTL) {
            return cached.data;
        }

        // Find theme in manifest
        const themeInfo = manifest?.themes?.find(t => t.id === themeId);
        const fileName = themeInfo?.file || (themeId + '.json');

        try {
            const cacheBuster = '?v=' + Date.now();
            const res = await fetch(themesPath + fileName + cacheBuster);
            if (!res.ok) throw new Error('HTTP ' + res.status);
            const theme = await res.json();

            // Ensure ID is set
            theme.id = theme.id || themeId;

            // Handle inheritance
            if (theme.extends) {
                _loadingChain.add(themeId);
                const baseTheme = await loadTheme(theme.extends, _loadingChain);
                if (baseTheme) {
                    const merged = mergeThemes(baseTheme, theme);
                    themeCache[themeId] = { data: merged, timestamp: now };
                    return merged;
                }
            }

            // Cache it
            themeCache[themeId] = { data: theme, timestamp: now };
            return theme;
        } catch (error) {
            console.error('Failed to load theme "' + themeId + '":', error);
            return null;
        }
    }

    /**
     * Deep merge two theme objects. Child values override base values.
     * @param {Object} base - Base theme object
     * @param {Object} child - Child theme object (overrides)
     * @returns {Object} Merged theme
     */
    function mergeThemes(base, child) {
        const result = {};

        // Copy all base properties
        for (const key of Object.keys(base)) {
            if (typeof base[key] === 'object' && base[key] !== null && !Array.isArray(base[key])) {
                result[key] = { ...base[key] };
            } else {
                result[key] = base[key];
            }
        }

        // Overlay child properties
        for (const key of Object.keys(child)) {
            if (key === 'extends') continue; // Don't copy extends reference

            if (typeof child[key] === 'object' && child[key] !== null && !Array.isArray(child[key])) {
                // Deep merge objects (variables, effects, fonts, etc.)
                result[key] = { ...(result[key] || {}), ...child[key] };
            } else {
                // Direct assignment for primitives and arrays
                result[key] = child[key];
            }
        }

        return result;
    }

    function applyVariables(variables) {
        if (!variables) return;
        const root = document.documentElement;
        for (const [key, value] of Object.entries(variables)) {
            root.style.setProperty('--' + key, value);
        }
    }

    function applyEffects(effects) {
        // Clear existing effects
        if (effectsContainer) {
            effectsContainer.innerHTML = '';
        }
        document.body.style.filter = '';
        clearDynamicCSS('effects');

        if (!effects) return;

        // Scanlines overlay
        if (effects.scanlines?.enabled) {
            const opacity = effects.scanlines.opacity ?? 0.25;
            const spacing = effects.scanlines.spacing ?? 3;

            const scanlines = document.createElement('div');
            scanlines.className = 'theme-effect-scanlines';
            scanlines.style.cssText =
                'position: fixed;' +
                'inset: 0;' +
                'z-index: 9999;' +
                'pointer-events: none;' +
                'background: repeating-linear-gradient(' +
                    '0deg,' +
                    'rgba(0, 0, 0, ' + opacity + '),' +
                    'rgba(0, 0, 0, ' + opacity + ') 1px,' +
                    'transparent 1px,' +
                    'transparent ' + spacing + 'px' +
                ');';
            effectsContainer.appendChild(scanlines);
        }

        // Vignette
        if (effects.vignette?.enabled) {
            const strength = effects.vignette.strength ?? 0.5;

            const vignette = document.createElement('div');
            vignette.className = 'theme-effect-vignette';
            vignette.style.cssText =
                'position: fixed;' +
                'inset: 0;' +
                'z-index: 9998;' +
                'pointer-events: none;' +
                'background: radial-gradient(' +
                    'ellipse at center,' +
                    'transparent 0%,' +
                    'transparent 50%,' +
                    'rgba(0, 0, 0, ' + strength + ') 100%' +
                ');';
            effectsContainer.appendChild(vignette);
        }

        // CRT Flicker
        if (effects.crtFlicker?.enabled) {
            const intensity = effects.crtFlicker.intensity ?? 0.04;
            addDynamicCSS('effects',
                '@keyframes theme-crt-flicker {' +
                    '0% { opacity: ' + (1 - intensity) + '; }' +
                    '50% { opacity: 1; }' +
                    '100% { opacity: ' + (1 - intensity * 0.75) + '; }' +
                '}' +
                '.theme-effect-scanlines {' +
                    'animation: theme-crt-flicker 0.1s infinite;' +
                '}'
            );
        }

        // Rain effect
        if (effects.rain?.enabled) {
            const color = effects.rain.color ?? 'rgba(1, 205, 254, 0.03)';
            const speed = effects.rain.speed ?? 0.5;

            const rain = document.createElement('div');
            rain.className = 'theme-effect-rain';
            rain.style.cssText =
                'position: fixed;' +
                'inset: 0;' +
                'z-index: 9997;' +
                'pointer-events: none;' +
                'background: repeating-linear-gradient(' +
                    '180deg,' +
                    'transparent,' +
                    'transparent 2px,' +
                    color + ' 2px,' +
                    color + ' 4px' +
                ');';
            effectsContainer.appendChild(rain);

            addDynamicCSS('effects-rain',
                '@keyframes theme-rain-fall {' +
                    '0% { transform: translateY(-4px); }' +
                    '100% { transform: translateY(0); }' +
                '}' +
                '.theme-effect-rain {' +
                    'animation: theme-rain-fall ' + speed + 's linear infinite;' +
                '}'
            );
        }

        // Bloom filter
        if (effects.bloom?.enabled) {
            const contrast = effects.bloom.contrast ?? 1.1;
            const brightness = effects.bloom.brightness ?? 1.05;
            document.body.style.filter = 'contrast(' + contrast + ') brightness(' + brightness + ')';
        }
    }

    function applyFonts(fonts) {
        if (!fonts) return;
        const root = document.documentElement;

        if (fonts.primary) {
            root.style.setProperty('--font-primary', fonts.primary);
        }
        if (fonts.mono) {
            root.style.setProperty('--font-mono', fonts.mono);
        }
    }

    function applyAnimations(animations) {
        clearDynamicCSS('animations');
        if (!animations) return;

        let css = '';
        for (const [name, anim] of Object.entries(animations)) {
            if (anim.keyframes) {
                css += anim.keyframes + '\n';
            }
        }
        if (css) {
            addDynamicCSS('animations', css);
        }
    }

    function applyOverrideCSS(cssText) {
        clearDynamicCSS('overrides');
        if (cssText) {
            addDynamicCSS('overrides', cssText);
        }
    }

    function updateHtmlClass(theme) {
        const html = document.documentElement;

        // Remove all theme classes (keep list of known themes)
        html.classList.remove('dark', 'neon-noir', 'light');
        delete html.dataset.theme;

        // Add dark class for dark-based themes (for Tailwind dark: utilities)
        const isDark = theme.meta?.category === 'dark' ||
                       theme.id === 'dark' ||
                       theme.id === 'neon-noir';
        if (isDark) {
            html.classList.add('dark');
        }

        // Add theme ID as class for CSS selector compatibility
        // This allows CSS like .neon-noir .tile-card to work
        if (theme.id && theme.id !== 'light') {
            html.classList.add(theme.id);
        }

        // Set theme ID as data attribute for any CSS that needs it
        if (theme.id) {
            html.dataset.theme = theme.id;
        }
    }

    function dispatchThemeChangeEvent(theme) {
        // Custom event for components to listen to
        window.dispatchEvent(new CustomEvent('themechange', {
            detail: { theme: theme, themeId: theme.id }
        }));

        // Direct chart update for backward compatibility with existing code
        if (typeof App !== 'undefined' && App.updateChartTheme) {
            App.updateChartTheme();
        }
    }

    function fallbackToCSSThemes() {
        // Fall back to existing CSS-class-based theming
        const savedTheme = localStorage.getItem('theme') || 'light';
        const html = document.documentElement;
        html.classList.remove('dark', 'neon-noir');

        if (savedTheme === 'dark') {
            html.classList.add('dark');
        } else if (savedTheme === 'neon-noir') {
            html.classList.add('dark', 'neon-noir');
        }

        console.log('Using CSS fallback theming');
    }

    // ===== DOM HELPERS =====

    function createEffectsContainer() {
        effectsContainer = document.getElementById('theme-effects');
        if (!effectsContainer) {
            effectsContainer = document.createElement('div');
            effectsContainer.id = 'theme-effects';
            effectsContainer.setAttribute('aria-hidden', 'true');
            document.body.appendChild(effectsContainer);
        }
    }

    function createDynamicStyleSheet() {
        dynamicStyleSheet = document.getElementById('theme-dynamic-styles');
        if (!dynamicStyleSheet) {
            dynamicStyleSheet = document.createElement('style');
            dynamicStyleSheet.id = 'theme-dynamic-styles';
            document.head.appendChild(dynamicStyleSheet);
        }
        dynamicStyleSheet._sections = dynamicStyleSheet._sections || {};
    }

    function addDynamicCSS(sectionId, cssText) {
        if (!dynamicStyleSheet) createDynamicStyleSheet();
        dynamicStyleSheet._sections[sectionId] = cssText;
        rebuildDynamicStyleSheet();
    }

    function clearDynamicCSS(sectionId) {
        if (!dynamicStyleSheet?._sections) return;
        delete dynamicStyleSheet._sections[sectionId];
        rebuildDynamicStyleSheet();
    }

    function rebuildDynamicStyleSheet() {
        if (!dynamicStyleSheet) return;
        dynamicStyleSheet.textContent = Object.values(dynamicStyleSheet._sections || {}).join('\n');
    }

    // ===== EXPORT =====

    return {
        init: init,
        applyTheme: applyTheme,
        applyThemeJson: applyThemeJson,
        getCurrentTheme: getCurrentTheme,
        getCurrentThemeId: getCurrentThemeId,
        getThemeColors: getThemeColors,
        getAvailableThemes: getAvailableThemes,
        isCurrentTheme: isCurrentTheme,
    };
})();

// Export for module systems if needed
if (typeof module !== 'undefined' && module.exports) {
    module.exports = ThemeLoader;
}
