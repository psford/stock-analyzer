/**
 * Unit tests for chartSeries management functions
 * Tests the core series management logic for the chart benchmarks feature
 */

// Constants (mirrored from app.js for testing)
const SERIES_PALETTE = [
    { color: '#0072B2', dash: 'solid' },
    { color: '#E69F00', dash: 'dash' },
    { color: '#009E73', dash: 'dot' },
    { color: '#56B4E9', dash: 'dashdot' },
    { color: '#D55E00', dash: 'longdash' },
    { color: '#CC79A7', dash: 'solid' },
    { color: '#F0E442', dash: 'dash' },
];
const MAX_BENCHMARKS = 5;

// Pure functions for testing (no DOM, no App object dependencies)
function buildPrimarySeries(currentTicker, historyData) {
    if (!historyData || !currentTicker) return null;
    return {
        ticker: currentTicker,
        label: currentTicker,
        type: 'primary',
        data: historyData,
        color: SERIES_PALETTE[0].color,
        dash: SERIES_PALETTE[0].dash
    };
}

function assignPalettePositions(chartSeries) {
    chartSeries.forEach((series, i) => {
        if (i < SERIES_PALETTE.length) {
            series.color = SERIES_PALETTE[i].color;
            series.dash = SERIES_PALETTE[i].dash;
        }
    });
}

function addSeries(chartSeries, ticker, label, type, data) {
    // Prevent duplicates
    if (chartSeries.some(s => s.ticker === ticker)) {
        return { success: false, reason: 'duplicate' };
    }
    // Enforce max benchmarks
    const benchmarkCount = chartSeries.filter(s => s.type === 'benchmark').length;
    if (type === 'benchmark' && benchmarkCount >= MAX_BENCHMARKS) {
        return { success: false, reason: 'max_benchmarks' };
    }
    // Only one comparison allowed
    if (type === 'comparison') {
        const newSeries = chartSeries.filter(s => s.type !== 'comparison');
        chartSeries.splice(0, chartSeries.length, ...newSeries);
    }
    const idx = chartSeries.length;
    const palette = idx < SERIES_PALETTE.length ? SERIES_PALETTE[idx] : SERIES_PALETTE[SERIES_PALETTE.length - 1];
    chartSeries.push({
        ticker,
        label,
        type,
        data,
        color: palette.color,
        dash: palette.dash
    });
    assignPalettePositions(chartSeries);
    return { success: true };
}

function removeSeries(chartSeries, ticker) {
    const idx = chartSeries.findIndex(s => s.ticker === ticker);
    if (idx <= 0) return null; // Can't remove primary (index 0) or not found
    const removed = chartSeries.splice(idx, 1)[0];
    assignPalettePositions(chartSeries);
    return removed;
}

function getSeriesByType(chartSeries, type) {
    return chartSeries.filter(s => s.type === type);
}

function clearBenchmarkSeries(chartSeries) {
    const newSeries = chartSeries.filter(s => s.type !== 'benchmark');
    chartSeries.splice(0, chartSeries.length, ...newSeries);
    assignPalettePositions(chartSeries);
}

function clearAllSeries(chartSeries) {
    const newSeries = chartSeries.filter(s => s.type === 'primary');
    chartSeries.splice(0, chartSeries.length, ...newSeries);
}

function isMultiSeriesMode(chartSeries) {
    return chartSeries.length > 1;
}

// ============================================
// Tests
// ============================================

