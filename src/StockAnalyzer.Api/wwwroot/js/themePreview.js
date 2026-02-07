/**
 * Theme Preview Component
 * A self-contained miniature mock-up of the Stock Analyzer app for theme preview.
 *
 * Usage:
 *   const preview = ThemePreview.create(containerElement);
 *   preview.applyTheme(themeJson);
 *   preview.destroy();
 */
const ThemePreview = (function() {
    'use strict';

    // Sample data for the fake chart
    const SAMPLE_CHART_DATA = [
        { x: 0, y: 100 }, { x: 1, y: 102 }, { x: 2, y: 98 }, { x: 3, y: 105 },
        { x: 4, y: 103 }, { x: 5, y: 108 }, { x: 6, y: 112 }, { x: 7, y: 107 },
        { x: 8, y: 115 }, { x: 9, y: 118 }, { x: 10, y: 114 }, { x: 11, y: 120 },
        { x: 12, y: 125 }, { x: 13, y: 122 }, { x: 14, y: 128 }
    ];

    // Sample watchlist data
    const SAMPLE_WATCHLIST = [
        { ticker: 'AAPL', name: 'Apple Inc.', change: '+2.34%', positive: true },
        { ticker: 'MSFT', name: 'Microsoft', change: '-0.87%', positive: false },
        { ticker: 'GOOGL', name: 'Alphabet', change: '+1.56%', positive: true }
    ];

    // Sample metrics
    const SAMPLE_METRICS = [
        { label: 'Market Cap', value: '$2.8T' },
        { label: 'P/E Ratio', value: '28.5' },
        { label: '52W High', value: '$198.23' },
        { label: '52W Low', value: '$124.17' }
    ];

    /**
     * Create the preview HTML structure
     */
    function createPreviewHTML() {
        return `
            <div class="theme-preview-container">
                <!-- Effects container (for scanlines, rain, etc.) -->
                <div class="preview-effects"></div>

                <!-- Header -->
                <header class="preview-header">
                    <div class="preview-header-content">
                        <div class="preview-logo">
                            <svg class="preview-logo-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 7h8m0 0v8m0-8l-8 8-4-4-6 6"></path>
                            </svg>
                            <span class="preview-logo-text">Stock Analyzer</span>
                        </div>
                        <div class="preview-header-buttons">
                            <button class="preview-btn-icon" title="Watchlist">
                                <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11.049 2.927c.3-.921 1.603-.921 1.902 0l1.519 4.674a1 1 0 00.95.69h4.915c.969 0 1.371 1.24.588 1.81l-3.976 2.888a1 1 0 00-.363 1.118l1.518 4.674c.3.922-.755 1.688-1.538 1.118l-3.976-2.888a1 1 0 00-1.176 0l-3.976 2.888c-.783.57-1.838-.197-1.538-1.118l1.518-4.674a1 1 0 00-.363-1.118l-3.976-2.888c-.784-.57-.38-1.81.588-1.81h4.914a1 1 0 00.951-.69l1.519-4.674z"/>
                                </svg>
                            </button>
                            <button class="preview-btn-icon preview-theme-btn" title="Theme">
                                <svg fill="currentColor" viewBox="0 0 20 20">
                                    <path fill-rule="evenodd" d="M11.3 1.046A1 1 0 0112 2v5h4a1 1 0 01.82 1.573l-7 10A1 1 0 018 18v-5H4a1 1 0 01-.82-1.573l7-10a1 1 0 011.12-.38z" clip-rule="evenodd"/>
                                </svg>
                            </button>
                        </div>
                    </div>
                </header>

                <!-- Main Content -->
                <main class="preview-main">
                    <!-- Search Section -->
                    <div class="preview-search-section">
                        <div class="preview-search-row">
                            <div class="preview-input-group">
                                <label>Search Ticker</label>
                                <input type="text" value="AAPL" readonly class="preview-input">
                            </div>
                            <div class="preview-input-group">
                                <label>Compare To</label>
                                <input type="text" value="SPY" readonly class="preview-input">
                            </div>
                            <button class="preview-btn-primary">Analyze</button>
                            <button class="preview-btn-secondary">Clear</button>
                        </div>
                        <div class="preview-controls-row">
                            <label class="preview-checkbox">
                                <input type="checkbox" checked disabled>
                                <span>SMA 20</span>
                            </label>
                            <label class="preview-checkbox">
                                <input type="checkbox" checked disabled>
                                <span>SMA 50</span>
                            </label>
                            <label class="preview-checkbox">
                                <input type="checkbox" disabled>
                                <span>RSI</span>
                            </label>
                            <label class="preview-checkbox">
                                <input type="checkbox" disabled>
                                <span>MACD</span>
                            </label>
                        </div>
                    </div>

                    <!-- Content Grid -->
                    <div class="preview-grid">
                        <!-- Chart Tile -->
                        <div class="preview-tile preview-tile-chart">
                            <div class="preview-tile-header">
                                <span class="preview-tile-title">
                                    <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 7h8m0 0v8m0-8l-8 8-4-4-6 6"/>
                                    </svg>
                                    Stock Chart
                                </span>
                                <span class="preview-tile-actions">
                                    <button class="preview-tile-btn">🔒</button>
                                    <button class="preview-tile-btn">&times;</button>
                                </span>
                            </div>
                            <div class="preview-tile-body">
                                <canvas class="preview-chart-canvas"></canvas>
                                <!-- Significant move markers -->
                                <div class="preview-marker preview-marker-up" style="left: 40%; top: 30%;" title="+7.2%">▲</div>
                                <div class="preview-marker preview-marker-down" style="left: 70%; top: 60%;" title="-5.8%">▼</div>
                            </div>
                        </div>

                        <!-- Watchlist Tile -->
                        <div class="preview-tile preview-tile-watchlist">
                            <div class="preview-tile-header">
                                <span class="preview-tile-title">
                                    <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11.049 2.927c.3-.921 1.603-.921 1.902 0l1.519 4.674a1 1 0 00.95.69h4.915c.969 0 1.371 1.24.588 1.81l-3.976 2.888a1 1 0 00-.363 1.118l1.518 4.674c.3.922-.755 1.688-1.538 1.118l-3.976-2.888a1 1 0 00-1.176 0l-3.976 2.888c-.783.57-1.838-.197-1.538-1.118l1.518-4.674a1 1 0 00-.363-1.118l-3.976-2.888c-.784-.57-.38-1.81.588-1.81h4.914a1 1 0 00.951-.69l1.519-4.674z"/>
                                    </svg>
                                    My Watchlist
                                </span>
                                <span class="preview-tile-actions">
                                    <button class="preview-tile-btn">🔒</button>
                                    <button class="preview-tile-btn">&times;</button>
                                </span>
                            </div>
                            <div class="preview-tile-body preview-watchlist-body">
                                ${SAMPLE_WATCHLIST.map(item => `
                                    <div class="preview-watchlist-item">
                                        <div class="preview-watchlist-ticker">${item.ticker}</div>
                                        <div class="preview-watchlist-name">${item.name}</div>
                                        <div class="preview-watchlist-change ${item.positive ? 'positive' : 'negative'}">${item.change}</div>
                                    </div>
                                `).join('')}
                            </div>
                        </div>

                        <!-- Metrics Tile -->
                        <div class="preview-tile preview-tile-metrics">
                            <div class="preview-tile-header">
                                <span class="preview-tile-title">
                                    <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 7h6m0 10v-3m-3 3v-6m-3 6v-1M3 3h18v18H3z"/>
                                    </svg>
                                    Key Metrics
                                </span>
                                <span class="preview-tile-actions">
                                    <button class="preview-tile-btn">🔒</button>
                                    <button class="preview-tile-btn">&times;</button>
                                </span>
                            </div>
                            <div class="preview-tile-body preview-metrics-body">
                                ${SAMPLE_METRICS.map(m => `
                                    <div class="preview-metric">
                                        <span class="preview-metric-label">${m.label}</span>
                                        <span class="preview-metric-value">${m.value}</span>
                                    </div>
                                `).join('')}
                            </div>
                        </div>
                    </div>
                </main>

                <!-- Footer -->
                <footer class="preview-footer">
                    <span>Stock Analyzer v4.0.0</span>
                    <span class="preview-footer-links">
                        <a href="#">About</a> | <a href="#">Docs</a> | <a href="#">GitHub</a>
                    </span>
                </footer>
            </div>
        `;
    }

    /**
     * Create the preview styles (injected into the preview container)
     */
    function createPreviewStyles() {
        return `
            .theme-preview-container {
                position: relative;
                width: 100%;
                height: 100%;
                min-height: 400px;
                overflow: hidden;
                font-family: var(--font-primary, ui-sans-serif, system-ui, sans-serif);
                font-size: 11px;
                background: var(--bg-primary, #ffffff);
                color: var(--text-primary, #1f2937);
                border-radius: var(--radius-lg, 8px);
                box-shadow: inset 0 0 0 1px var(--border-primary, #e5e7eb);
            }

            /* Effects overlay */
            .preview-effects {
                position: absolute;
                inset: 0;
                pointer-events: none;
                z-index: 100;
            }

            /* Header */
            .preview-header {
                position: relative;
                z-index: 2;
                background: var(--bg-secondary, #f9fafb);
                border-bottom: 1px solid var(--border-primary, #e5e7eb);
                padding: 8px 12px;
                border-radius: var(--radius-lg, 8px) var(--radius-lg, 8px) 0 0;
            }
            .preview-header-content {
                display: flex;
                justify-content: space-between;
                align-items: center;
            }
            .preview-logo {
                display: flex;
                align-items: center;
                gap: 6px;
            }
            .preview-logo-icon {
                width: 16px;
                height: 16px;
                color: var(--accent, #3b82f6);
            }
            .preview-logo-text {
                font-weight: bold;
                font-size: 13px;
                color: var(--text-primary, #1f2937);
            }
            .preview-header-buttons {
                display: flex;
                gap: 4px;
            }
            .preview-btn-icon {
                width: 24px;
                height: 24px;
                display: flex;
                align-items: center;
                justify-content: center;
                background: var(--bg-tertiary, #e5e7eb);
                border: none;
                border-radius: var(--radius-md, 6px);
                cursor: pointer;
                color: var(--text-secondary, #6b7280);
            }
            .preview-btn-icon svg {
                width: 12px;
                height: 12px;
            }
            .preview-theme-btn svg {
                color: var(--accent, #3b82f6);
            }

            /* Main content */
            .preview-main {
                position: relative;
                z-index: 2;
                padding: 8px;
            }

            /* Search section */
            .preview-search-section {
                background: var(--bg-secondary, #f9fafb);
                border: 1px solid var(--border-primary, #e5e7eb);
                border-radius: var(--radius-md, 6px);
                padding: 8px;
                margin-bottom: 8px;
            }
            .preview-search-row {
                display: flex;
                gap: 6px;
                align-items: flex-end;
                margin-bottom: 6px;
            }
            .preview-input-group {
                flex: 1;
            }
            .preview-input-group label {
                display: block;
                font-size: 9px;
                color: var(--text-secondary, #6b7280);
                margin-bottom: 2px;
            }
            .preview-input {
                width: 100%;
                padding: 4px 6px;
                border: 1px solid var(--border-primary, #e5e7eb);
                border-radius: var(--radius-sm, 4px);
                background: var(--bg-primary, #ffffff);
                color: var(--text-primary, #1f2937);
                font-size: 10px;
            }
            .preview-btn-primary {
                padding: 4px 10px;
                background: var(--accent, #3b82f6);
                color: var(--btn-primary-text, #ffffff);
                border: none;
                border-radius: var(--radius-sm, 4px);
                font-size: 10px;
                font-weight: 500;
                cursor: pointer;
            }
            .preview-btn-secondary {
                padding: 4px 10px;
                background: var(--bg-tertiary, #e5e7eb);
                color: var(--text-primary, #1f2937);
                border: none;
                border-radius: var(--radius-sm, 4px);
                font-size: 10px;
                cursor: pointer;
            }
            .preview-controls-row {
                display: flex;
                gap: 8px;
                flex-wrap: wrap;
            }
            .preview-checkbox {
                display: flex;
                align-items: center;
                gap: 3px;
                font-size: 9px;
                color: var(--text-secondary, #6b7280);
            }
            .preview-checkbox input {
                width: 10px;
                height: 10px;
                accent-color: var(--accent, #3b82f6);
            }

            /* Content grid */
            .preview-grid {
                display: grid;
                grid-template-columns: 2fr 1fr;
                grid-template-rows: auto auto;
                gap: 6px;
            }
            .preview-tile-chart {
                grid-row: span 2;
            }

            /* Tiles */
            .preview-tile {
                background: var(--bg-secondary, #f9fafb);
                border: 1px solid var(--border-primary, #e5e7eb);
                border-radius: var(--radius-md, 6px);
                overflow: hidden;
            }
            .preview-tile-header {
                display: flex;
                justify-content: space-between;
                align-items: center;
                padding: 6px 8px;
                background: var(--bg-tertiary, #e5e7eb);
                border-bottom: 1px solid var(--border-primary, #e5e7eb);
            }
            .preview-tile-title {
                display: flex;
                align-items: center;
                gap: 4px;
                font-weight: 600;
                font-size: 10px;
                color: var(--text-primary, #1f2937);
            }
            .preview-tile-title svg {
                width: 12px;
                height: 12px;
                color: var(--accent, #3b82f6);
            }
            .preview-tile-actions {
                display: flex;
                gap: 2px;
            }
            .preview-tile-btn {
                width: 16px;
                height: 16px;
                display: flex;
                align-items: center;
                justify-content: center;
                background: transparent;
                border: none;
                font-size: 10px;
                color: var(--text-secondary, #6b7280);
                cursor: pointer;
                border-radius: 2px;
            }
            .preview-tile-btn:hover {
                background: var(--bg-primary, #ffffff);
            }
            .preview-tile-body {
                padding: 8px;
                position: relative;
            }

            /* Chart canvas */
            .preview-chart-canvas {
                width: 100%;
                height: 120px;
                display: block;
            }

            /* Significant move markers */
            .preview-marker {
                position: absolute;
                font-size: 10px;
                cursor: pointer;
                animation: pulse 2s infinite;
            }
            .preview-marker-up {
                color: var(--success, #10b981);
                text-shadow: 0 0 4px var(--success, #10b981);
            }
            .preview-marker-down {
                color: var(--error, #ef4444);
                text-shadow: 0 0 4px var(--error, #ef4444);
            }
            @keyframes pulse {
                0%, 100% { opacity: 1; }
                50% { opacity: 0.6; }
            }

            /* Watchlist */
            .preview-watchlist-body {
                padding: 4px 8px;
            }
            .preview-watchlist-item {
                display: flex;
                align-items: center;
                gap: 6px;
                padding: 4px 0;
                border-bottom: 1px solid var(--border-primary, #e5e7eb);
            }
            .preview-watchlist-item:last-child {
                border-bottom: none;
            }
            .preview-watchlist-ticker {
                font-weight: 600;
                color: var(--accent, #3b82f6);
                min-width: 35px;
            }
            .preview-watchlist-name {
                flex: 1;
                color: var(--text-secondary, #6b7280);
                font-size: 9px;
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
            }
            .preview-watchlist-change {
                font-weight: 500;
                font-size: 9px;
            }
            .preview-watchlist-change.positive {
                color: var(--success, #10b981);
            }
            .preview-watchlist-change.negative {
                color: var(--error, #ef4444);
            }

            /* Metrics */
            .preview-metrics-body {
                display: grid;
                grid-template-columns: 1fr 1fr;
                gap: 4px;
            }
            .preview-metric {
                display: flex;
                flex-direction: column;
            }
            .preview-metric-label {
                font-size: 8px;
                color: var(--text-secondary, #6b7280);
            }
            .preview-metric-value {
                font-weight: 600;
                font-size: 10px;
                color: var(--text-primary, #1f2937);
            }

            /* Footer */
            .preview-footer {
                position: relative;
                z-index: 2;
                display: flex;
                justify-content: space-between;
                align-items: center;
                padding: 6px 12px;
                background: var(--bg-secondary, #f9fafb);
                border-top: 1px solid var(--border-primary, #e5e7eb);
                font-size: 9px;
                color: var(--text-secondary, #6b7280);
                border-radius: 0 0 var(--radius-lg, 8px) var(--radius-lg, 8px);
            }
            .preview-footer-links a {
                color: var(--accent, #3b82f6);
                text-decoration: none;
            }

            /* Effects - Scanlines */
            .preview-effects.scanlines::before {
                content: '';
                position: absolute;
                inset: 0;
                background: repeating-linear-gradient(
                    0deg,
                    transparent,
                    transparent 1px,
                    rgba(0, 0, 0, var(--scanline-opacity, 0.1)) 1px,
                    rgba(0, 0, 0, var(--scanline-opacity, 0.1)) 2px
                );
                pointer-events: none;
            }

            /* Effects - Vignette */
            .preview-effects.vignette::after {
                content: '';
                position: absolute;
                inset: 0;
                background: radial-gradient(ellipse at center, transparent 50%, rgba(0,0,0,var(--vignette-opacity, 0.3)) 100%);
                pointer-events: none;
            }

            /* Effects - Rain */
            .preview-effects .rain-drops {
                position: absolute;
                inset: 0;
                overflow: hidden;
            }
            .preview-effects .rain-drop {
                position: absolute;
                width: 1px;
                height: 15px;
                background: linear-gradient(to bottom, transparent, var(--rain-color, rgba(1,205,254,0.3)));
                animation: rain-fall linear infinite;
            }
            @keyframes rain-fall {
                0% { transform: translateY(-20px); }
                100% { transform: translateY(calc(100% + 20px)); }
            }

            /* CRT Flicker */
            .preview-effects.crt-flicker {
                animation: crt-flicker 0.15s infinite;
            }
            @keyframes crt-flicker {
                0% { opacity: 0.97; }
                50% { opacity: 1; }
                100% { opacity: 0.98; }
            }
        `;
    }

    /**
     * Draw a simple line chart on canvas
     */
    function drawChart(canvas, themeColors) {
        const ctx = canvas.getContext('2d');
        const rect = canvas.getBoundingClientRect();
        canvas.width = rect.width * 2;
        canvas.height = rect.height * 2;
        ctx.scale(2, 2);

        const width = rect.width;
        const height = rect.height;
        const padding = 10;

        // Clear
        ctx.fillStyle = themeColors.chartBg || '#ffffff';
        ctx.fillRect(0, 0, width, height);

        // Grid
        ctx.strokeStyle = themeColors.chartGrid || '#e5e7eb';
        ctx.lineWidth = 0.5;
        for (let i = 0; i <= 4; i++) {
            const y = padding + (height - 2 * padding) * (i / 4);
            ctx.beginPath();
            ctx.moveTo(padding, y);
            ctx.lineTo(width - padding, y);
            ctx.stroke();
        }

        // Calculate scale
        const minY = Math.min(...SAMPLE_CHART_DATA.map(d => d.y)) - 5;
        const maxY = Math.max(...SAMPLE_CHART_DATA.map(d => d.y)) + 5;
        const xScale = (width - 2 * padding) / (SAMPLE_CHART_DATA.length - 1);
        const yScale = (height - 2 * padding) / (maxY - minY);

        // Draw line with glow
        ctx.strokeStyle = themeColors.linePrimary || '#3b82f6';
        ctx.lineWidth = 2;

        // Glow effect
        if (themeColors.lineGlow) {
            ctx.shadowColor = themeColors.linePrimary || '#3b82f6';
            ctx.shadowBlur = 8;
        }

        ctx.beginPath();
        SAMPLE_CHART_DATA.forEach((point, i) => {
            const x = padding + i * xScale;
            const y = height - padding - (point.y - minY) * yScale;
            if (i === 0) ctx.moveTo(x, y);
            else ctx.lineTo(x, y);
        });
        ctx.stroke();
        ctx.shadowBlur = 0;

        // Draw SMA line (fake - just offset)
        ctx.strokeStyle = themeColors.lineSma20 || '#f59e0b';
        ctx.lineWidth = 1;
        ctx.setLineDash([3, 3]);
        ctx.beginPath();
        SAMPLE_CHART_DATA.forEach((point, i) => {
            const x = padding + i * xScale;
            const y = height - padding - (point.y - 3 - minY) * yScale;
            if (i === 0) ctx.moveTo(x, y);
            else ctx.lineTo(x, y);
        });
        ctx.stroke();
        ctx.setLineDash([]);
    }

    /**
     * Apply effects based on theme JSON
     */
    function applyEffects(effectsContainer, effects) {
        // Clear existing effects
        effectsContainer.className = 'preview-effects';
        effectsContainer.innerHTML = '';

        // Stop any active canvas effects
        if (typeof CanvasEffects !== 'undefined') {
            CanvasEffects.stopAll();
        }

        if (!effects) return;

        // Scanlines
        if (effects.scanlines?.enabled) {
            effectsContainer.classList.add('scanlines');
            effectsContainer.style.setProperty('--scanline-opacity', effects.scanlines.opacity || 0.1);
        }

        // Vignette
        if (effects.vignette?.enabled) {
            effectsContainer.classList.add('vignette');
            effectsContainer.style.setProperty('--vignette-opacity', effects.vignette.opacity || 0.3);
        }

        // CRT Flicker
        if (effects.crtFlicker?.enabled) {
            effectsContainer.classList.add('crt-flicker');
        }

        // Rain (CSS-based simple rain)
        if (effects.rain?.enabled) {
            const rainContainer = document.createElement('div');
            rainContainer.className = 'rain-drops';
            const dropCount = 15;
            const color = effects.rain.color || 'rgba(1,205,254,0.3)';
            effectsContainer.style.setProperty('--rain-color', color);

            for (let i = 0; i < dropCount; i++) {
                const drop = document.createElement('div');
                drop.className = 'rain-drop';
                drop.style.left = `${Math.random() * 100}%`;
                drop.style.animationDuration = `${0.5 + Math.random() * 0.5}s`;
                drop.style.animationDelay = `${Math.random() * 2}s`;
                rainContainer.appendChild(drop);
            }
            effectsContainer.appendChild(rainContainer);
        }

        // Canvas-based effects (requires canvasEffects.js)
        if (typeof CanvasEffects !== 'undefined') {
            const previewContainer = effectsContainer.closest('.theme-preview-container');

            // Matrix Rain - authentic falling characters
            if (effects.matrixRain?.enabled && previewContainer) {
                CanvasEffects.start('matrixRain', previewContainer, {
                    color: effects.matrixRain.color || '#00ff41',
                    backgroundColor: effects.matrixRain.backgroundColor || 'rgba(0, 0, 0, 0.05)',
                    fontSize: effects.matrixRain.fontSize || 14,
                    speed: effects.matrixRain.speed || 1,
                    density: effects.matrixRain.density || 0.98,
                    characters: effects.matrixRain.characters,
                    glowIntensity: effects.matrixRain.glowIntensity || 0.8
                });
            }

            // Snow effect
            if (effects.snow?.enabled && previewContainer) {
                CanvasEffects.start('snow', previewContainer, {
                    color: effects.snow.color || '#ffffff',
                    count: effects.snow.count || 100,
                    speed: effects.snow.speed || 1,
                    wind: effects.snow.wind || 0.5
                });
            }

            // Particles effect
            if (effects.particles?.enabled && previewContainer) {
                CanvasEffects.start('particles', previewContainer, {
                    color: effects.particles.color || '#ffffff',
                    count: effects.particles.count || 50,
                    speed: effects.particles.speed || 0.5,
                    connections: effects.particles.connections !== false,
                    connectionDistance: effects.particles.connectionDistance || 100
                });
            }
        }
    }

    /**
     * Create a preview instance
     */
    function create(container) {
        // Inject styles
        const styleId = 'theme-preview-styles';
        if (!document.getElementById(styleId)) {
            const style = document.createElement('style');
            style.id = styleId;
            style.textContent = createPreviewStyles();
            document.head.appendChild(style);
        }

        // Create preview DOM
        container.innerHTML = createPreviewHTML();
        const previewEl = container.querySelector('.theme-preview-container');
        const canvas = container.querySelector('.preview-chart-canvas');
        const effectsEl = container.querySelector('.preview-effects');

        // Initial draw with default colors
        setTimeout(() => {
            drawChart(canvas, {});
        }, 0);

        return {
            element: previewEl,

            /**
             * Apply a theme JSON to the preview
             */
            applyTheme: function(themeJson) {
                if (!themeJson) return;

                // Apply CSS variables via scoped <style> element
                // This ensures variables override the main app's :root variables
                const variables = themeJson.variables || {};
                const dynamicStyleId = 'theme-preview-dynamic-vars';
                let dynamicStyle = document.getElementById(dynamicStyleId);
                if (!dynamicStyle) {
                    dynamicStyle = document.createElement('style');
                    dynamicStyle.id = dynamicStyleId;
                    document.head.appendChild(dynamicStyle);
                }

                // Build CSS with variables scoped to preview container
                let cssVars = '';
                Object.entries(variables).forEach(([key, value]) => {
                    cssVars += `--${key}: ${value};\n`;
                });
                if (themeJson.fonts?.primary) {
                    cssVars += `--font-primary: ${themeJson.fonts.primary};\n`;
                }
                if (themeJson.fonts?.mono) {
                    cssVars += `--font-mono: ${themeJson.fonts.mono};\n`;
                }

                // Build customCSS rules if present
                let customCssRules = '';
                if (themeJson.customCSS && typeof CssSanitizer !== 'undefined') {
                    const sanitizedCustom = CssSanitizer.sanitizeCustomCss(themeJson.customCSS);
                    customCssRules = CssSanitizer.generateScopedCss(sanitizedCustom);
                }

                // Apply to preview container and ALL descendants
                dynamicStyle.textContent = `
                    .theme-preview-container,
                    .theme-preview-container * {
                        ${cssVars}
                    }
                    ${customCssRules}
                `;

                // Redraw chart with theme colors
                const themeColors = {
                    chartBg: variables['chart-bg'] || variables['bg-primary'],
                    chartGrid: variables['chart-grid'] || variables['border-primary'],
                    linePrimary: variables['chart-line-primary'] || variables['accent'],
                    lineSma20: variables['chart-line-sma20'] || '#f59e0b',
                    lineGlow: themeJson.effects?.bloom?.enabled || themeJson.id === 'neon-noir'
                };
                drawChart(canvas, themeColors);

                // Apply effects
                applyEffects(effectsEl, themeJson.effects);
            },

            /**
             * Get the current theme's CSS variables
             */
            getVariables: function() {
                const style = getComputedStyle(previewEl);
                const vars = {};
                for (const prop of style) {
                    if (prop.startsWith('--')) {
                        vars[prop.substring(2)] = style.getPropertyValue(prop).trim();
                    }
                }
                return vars;
            },

            /**
             * Destroy the preview
             */
            destroy: function() {
                container.innerHTML = '';
            }
        };
    }

    // Public API
    return {
        create: create
    };
})();

// Export for module systems
if (typeof module !== 'undefined' && module.exports) {
    module.exports = ThemePreview;
}
