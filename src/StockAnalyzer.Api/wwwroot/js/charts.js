/**
 * Stock Chart Configuration
 * Handles Plotly.js chart rendering with technical indicators
 */
const Charts = {
    /**
     * Check if dark mode is currently enabled
     */
    isDarkMode() {
        return document.documentElement.classList.contains('dark');
    },

    /**
     * Get theme-aware colors
     */
    getThemeColors() {
        const isDark = this.isDarkMode();
        return {
            background: isDark ? '#1F2937' : '#FFFFFF',
            paper: isDark ? '#1F2937' : '#FFFFFF',
            text: isDark ? '#F9FAFB' : '#1F2937',
            gridColor: isDark ? '#374151' : '#E5E7EB',
            axisColor: isDark ? '#9CA3AF' : '#6B7280'
        };
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
     * @returns {Object} Domain ranges for each subplot
     */
    calculateSubplotDomains(showRsi, showMacd) {
        // Gap between subplots
        const gap = 0.03;

        // No indicators: full chart for price
        if (!showRsi && !showMacd) {
            return {
                priceDomain: [0, 1],
                rsiDomain: null,
                macdDomain: null
            };
        }

        // One indicator: 68% price, 28% indicator (with gaps)
        if (showRsi && !showMacd) {
            return {
                priceDomain: [0.32, 1],
                rsiDomain: [0, 0.28],
                macdDomain: null
            };
        }

        if (!showRsi && showMacd) {
            return {
                priceDomain: [0.32, 1],
                rsiDomain: null,
                macdDomain: [0, 0.28]
            };
        }

        // Both indicators: 50% price, 22% each indicator (with gaps)
        return {
            priceDomain: [0.50, 1],
            rsiDomain: [0.25, 0.46],
            macdDomain: [0, 0.21]
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
                line: { color: '#3B82F6', width: 2 },
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
                line: { color: '#F59E0B', width: 2, dash: 'dash' },
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
                    text: `${historyData.symbol} vs ${comparisonTicker} - ${historyData.period.toUpperCase()}`,
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
                hovermode: 'x unified'
            };

            const config = {
                responsive: true,
                displayModeBar: true,
                modeBarButtonsToRemove: ['pan2d', 'lasso2d', 'select2d']
            };

            Plotly.newPlot(elementId, traces, layout, config).then(() => {
                const chartEl = document.getElementById(elementId);
                if (chartEl) {
                    Plotly.Plots.resize(chartEl);
                }
            });

            return; // Exit early for comparison mode
        }

        // SINGLE STOCK MODE: Normal rendering with candlestick/line and indicators
        const dates = data.map(d => d.date);
        const opens = data.map(d => d.open);
        const highs = data.map(d => d.high);
        const lows = data.map(d => d.low);
        const closes = data.map(d => d.close);

        const domains = this.calculateSubplotDomains(showRsi, showMacd);

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
                increasing: { line: { color: '#10B981' } },
                decreasing: { line: { color: '#EF4444' } },
                yaxis: 'y'
            });
        } else {
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: dates,
                y: closes,
                name: historyData.symbol,
                line: { color: '#3B82F6', width: 2 },
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
                    line: { color: '#F59E0B', width: 1, dash: 'dot' },
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
                    line: { color: '#8B5CF6', width: 1, dash: 'dot' },
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
                    line: { color: '#EC4899', width: 1, dash: 'dot' },
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
                line: { color: '#6366F1', width: 1 },
                yaxis: 'y'
            });

            // Middle band (SMA 20)
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: bbDates,
                y: middleBand,
                name: 'BB Middle',
                line: { color: '#6366F1', width: 1, dash: 'dash' },
                yaxis: 'y'
            });

            // Lower band
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: bbDates,
                y: lowerBand,
                name: 'BB Lower',
                line: { color: '#6366F1', width: 1 },
                fill: 'tonexty',
                fillcolor: 'rgba(99, 102, 241, 0.1)',
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
                        color: '#10B981',
                        size: 22,
                        symbol: 'triangle-up',
                        line: { color: '#065F46', width: 2 }
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
                        color: '#EF4444',
                        size: 22,
                        symbol: 'triangle-down',
                        line: { color: '#991B1B', width: 2 }
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
                line: { color: '#8B5CF6', width: 1.5 },
                yaxis: 'y2'
            });

            // Overbought line (70)
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: [rsiDates[0], rsiDates[rsiDates.length - 1]],
                y: [70, 70],
                name: 'Overbought',
                line: { color: '#EF4444', width: 1, dash: 'dot' },
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
                line: { color: '#10B981', width: 1, dash: 'dot' },
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
                line: { color: '#3B82F6', width: 1.5 },
                yaxis: 'y3'
            });

            // Signal Line
            traces.push({
                type: 'scatter',
                mode: 'lines',
                x: macdDates,
                y: macdData.map(d => d.signalLine),
                name: 'Signal',
                line: { color: '#F59E0B', width: 1.5 },
                yaxis: 'y3'
            });

            // Histogram (bar chart)
            const histogramColors = macdData.map(d =>
                (d.histogram || 0) >= 0 ? 'rgba(16, 185, 129, 0.7)' : 'rgba(239, 68, 68, 0.7)'
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

        // Build layout
        const layout = {
            title: {
                text: `${historyData.symbol} - ${historyData.period.toUpperCase()}`,
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
            hoverdistance: 20
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

        const config = {
            responsive: true,
            displayModeBar: true,
            modeBarButtonsToRemove: ['pan2d', 'lasso2d', 'select2d']
        };

        Plotly.newPlot(elementId, traces, layout, config).then(() => {
            const chartEl = document.getElementById(elementId);
            if (chartEl) {
                Plotly.Plots.resize(chartEl);
            }
        });
    },

    /**
     * Update chart with new options
     */
    updateChart(elementId, historyData, analysisData, options) {
        this.renderStockChart(elementId, historyData, analysisData, options);
    }
};