describe('chartSeries management functions', () => {
    // Helper: Create test data
    function createMockHistoryData() {
        return [
            { date: '2024-01-01', open: 100, close: 101, high: 102, low: 99, volume: 1000000 },
            { date: '2024-01-02', open: 101, close: 103, high: 104, low: 100, volume: 1100000 }
        ];
    }

    describe('buildPrimarySeries', () => {
        test('returns null when no historyData', () => {
            const series = buildPrimarySeries('MSFT', null);
            expect(series).toBeNull();
        });

        test('returns null when no currentTicker', () => {
            const series = buildPrimarySeries(null, createMockHistoryData());
            expect(series).toBeNull();
        });

        test('builds primary series with correct structure', () => {
            const historyData = createMockHistoryData();
            const series = buildPrimarySeries('MSFT', historyData);

            expect(series).not.toBeNull();
            expect(series.ticker).toBe('MSFT');
            expect(series.label).toBe('MSFT');
            expect(series.type).toBe('primary');
            expect(series.data).toBe(historyData);
            expect(series.color).toBe(SERIES_PALETTE[0].color);
            expect(series.dash).toBe(SERIES_PALETTE[0].dash);
        });

        test('assigns primary palette (blue, solid)', () => {
            const series = buildPrimarySeries('MSFT', createMockHistoryData());
            expect(series.color).toBe('#0072B2');
            expect(series.dash).toBe('solid');
        });
    });

    describe('assignPalettePositions', () => {
        test('assigns correct colors and dashes by position', () => {
            const chartSeries = [
                { ticker: 'MSFT', type: 'primary', color: '', dash: '' },
                { ticker: 'SPY', type: 'comparison', color: '', dash: '' },
                { ticker: 'QQQ', type: 'benchmark', color: '', dash: '' }
            ];

            assignPalettePositions(chartSeries);

            expect(chartSeries[0].color).toBe('#0072B2');
            expect(chartSeries[0].dash).toBe('solid');
            expect(chartSeries[1].color).toBe('#E69F00');
            expect(chartSeries[1].dash).toBe('dash');
            expect(chartSeries[2].color).toBe('#009E73');
            expect(chartSeries[2].dash).toBe('dot');
        });

        test('handles more than 7 series', () => {
            const chartSeries = Array.from({ length: 9 }, (_, i) => ({
                ticker: `TICK${i}`,
                type: i === 0 ? 'primary' : 'benchmark',
                color: 'initial',
                dash: 'initial'
            }));

            assignPalettePositions(chartSeries);

            // All series within palette length get assigned colors
            expect(chartSeries[0].color).toBe(SERIES_PALETTE[0].color);
            expect(chartSeries[6].color).toBe(SERIES_PALETTE[6].color);
            // Series beyond palette length do not get modified (no palette entry exists)
            expect(chartSeries[7].color).toBe('initial');
            expect(chartSeries[8].color).toBe('initial');
        });
    });

    describe('addSeries', () => {
        test('adds a new series successfully', () => {
            const chartSeries = [];
            const result = addSeries(chartSeries, 'SPY', 'SPY', 'comparison', createMockHistoryData());

            expect(result.success).toBe(true);
            expect(chartSeries.length).toBe(1);
            expect(chartSeries[0].ticker).toBe('SPY');
            expect(chartSeries[0].type).toBe('comparison');
        });

        test('prevents duplicate tickers', () => {
            const chartSeries = [
                buildPrimarySeries('MSFT', createMockHistoryData())
            ];
            const result = addSeries(chartSeries, 'MSFT', 'MSFT', 'comparison', createMockHistoryData());

            expect(result.success).toBe(false);
            expect(result.reason).toBe('duplicate');
            expect(chartSeries.length).toBe(1);
        });

        test('replaces existing comparison with new one', () => {
            const historyData = createMockHistoryData();
            const chartSeries = [
                buildPrimarySeries('MSFT', historyData),
                { ticker: 'SPY', label: 'SPY', type: 'comparison', data: historyData, color: '', dash: '' }
            ];

            const result = addSeries(chartSeries, 'QQQ', 'QQQ', 'comparison', historyData);

            expect(result.success).toBe(true);
            expect(chartSeries.length).toBe(2);
            expect(chartSeries[1].ticker).toBe('QQQ');
            const hasOldComparison = chartSeries.some(s => s.ticker === 'SPY');
            expect(hasOldComparison).toBe(false);
        });

        test('enforces max benchmarks limit', () => {
            const historyData = createMockHistoryData();
            const chartSeries = [
                buildPrimarySeries('MSFT', historyData)
            ];

            // Add 5 benchmarks (max)
            for (let i = 0; i < 5; i++) {
                addSeries(chartSeries, `BM${i}`, `BM${i}`, 'benchmark', historyData);
            }
            expect(chartSeries.length).toBe(6); // primary + 5 benchmarks

            // Try to add 6th benchmark
            const result = addSeries(chartSeries, 'BM5', 'BM5', 'benchmark', historyData);
            expect(result.success).toBe(false);
            expect(result.reason).toBe('max_benchmarks');
            expect(chartSeries.length).toBe(6); // Unchanged
        });

        test('assigns palette colors by position', () => {
            const historyData = createMockHistoryData();
            const chartSeries = [buildPrimarySeries('MSFT', historyData)];

            addSeries(chartSeries, 'SPY', 'SPY', 'comparison', historyData);
            addSeries(chartSeries, 'QQQ', 'QQQ', 'benchmark', historyData);

            expect(chartSeries[0].color).toBe('#0072B2');
            expect(chartSeries[1].color).toBe('#E69F00');
            expect(chartSeries[2].color).toBe('#009E73');
        });
    });

    describe('removeSeries', () => {
        test('cannot remove primary series (index 0)', () => {
            const historyData = createMockHistoryData();
            const chartSeries = [buildPrimarySeries('MSFT', historyData)];

            const removed = removeSeries(chartSeries, 'MSFT');

            expect(removed).toBeNull();
            expect(chartSeries.length).toBe(1);
        });

        test('removes non-primary series', () => {
            const historyData = createMockHistoryData();
            const chartSeries = [
                buildPrimarySeries('MSFT', historyData),
                { ticker: 'SPY', label: 'SPY', type: 'comparison', data: historyData, color: '', dash: '' }
            ];

            const removed = removeSeries(chartSeries, 'SPY');

            expect(removed).not.toBeNull();
            expect(removed.ticker).toBe('SPY');
            expect(chartSeries.length).toBe(1);
        });

        test('returns null when ticker not found', () => {
            const historyData = createMockHistoryData();
            const chartSeries = [buildPrimarySeries('MSFT', historyData)];

            const removed = removeSeries(chartSeries, 'NOTFOUND');

            expect(removed).toBeNull();
            expect(chartSeries.length).toBe(1);
        });

        test('reassigns palette positions after removal', () => {
            const historyData = createMockHistoryData();
            const chartSeries = [
                buildPrimarySeries('MSFT', historyData),
                { ticker: 'SPY', label: 'SPY', type: 'comparison', data: historyData, color: '', dash: '' },
                { ticker: 'QQQ', label: 'QQQ', type: 'benchmark', data: historyData, color: '', dash: '' }
            ];
            assignPalettePositions(chartSeries);
            const origQQQColor = chartSeries[2].color;

            removeSeries(chartSeries, 'SPY');

            // QQQ should now be at index 1 and get the comparison color
            expect(chartSeries[1].ticker).toBe('QQQ');
            expect(chartSeries[1].color).toBe('#E69F00');
            expect(chartSeries[1].color).not.toBe(origQQQColor);
        });
    });

    describe('getSeriesByType', () => {
        test('returns all series of a given type', () => {
            const historyData = createMockHistoryData();
            const chartSeries = [
                buildPrimarySeries('MSFT', historyData),
                { ticker: 'BM1', label: 'BM1', type: 'benchmark', data: historyData, color: '', dash: '' },
                { ticker: 'BM2', label: 'BM2', type: 'benchmark', data: historyData, color: '', dash: '' }
            ];

            const benchmarks = getSeriesByType(chartSeries, 'benchmark');

            expect(benchmarks.length).toBe(2);
            expect(benchmarks[0].ticker).toBe('BM1');
            expect(benchmarks[1].ticker).toBe('BM2');
        });

        test('returns empty array when no series of type found', () => {
            const historyData = createMockHistoryData();
            const chartSeries = [buildPrimarySeries('MSFT', historyData)];

            const comparisons = getSeriesByType(chartSeries, 'comparison');

            expect(comparisons.length).toBe(0);
        });
    });

    describe('clearBenchmarkSeries', () => {
        test('removes all benchmark series, keeps primary and comparison', () => {
            const historyData = createMockHistoryData();
            const chartSeries = [
                buildPrimarySeries('MSFT', historyData),
                { ticker: 'SPY', label: 'SPY', type: 'comparison', data: historyData, color: '', dash: '' },
                { ticker: 'BM1', label: 'BM1', type: 'benchmark', data: historyData, color: '', dash: '' },
                { ticker: 'BM2', label: 'BM2', type: 'benchmark', data: historyData, color: '', dash: '' }
            ];

            clearBenchmarkSeries(chartSeries);

            expect(chartSeries.length).toBe(2);
            expect(chartSeries[0].ticker).toBe('MSFT');
            expect(chartSeries[1].ticker).toBe('SPY');
        });

        test('does nothing if no benchmarks present', () => {
            const historyData = createMockHistoryData();
            const chartSeries = [buildPrimarySeries('MSFT', historyData)];

            clearBenchmarkSeries(chartSeries);

            expect(chartSeries.length).toBe(1);
            expect(chartSeries[0].ticker).toBe('MSFT');
        });
    });

    describe('clearAllSeries', () => {
        test('removes all non-primary series', () => {
            const historyData = createMockHistoryData();
            const chartSeries = [
                buildPrimarySeries('MSFT', historyData),
                { ticker: 'SPY', label: 'SPY', type: 'comparison', data: historyData, color: '', dash: '' },
                { ticker: 'BM1', label: 'BM1', type: 'benchmark', data: historyData, color: '', dash: '' }
            ];

            clearAllSeries(chartSeries);

            expect(chartSeries.length).toBe(1);
            expect(chartSeries[0].ticker).toBe('MSFT');
            expect(chartSeries[0].type).toBe('primary');
        });

        test('leaves empty array if no primary series', () => {
            const historyData = createMockHistoryData();
            const chartSeries = [
                { ticker: 'SPY', label: 'SPY', type: 'comparison', data: historyData, color: '', dash: '' }
            ];

            clearAllSeries(chartSeries);

            expect(chartSeries.length).toBe(0);
        });
    });

    describe('isMultiSeriesMode', () => {
        test('returns true when more than one series', () => {
            const historyData = createMockHistoryData();
            const chartSeries = [
                buildPrimarySeries('MSFT', historyData),
                { ticker: 'SPY', label: 'SPY', type: 'comparison', data: historyData, color: '', dash: '' }
            ];

            expect(isMultiSeriesMode(chartSeries)).toBe(true);
        });

        test('returns false with exactly one series', () => {
            const historyData = createMockHistoryData();
            const chartSeries = [buildPrimarySeries('MSFT', historyData)];

            expect(isMultiSeriesMode(chartSeries)).toBe(false);
        });

        test('returns false with no series', () => {
            const chartSeries = [];

            expect(isMultiSeriesMode(chartSeries)).toBe(false);
        });
    });

    describe('Acceptance Criteria AC1.1: chartSeries structure', () => {
        test('AC1.1: Each series entry has correct structure with all required fields', () => {
            const historyData = createMockHistoryData();
            const series = buildPrimarySeries('MSFT', historyData);

            expect(series).toHaveProperty('ticker');
            expect(series).toHaveProperty('label');
            expect(series).toHaveProperty('type');
            expect(series).toHaveProperty('data');
            expect(series).toHaveProperty('color');
            expect(series).toHaveProperty('dash');

            expect(typeof series.ticker).toBe('string');
            expect(typeof series.label).toBe('string');
            expect(['primary', 'comparison', 'benchmark']).toContain(series.type);
            expect(typeof series.color).toBe('string');
            expect(series.color).toMatch(/^#[0-9A-Fa-f]{6}$/);
            expect(typeof series.dash).toBe('string');
        });
    });

    describe('Acceptance Criteria AC1.F2: Max benchmarks enforcement', () => {
        test('AC1.F2: Adding 6th benchmark is rejected with max 5 enforced', () => {
            const historyData = createMockHistoryData();
            const chartSeries = [buildPrimarySeries('MSFT', historyData)];

            // Add 5 benchmarks successfully
            for (let i = 1; i <= 5; i++) {
                const result = addSeries(chartSeries, `BM${i}`, `BM${i}`, 'benchmark', historyData);
                expect(result.success).toBe(true);
            }

            // Verify we have primary + 5 benchmarks
            const benchmarkCount = getSeriesByType(chartSeries, 'benchmark').length;
            expect(benchmarkCount).toBe(5);

            // Try to add 6th benchmark - should fail
            const result = addSeries(chartSeries, 'BM6', 'BM6', 'benchmark', historyData);
            expect(result.success).toBe(false);
            expect(getSeriesByType(chartSeries, 'benchmark').length).toBe(5);
        });
    });
});

// ============================================
// Indicator State Save/Restore Tests
// ============================================

// Pure functions extracted from disableIndicators() for testability
function saveIndicatorState(currentState) {
    return { ...currentState };
}

function restoreIndicatorState(savedState, defaultState) {
    if (!savedState) return { ...defaultState };
    return { ...savedState };
}

describe('Indicator state save/restore (AC6.2, AC6.F1)', () => {
    const defaultState = {
        'show-rsi': false,
        'show-macd': false,
        'show-bollinger': false,
        'show-stochastic': false,
        'ma-20': true,
        'ma-50': true,
        'ma-200': false,
        'chart-type': 'line',
        'show-markers': false
    };

    test('AC6.2: saveIndicatorState preserves all checkbox values', () => {
        const current = {
            'show-rsi': true,
            'show-macd': false,
            'show-bollinger': true,
            'show-stochastic': false,
            'ma-20': true,
            'ma-50': false,
            'ma-200': true,
            'chart-type': 'candlestick',
            'show-markers': true
        };

        const saved = saveIndicatorState(current);

        expect(saved['show-rsi']).toBe(true);
        expect(saved['show-macd']).toBe(false);
        expect(saved['show-bollinger']).toBe(true);
        expect(saved['show-stochastic']).toBe(false);
        expect(saved['ma-20']).toBe(true);
        expect(saved['ma-50']).toBe(false);
        expect(saved['ma-200']).toBe(true);
        expect(saved['chart-type']).toBe('candlestick');
        expect(saved['show-markers']).toBe(true);
    });

    test('AC6.2: restoreIndicatorState returns saved values, not defaults', () => {
        const saved = {
            'show-rsi': true,
            'show-macd': true,
            'ma-20': false,
            'chart-type': 'candlestick',
            'show-markers': true
        };

        const restored = restoreIndicatorState(saved, defaultState);

        expect(restored['show-rsi']).toBe(true);
        expect(restored['show-macd']).toBe(true);
        expect(restored['ma-20']).toBe(false);
        expect(restored['chart-type']).toBe('candlestick');
        expect(restored['show-markers']).toBe(true);
    });

    test('AC6.2: restoreIndicatorState returns defaults when no saved state', () => {
        const restored = restoreIndicatorState(null, defaultState);

        expect(restored['show-rsi']).toBe(false);
        expect(restored['ma-20']).toBe(true);
        expect(restored['chart-type']).toBe('line');
        expect(restored['show-markers']).toBe(false);
    });

    test('AC6.F1: RSI checked before disable is restored after re-enable', () => {
        const userState = {
            'show-rsi': true,
            'show-macd': false,
            'show-bollinger': false,
            'show-stochastic': false,
            'ma-20': true,
            'ma-50': true,
            'ma-200': false,
            'chart-type': 'line',
            'show-markers': false
        };

        // Simulate: save state before disabling
        const saved = saveIndicatorState(userState);
        expect(saved['show-rsi']).toBe(true);

        // Simulate: restore after re-enabling
        const restored = restoreIndicatorState(saved, defaultState);
        expect(restored['show-rsi']).toBe(true);
    });

    test('calling save twice does not overwrite first saved state', () => {
        const firstState = { 'show-rsi': true, 'show-macd': true };
        let savedIndicatorState = null;

        // First disable: save state
        if (!savedIndicatorState) {
            savedIndicatorState = saveIndicatorState(firstState);
        }
        expect(savedIndicatorState['show-rsi']).toBe(true);

        // Second disable call (e.g., adding another benchmark): should NOT overwrite
        const disabledState = { 'show-rsi': false, 'show-macd': false };
        if (!savedIndicatorState) {
            savedIndicatorState = saveIndicatorState(disabledState);
        }

        // Original state preserved
        expect(savedIndicatorState['show-rsi']).toBe(true);
        expect(savedIndicatorState['show-macd']).toBe(true);
    });

    test('re-enable clears savedIndicatorState to null', () => {
        let savedIndicatorState = saveIndicatorState({ 'show-rsi': true });

        // Simulate re-enable: restore and clear
        const restored = restoreIndicatorState(savedIndicatorState, defaultState);
        savedIndicatorState = null;

        expect(restored['show-rsi']).toBe(true);
        expect(savedIndicatorState).toBeNull();
    });

    test('saveIndicatorState creates independent copy (no reference sharing)', () => {
        const original = { 'show-rsi': true, 'ma-20': true };
        const saved = saveIndicatorState(original);

        // Mutate original (simulating checkbox uncheck during disable)
        original['show-rsi'] = false;
        original['ma-20'] = false;

        // Saved state should be unaffected
        expect(saved['show-rsi']).toBe(true);
        expect(saved['ma-20']).toBe(true);
    });
});

// ========================================================
// Benchmark localStorage persistence (AC7.1, AC7.F1)
// ========================================================

/**
 * Pure save function: serializes benchmark tickers to JSON string.
 * Mirrors saveBenchmarkSelections() logic without DOM/localStorage dependency.
 */
function serializeBenchmarks(benchmarkTickers) {
    return JSON.stringify(benchmarkTickers);
}

/**
 * Pure load function: parses and validates benchmark tickers from stored string.
 * Mirrors loadBenchmarkSelections() logic without localStorage dependency.
 * @param {string|null} data - Raw string from localStorage
 * @returns {string[]} Validated array of ticker strings
 */
function parseBenchmarks(data) {
    if (!data) return [];
    try {
        const parsed = JSON.parse(data);
        if (!Array.isArray(parsed)) return [];
        return parsed.filter(t => typeof t === 'string' && t.length > 0);
    } catch (error) {
        return [];
    }
}

describe('Benchmark localStorage persistence (AC7.1, AC7.F1)', () => {
    test('AC7.1: serializeBenchmarks stores tickers as JSON array', () => {
        const result = serializeBenchmarks(['SPY', 'QQQ']);
        expect(result).toBe('["SPY","QQQ"]');
        expect(JSON.parse(result)).toEqual(['SPY', 'QQQ']);
    });

    test('AC7.1: serializeBenchmarks handles empty array', () => {
        const result = serializeBenchmarks([]);
        expect(result).toBe('[]');
    });

    test('AC7.F1: parseBenchmarks returns empty array for null', () => {
        expect(parseBenchmarks(null)).toEqual([]);
    });

    test('AC7.F1: parseBenchmarks returns empty array for non-JSON string', () => {
        expect(parseBenchmarks('not valid json{')).toEqual([]);
    });

    test('AC7.F1: parseBenchmarks returns empty array for non-array JSON', () => {
        expect(parseBenchmarks('{"foo": "bar"}')).toEqual([]);
        expect(parseBenchmarks('"just a string"')).toEqual([]);
        expect(parseBenchmarks('42')).toEqual([]);
    });

    test('AC7.F1: parseBenchmarks filters non-string elements', () => {
        expect(parseBenchmarks('[1, null, "SPY", "", true, "QQQ"]')).toEqual(['SPY', 'QQQ']);
    });

    test('round-trip: serialize then parse returns same tickers', () => {
        const tickers = ['SPY', 'QQQ', 'DIA'];
        const serialized = serializeBenchmarks(tickers);
        const restored = parseBenchmarks(serialized);
        expect(restored).toEqual(tickers);
    });

    test('round-trip: empty array survives serialize/parse', () => {
        const serialized = serializeBenchmarks([]);
        const restored = parseBenchmarks(serialized);
        expect(restored).toEqual([]);
    });
});
