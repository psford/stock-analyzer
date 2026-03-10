/**
 * Stock Chart Configuration
 * Handles Plotly.js chart rendering with technical indicators
 */
const Charts = {
    // Track which chart elements have been initially rendered (for Plotly.react optimization)
    _renderedCharts: {},

    /**
     * Smart plot: uses newPlot for first render, react for subsequent renders.
     * Plotly.react does a diff-based update (no DOM teardown), much faster for re-renders.
     */
    _smartPlot(elementId, traces, layout, config) {
        const plotFn = this._renderedCharts[elementId] ? Plotly.react : Plotly.newPlot;
        return plotFn(elementId, traces, layout, config).then(() => {
            this._renderedCharts[elementId] = true;
            const chartEl = document.getElementById(elementId);
            if (chartEl) {
                Plotly.Plots.resize(chartEl);
            }
        });
    },

    /**
     * Reset chart state for a new ticker (forces newPlot on next render)
     */
    resetChart(elementId) {
        delete this._renderedCharts[elementId];
    },

    /**
     * Format the period label for chart titles.
     * Shows actual date range for custom periods instead of "CUSTOM".
     */
    formatPeriodLabel(historyData) {
        if (historyData.startDate && historyData.endDate) {
            const fmt = (d) => new Date(d).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
            return `${fmt(historyData.startDate)} - ${fmt(historyData.endDate)}`;
        }
        return historyData.period ? historyData.period.toUpperCase() : '';
    },

    /**
     * Attach a plotly_relayout listener that updates the chart title
     * to reflect the currently visible date range on scroll/zoom.
     */
    _attachDynamicTitle(elementId, symbol, comparisonSymbol) {
        const chartEl = document.getElementById(elementId);
        if (!chartEl) return;

        // Remove previous listener to avoid duplicates on Plotly.react re-renders
        if (chartEl._titleUpdater) {
            chartEl.removeListener('plotly_relayout', chartEl._titleUpdater);
        }

        const fmt = (dateStr) => {
            const d = new Date(dateStr);
            if (isNaN(d.getTime())) return '';
            return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
        };

        const prefix = comparisonSymbol
            ? `${symbol} vs ${comparisonSymbol}`
            : symbol;

        chartEl._titleUpdater = (eventData) => {
            // Only react to x-axis range changes
            if (!eventData['xaxis.range'] &&
                !eventData['xaxis.range[0]'] &&
                !eventData['xaxis.autorange']) return;

            // Read the current range from the layout (up to date after relayout)
            const range = chartEl._fullLayout?.xaxis?.range;
            if (!range || range.length < 2) return;

            const startFmt = fmt(range[0]);
            const endFmt = fmt(range[1]);
            if (!startFmt || !endFmt) return;

            // Update DOM directly to avoid infinite relayout loop
            const titleEl = chartEl.querySelector('.gtitle');
            if (titleEl) {
                titleEl.textContent = `${prefix} - ${startFmt} - ${endFmt}`;
            }
        };

        chartEl.on('plotly_relayout', chartEl._titleUpdater);
    },

    /**
     * Check if dark mode is currently enabled
     */
    isDarkMode() {
        return document.documentElement.classList.contains('dark');
    },

    /**
     * Read a CSS custom property value from :root
     */
    getCssVar(name) {
        return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    },

    /**
     * Get theme-aware colors from CSS variables
     * Themes define these in input.css, JS just reads them
     */
    getThemeColors() {
        return {
            // Layout colors
            background: this.getCssVar('--chart-bg') || '#ffffff',
            paper: this.getCssVar('--chart-bg') || '#ffffff',
            text: this.getCssVar('--chart-text') || '#1f2937',
            gridColor: this.getCssVar('--chart-grid') || '#e5e7eb',
            axisColor: this.getCssVar('--chart-axis') || '#6b7280',
            // Line colors
            linePrimary: this.getCssVar('--chart-line-primary') || '#3b82f6',
            lineSecondary: this.getCssVar('--chart-line-secondary') || '#f59e0b',
            sma20: this.getCssVar('--chart-line-sma20') || '#f59e0b',
            sma50: this.getCssVar('--chart-line-sma50') || '#8b5cf6',
            sma200: this.getCssVar('--chart-line-sma200') || '#ec4899',
            // Candlestick/volume
            candleUp: this.getCssVar('--chart-candle-up') || '#10b981',
            candleDown: this.getCssVar('--chart-candle-down') || '#ef4444',
            volumeUp: this.getCssVar('--chart-volume-up') || '#065f46',
            volumeDown: this.getCssVar('--chart-volume-down') || '#991b1b',
            // Indicators
            rsi: this.getCssVar('--chart-rsi') || '#8b5cf6',
            macd: this.getCssVar('--chart-macd') || '#3b82f6',
            macdSignal: this.getCssVar('--chart-macd-signal') || '#f59e0b',
            stochastic: this.getCssVar('--chart-stochastic') || '#14b8a6',
            stochasticD: this.getCssVar('--chart-stochastic-d') || '#f59e0b',
            overbought: this.getCssVar('--chart-overbought') || '#ef4444',
            oversold: this.getCssVar('--chart-oversold') || '#10b981',
            bollinger: this.getCssVar('--chart-bollinger') || '#6366f1',
            // Glow effect (for neon themes)
            glowEnabled: this.getCssVar('--chart-line-glow') === 'enabled',
            glowColor: this.getCssVar('--chart-line-glow-color') || 'transparent',
            glowWidth: parseInt(this.getCssVar('--chart-line-glow-width')) || 0,
            // Significant move markers
            markerUp: this.getCssVar('--chart-marker-up') || '#10b981',
            markerDown: this.getCssVar('--chart-marker-down') || '#ef4444',
            markerUpOutline: this.getCssVar('--chart-marker-up-outline') || '#065f46',
            markerDownOutline: this.getCssVar('--chart-marker-down-outline') || '#991b1b',
            markerSymbol: this.getCssVar('--chart-marker-symbol') || 'triangle',
            markerSize: parseInt(this.getCssVar('--chart-marker-size')) || 22,
            // Benchmark overlay colors (Okabe-Ito defaults, theme-overridable)
            seriesPrimary: this.getCssVar('--chart-series-primary') || '#0072B2',
            seriesComparison: this.getCssVar('--chart-series-comparison') || '#E69F00',
            benchmark1: this.getCssVar('--chart-benchmark-1') || '#009E73',
            benchmark2: this.getCssVar('--chart-benchmark-2') || '#56B4E9',
            benchmark3: this.getCssVar('--chart-benchmark-3') || '#D55E00',
            benchmark4: this.getCssVar('--chart-benchmark-4') || '#CC79A7',
            benchmark5: this.getCssVar('--chart-benchmark-5') || '#F0E442'
        };
    },

    /**
     * Get the theme-aware Okabe-Ito color palette for chart series.
     * Returns array of {color, dash} matching SERIES_PALETTE positions.
     */
    getSeriesPalette() {
        const tc = this.getThemeColors();
        return [
            { color: tc.seriesPrimary, dash: 'solid' },
            { color: tc.seriesComparison, dash: 'dash' },
            { color: tc.benchmark1, dash: 'dot' },
            { color: tc.benchmark2, dash: 'dashdot' },
            { color: tc.benchmark3, dash: 'longdash' },
            { color: tc.benchmark4, dash: 'solid' },
            { color: tc.benchmark5, dash: 'dash' }
        ];
    },

    /**
     * Normalize data to percentage change from period start
     * @param {Array} data - Array of OHLCV data points
     * @returns {Array} Array of {date, value} with percentage change
     */
    normalizeToPercentChange(data) {
        if (!data || data.length === 0) return [];
        const baseValue = data[0].close;
        if (baseValue === 0) return data.map(d => ({ date: d.date, value: 0 }));
        return data.map(d => ({
            date: d.date,
            value: ((d.close - baseValue) / baseValue) * 100
        }));
    },

    /**
     * Calculate subplot domains based on enabled indicators
     * @param {boolean} showRsi - Whether RSI panel is shown
     * @param {boolean} showMacd - Whether MACD panel is shown
     * @param {boolean} showStochastic - Whether Stochastic panel is shown
     * @returns {Object} Domain ranges for each subplot
     */
    calculateSubplotDomains(showRsi, showMacd, showStochastic) {
        const indicatorCount = [showRsi, showMacd, showStochastic].filter(Boolean).length;

        // No indicators: full chart for price
        if (indicatorCount === 0) {
            return {
                priceDomain: [0, 1],
                rsiDomain: null,
                macdDomain: null,
                stochasticDomain: null
            };
        }

        // One indicator: 68% price, 28% indicator
        if (indicatorCount === 1) {
            const indicatorDomain = [0, 0.28];
            return {
                priceDomain: [0.32, 1],
                rsiDomain: showRsi ? indicatorDomain : null,
                macdDomain: showMacd ? indicatorDomain : null,
                stochasticDomain: showStochastic ? indicatorDomain : null
            };
        }

        // Two indicators: 50% price, 22% each indicator
        if (indicatorCount === 2) {
            const activeIndicators = [];
            if (showRsi) activeIndicators.push('rsi');
            if (showMacd) activeIndicators.push('macd');
            if (showStochastic) activeIndicators.push('stochastic');

            return {
                priceDomain: [0.50, 1],
                rsiDomain: activeIndicators[0] === 'rsi' ? [0.25, 0.46] : (activeIndicators[1] === 'rsi' ? [0, 0.21] : null),
                macdDomain: activeIndicators[0] === 'macd' ? [0.25, 0.46] : (activeIndicators[1] === 'macd' ? [0, 0.21] : null),
                stochasticDomain: activeIndicators[0] === 'stochastic' ? [0.25, 0.46] : (activeIndicators[1] === 'stochastic' ? [0, 0.21] : null)
            };
        }

        // Three indicators: 45% price, 17% each indicator
        return {
            priceDomain: [0.55, 1],
            rsiDomain: [0.37, 0.52],
            macdDomain: [0.19, 0.34],
            stochasticDomain: [0, 0.16]
        };
    },

    /**
     * Render stock chart with OHLC data and technical indicators
     * @param {string} elementId - DOM element ID
     * @param {Object} historyData - Historical data from API
     * @param {Object} analysisData - Analysis data with moving averages, RSI, MACD
     * @param {Object} options - Chart options
     */
    renderStockChart(elementId, historyData, analysisData, options = {}) {
        const {
            chartType = 'candlestick',
            showMa20 = true,
            showMa50 = true,
            showMa200 = false,
            significantMoves = null,
            showMarkers = true,
            showRsi = false,
            showMacd = false,
            showBollinger = false,
            showStochastic = false,
            comparisonData = null,
            comparisonTicker = null
        } = options;

        const data = historyData.data;
        const themeColors = this.getThemeColors();

        // Check if we're in comparison mode
        const isComparing = comparisonData && comparisonTicker && comparisonData.data;

        const traces = [];

        // COMPARISON MODE: Show normalized percentage change for both stocks
        if (isComparing) {
            // Normalize primary stock to % change
            const primaryNormalized = this.normalizeToPercentChange(data);
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: primaryNormalized.map(d => d.date),
                y: primaryNormalized.map(d => d.value),
                name: historyData.symbol,
                line: { color: themeColors.linePrimary, width: 2 },
                yaxis: 'y'
            });

            // Normalize comparison stock to % change
            const comparisonNormalized = this.normalizeToPercentChange(comparisonData.data);
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: comparisonNormalized.map(d => d.date),
                y: comparisonNormalized.map(d => d.value),
                name: comparisonTicker,
                line: { color: themeColors.lineSecondary, width: 2, dash: 'dash' },
                yaxis: 'y'
            });

            // Add zero reference line
            const allDates = primaryNormalized.map(d => d.date);
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: [allDates[0], allDates[allDates.length - 1]],
                y: [0, 0],
                name: 'Baseline',
                line: { color: themeColors.gridColor, width: 1, dash: 'dot' },
                yaxis: 'y',
                showlegend: false,
                hoverinfo: 'skip'
            });

            // Build comparison layout
            const layout = {
                title: {
                    text: `${historyData.symbol} vs ${comparisonTicker} - ${this.formatPeriodLabel(historyData)}`,
                    font: { size: 18, color: themeColors.text }
                },
                xaxis: {
                    rangeslider: { visible: false },
                    gridcolor: themeColors.gridColor,
                    tickfont: { color: themeColors.axisColor },
                    linecolor: themeColors.gridColor
                },
                yaxis: {
                    title: { text: '% Change', font: { color: themeColors.axisColor, size: 11 } },
                    gridcolor: themeColors.gridColor,
                    tickfont: { color: themeColors.axisColor },
                    linecolor: themeColors.gridColor,
                    ticksuffix: '%',
                    domain: [0, 1]
                },
                plot_bgcolor: themeColors.background,
                paper_bgcolor: themeColors.paper,
                showlegend: true,
                legend: {
                    orientation: 'h',
                    yanchor: 'top',
                    y: -0.08,
                    xanchor: 'center',
                    x: 0.5,
                    font: { color: themeColors.axisColor, size: 10 }
                },
                autosize: true,
                margin: { t: 50, r: 30, b: 60, l: 60, autoexpand: true },
                hovermode: 'x unified',
                dragmode: false
            };

            const config = {
                responsive: true,
                displayModeBar: true,
                modeBarButtonsToRemove: ['pan2d', 'lasso2d', 'select2d']
            };

            this._smartPlot(elementId, traces, layout, config).then(() => {
                this._attachDynamicTitle(elementId, historyData.symbol, comparisonTicker);
            });

            return; // Exit early for comparison mode
        }

        // SINGLE STOCK MODE: Normal rendering with candlestick/line and indicators
        const dates = data.map(d => d.date);
        const opens = data.map(d => d.open);
        const highs = data.map(d => d.high);
        const lows = data.map(d => d.low);
        const closes = data.map(d => d.close);

        const domains = this.calculateSubplotDomains(showRsi, showMacd, showStochastic);

        // Main price chart (yaxis = y)
        if (chartType === 'candlestick') {
            traces.push({
                type: 'candlestick',
                x: dates,
                open: opens,
                high: highs,
                low: lows,
                close: closes,
                name: historyData.symbol,
                increasing: { line: { color: themeColors.candleUp } },
                decreasing: { line: { color: themeColors.candleDown } },
                yaxis: 'y'
            });
        } else {
            // Add glow trace behind main line if theme supports it
            if (themeColors.glowEnabled && themeColors.glowWidth > 0) {
                traces.push({
                    type: 'scatter',
                    mode: 'lines',
                    x: dates,
                    y: closes,
                    name: historyData.symbol + ' (glow)',
                    line: { color: themeColors.glowColor, width: themeColors.glowWidth },
                    yaxis: 'y',
                    showlegend: false,
                    hoverinfo: 'skip'
                });
            }
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: dates,
                y: closes,
                name: historyData.symbol,
                line: { color: themeColors.linePrimary, width: 2 },
                yaxis: 'y'
            });
        }

        // Moving averages (on price chart, yaxis = y)
        if (analysisData && analysisData.movingAverages) {
            const maData = analysisData.movingAverages;
            const maDates = maData.map(d => d.date);

            if (showMa20) {
                const ma20 = maData.map(d => d.sma20).filter(v => v != null);
                const ma20Dates = maDates.slice(maDates.length - ma20.length);
                traces.push({
                    type: 'scatter',
                    mode: 'lines',
                    x: ma20Dates,
                    y: ma20,
                    name: 'SMA 20',
                    line: { color: themeColors.sma20, width: 1, dash: 'dot' },
                    yaxis: 'y'
                });
            }

            if (showMa50) {
                const ma50 = maData.map(d => d.sma50).filter(v => v != null);
                const ma50Dates = maDates.slice(maDates.length - ma50.length);
                traces.push({
                    type: 'scatter',
                    mode: 'lines',
                    x: ma50Dates,
                    y: ma50,
                    name: 'SMA 50',
                    line: { color: themeColors.sma50, width: 1, dash: 'dot' },
                    yaxis: 'y'
                });
            }

            if (showMa200) {
                const ma200 = maData.map(d => d.sma200).filter(v => v != null);
                const ma200Dates = maDates.slice(maDates.length - ma200.length);
                traces.push({
                    type: 'scatter',
                    mode: 'lines',
                    x: ma200Dates,
                    y: ma200,
                    name: 'SMA 200',
                    line: { color: themeColors.sma200, width: 1, dash: 'dot' },
                    yaxis: 'y'
                });
            }
        }

        // Bollinger Bands (on price chart, yaxis = y)
        if (showBollinger && analysisData && analysisData.bollingerBands) {
            const bbData = analysisData.bollingerBands;
            const bbDates = bbData.filter(d => d.upperBand != null).map(d => d.date);
            const upperBand = bbData.filter(d => d.upperBand != null).map(d => d.upperBand);
            const middleBand = bbData.filter(d => d.middleBand != null).map(d => d.middleBand);
            const lowerBand = bbData.filter(d => d.lowerBand != null).map(d => d.lowerBand);

            // Upper band
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: bbDates,
                y: upperBand,
                name: 'BB Upper',
                line: { color: themeColors.bollinger, width: 1 },
                yaxis: 'y'
            });

            // Middle band (SMA 20)
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: bbDates,
                y: middleBand,
                name: 'BB Middle',
                line: { color: themeColors.bollinger, width: 1, dash: 'dash' },
                yaxis: 'y'
            });

            // Lower band
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: bbDates,
                y: lowerBand,
                name: 'BB Lower',
                line: { color: themeColors.bollinger, width: 1 },
                fill: 'tonexty',
                fillcolor: themeColors.bollinger + '1a', // Add transparency
                yaxis: 'y'
            });
        }

        // Significant move markers (on price chart, yaxis = y)
        if (showMarkers && significantMoves && significantMoves.moves && significantMoves.moves.length > 0) {
            const moves = significantMoves.moves;
            const threshold = significantMoves.threshold || 5;

            const upMoves = moves.filter(m => m.isPositive);
            const downMoves = moves.filter(m => !m.isPositive);

            if (upMoves.length > 0) {
                const upY = upMoves.map(m => {
                    const dateStr = m.date.split('T')[0];
                    const dataPoint = data.find(d => d.date.startsWith(dateStr));
                    return dataPoint ? dataPoint.high * 1.02 : m.closePrice * 1.02;
                });

                traces.push({
                    type: 'scatter',
                    mode: 'markers',
                    x: upMoves.map(m => m.date.split('T')[0]),
                    y: upY,
                    name: `+${threshold}% Move`,
                    marker: {
                        color: themeColors.markerUp,
                        size: themeColors.markerSize,
                        symbol: themeColors.markerSymbol + '-up',
                        line: { color: themeColors.markerUpOutline, width: 2 }
                    },
                    customdata: upMoves,
                    hoverinfo: 'text',
                    hovertext: upMoves.map(m => `+${m.percentChange.toFixed(1)}%`),
                    showlegend: true,
                    yaxis: 'y'
                });
            }

            if (downMoves.length > 0) {
                const downY = downMoves.map(m => {
                    const dateStr = m.date.split('T')[0];
                    const dataPoint = data.find(d => d.date.startsWith(dateStr));
                    return dataPoint ? dataPoint.low * 0.98 : m.closePrice * 0.98;
                });

                traces.push({
                    type: 'scatter',
                    mode: 'markers',
                    x: downMoves.map(m => m.date.split('T')[0]),
                    y: downY,
                    name: `-${threshold}% Move`,
                    marker: {
                        color: themeColors.markerDown,
                        size: themeColors.markerSize,
                        symbol: themeColors.markerSymbol + '-down',
                        line: { color: themeColors.markerDownOutline, width: 2 }
                    },
                    customdata: downMoves,
                    hoverinfo: 'text',
                    hovertext: downMoves.map(m => `${m.percentChange.toFixed(1)}%`),
                    showlegend: true,
                    yaxis: 'y'
                });
            }
        }

        // RSI indicator (yaxis2)
        if (showRsi && analysisData && analysisData.rsi) {
            const rsiData = analysisData.rsi;
            const rsiDates = rsiData.map(d => d.date);
            const rsiValues = rsiData.map(d => d.rsi);

            // RSI line
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: rsiDates,
                y: rsiValues,
                name: 'RSI (14)',
                line: { color: themeColors.rsi, width: 1.5 },
                yaxis: 'y2'
            });

            // Overbought line (70)
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: [rsiDates[0], rsiDates[rsiDates.length - 1]],
                y: [70, 70],
                name: 'Overbought',
                line: { color: themeColors.overbought, width: 1, dash: 'dot' },
                yaxis: 'y2',
                showlegend: false,
                hoverinfo: 'skip'
            });

            // Oversold line (30)
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: [rsiDates[0], rsiDates[rsiDates.length - 1]],
                y: [30, 30],
                name: 'Oversold',
                line: { color: themeColors.oversold, width: 1, dash: 'dot' },
                yaxis: 'y2',
                showlegend: false,
                hoverinfo: 'skip'
            });
        }

        // MACD indicator (yaxis3)
        if (showMacd && analysisData && analysisData.macd) {
            const macdData = analysisData.macd;
            const macdDates = macdData.map(d => d.date);

            // MACD Line
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: macdDates,
                y: macdData.map(d => d.macdLine),
                name: 'MACD',
                line: { color: themeColors.macd, width: 1.5 },
                yaxis: 'y3'
            });

            // Signal Line
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: macdDates,
                y: macdData.map(d => d.signalLine),
                name: 'Signal',
                line: { color: themeColors.macdSignal, width: 1.5 },
                yaxis: 'y3'
            });

            // Histogram (bar chart) - use theme colors with transparency
            const histogramColors = macdData.map(d =>
                (d.histogram || 0) >= 0 ? themeColors.candleUp + 'b3' : themeColors.candleDown + 'b3'
            );
            traces.push({
                type: 'bar',
                x: macdDates,
                y: macdData.map(d => d.histogram),
                name: 'Histogram',
                marker: { color: histogramColors },
                yaxis: 'y3',
                showlegend: false
            });
        }

        // Stochastic Oscillator (yaxis4)
        if (showStochastic && analysisData && analysisData.stochastic) {
            const stochData = analysisData.stochastic;
            const stochDates = stochData.map(d => d.date);

            // %K Line (fast stochastic)
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: stochDates,
                y: stochData.map(d => d.k),
                name: '%K',
                line: { color: themeColors.stochastic, width: 1.5 },
                yaxis: 'y4'
            });

            // %D Line (signal line)
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: stochDates,
                y: stochData.map(d => d.d),
                name: '%D',
                line: { color: themeColors.stochasticD, width: 1.5, dash: 'dash' },
                yaxis: 'y4'
            });

            // Overbought line (80)
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: [stochDates[0], stochDates[stochDates.length - 1]],
                y: [80, 80],
                name: 'Overbought',
                line: { color: themeColors.overbought, width: 1, dash: 'dot' },
                yaxis: 'y4',
                showlegend: false,
                hoverinfo: 'skip'
            });

            // Oversold line (20)
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: [stochDates[0], stochDates[stochDates.length - 1]],
                y: [20, 20],
                name: 'Oversold',
                line: { color: themeColors.oversold, width: 1, dash: 'dot' },
                yaxis: 'y4',
                showlegend: false,
                hoverinfo: 'skip'
            });
        }

        // Build layout
        const layout = {
            title: {
                text: `${historyData.symbol} - ${this.formatPeriodLabel(historyData)}`,
                font: { size: 18, color: themeColors.text }
            },
            xaxis: {
                rangeslider: { visible: false },
                gridcolor: themeColors.gridColor,
                tickfont: { color: themeColors.axisColor },
                linecolor: themeColors.gridColor
            },
            yaxis: {
                title: { text: 'Price ($)', font: { color: themeColors.axisColor, size: 11 } },
                gridcolor: themeColors.gridColor,
                tickfont: { color: themeColors.axisColor },
                linecolor: themeColors.gridColor,
                domain: domains.priceDomain
            },
            plot_bgcolor: themeColors.background,
            paper_bgcolor: themeColors.paper,
            showlegend: true,
            legend: {
                orientation: 'h',
                yanchor: 'top',
                y: -0.08,
                xanchor: 'center',
                x: 0.5,
                font: { color: themeColors.axisColor, size: 10 }
            },
            autosize: true,
            margin: { t: 50, r: 30, b: 60, l: 60, autoexpand: true },
            hovermode: 'closest',
            hoverdistance: 20,
            dragmode: false
        };

        // Add RSI y-axis if shown
        if (showRsi && domains.rsiDomain) {
            layout.yaxis2 = {
                title: { text: 'RSI', font: { color: themeColors.axisColor, size: 10 } },
                gridcolor: themeColors.gridColor,
                tickfont: { color: themeColors.axisColor, size: 9 },
                linecolor: themeColors.gridColor,
                domain: domains.rsiDomain,
                range: [0, 100],
                dtick: 20,
                anchor: 'x'
            };
        }

        // Add MACD y-axis if shown
        if (showMacd && domains.macdDomain) {
            layout.yaxis3 = {
                title: { text: 'MACD', font: { color: themeColors.axisColor, size: 10 } },
                gridcolor: themeColors.gridColor,
                tickfont: { color: themeColors.axisColor, size: 9 },
                linecolor: themeColors.gridColor,
                domain: domains.macdDomain,
                anchor: 'x'
            };
        }

        // Add Stochastic y-axis if shown
        if (showStochastic && domains.stochasticDomain) {
            layout.yaxis4 = {
                title: { text: 'Stoch', font: { color: themeColors.axisColor, size: 10 } },
                gridcolor: themeColors.gridColor,
                tickfont: { color: themeColors.axisColor, size: 9 },
                linecolor: themeColors.gridColor,
                domain: domains.stochasticDomain,
                range: [0, 100],
                dtick: 20,
                anchor: 'x'
            };
        }

        const config = {
            responsive: true,
            displayModeBar: true,
            modeBarButtonsToRemove: ['pan2d', 'lasso2d', 'select2d']
        };

        this._smartPlot(elementId, traces, layout, config).then(() => {
            this._attachDynamicTitle(elementId, historyData.symbol, null);
        });
    },

    /**
     * Update chart with new options
     */
    updateChart(elementId, historyData, analysisData, options) {
        this.renderStockChart(elementId, historyData, analysisData, options);
    }
};
