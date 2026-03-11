/**
 * Unit tests for chartSeries management functions
 * Tests the REAL App object methods imported from app.js
 */

// Stub globals that app.js references at module load time
global.API = {};
global.Charts = {};
global.Watchlist = { loadWatchlists: jest.fn() };
global.LogSanitizer = { sanitize: (s) => s };
global.Plotly = { purge: jest.fn() };
global.flatpickr = undefined;
window.matchMedia = window.matchMedia || jest.fn().mockReturnValue({ matches: false });

// Import the real production code
const { App, SERIES_PALETTE, MAX_BENCHMARKS } = require('../js/app.js');

// Helper: reset App.chartSeries before each test
function resetApp() {
    App.chartSeries = [];
    App.savedIndicatorState = null;
    App.currentTicker = null;
    App.historyData = null;
}

// Suppress alert() in tests (addSeries calls alert on max benchmarks)
beforeAll(() => {
    global.alert = jest.fn();
});

beforeEach(() => {
    resetApp();
    jest.clearAllMocks();
});

// ========================================================
// chartSeries management functions
// ========================================================

describe('chartSeries management functions', () => {
    describe('buildPrimarySeries', () => {
        test('returns null when no historyData', () => {
            App.currentTicker = 'AAPL';
            App.historyData = null;
            expect(App.buildPrimarySeries()).toBeNull();
        });

        test('returns null when no currentTicker', () => {
            App.currentTicker = null;
            App.historyData = { data: [1, 2, 3] };
            expect(App.buildPrimarySeries()).toBeNull();
        });

        test('builds primary series with correct structure', () => {
            App.currentTicker = 'AAPL';
            App.historyData = { data: [1, 2, 3] };
            const result = App.buildPrimarySeries();
            expect(result).toEqual({
                ticker: 'AAPL',
                label: 'AAPL',
                type: 'primary',
                data: { data: [1, 2, 3] },
                color: SERIES_PALETTE[0].color,
                dash: SERIES_PALETTE[0].dash
            });
        });

        test('assigns primary palette (blue, solid)', () => {
            App.currentTicker = 'MSFT';
            App.historyData = { data: [] };
            const result = App.buildPrimarySeries();
            expect(result.color).toBe('#0072B2');
            expect(result.dash).toBe('solid');
        });
    });

    describe('assignPalettePositions', () => {
        test('assigns correct colors and dashes by position', () => {
            App.chartSeries = [
                { ticker: 'A', type: 'primary', color: '', dash: '' },
                { ticker: 'B', type: 'comparison', color: '', dash: '' },
                { ticker: 'C', type: 'benchmark', color: '', dash: '' },
            ];
            App.assignPalettePositions();
            expect(App.chartSeries[0].color).toBe(SERIES_PALETTE[0].color);
            expect(App.chartSeries[1].color).toBe(SERIES_PALETTE[1].color);
            expect(App.chartSeries[2].color).toBe(SERIES_PALETTE[2].color);
        });

        test('leaves excess series uncolored when beyond palette length', () => {
            App.chartSeries = Array.from({ length: 8 }, (_, i) => ({
                ticker: `T${i}`, type: 'benchmark', color: '', dash: ''
            }));
            App.assignPalettePositions();
            // Series within palette range get colors
            expect(App.chartSeries[0].color).toBe(SERIES_PALETTE[0].color);
            // Series beyond palette length keep their original color
            expect(App.chartSeries[7].color).toBe('');
        });
    });

    describe('addSeries', () => {
        beforeEach(() => {
            App.chartSeries = [
                { ticker: 'AAPL', label: 'AAPL', type: 'primary', data: {}, color: '#0072B2', dash: 'solid' }
            ];
        });

        test('adds a new series successfully', () => {
            const result = App.addSeries('SPY', 'S&P 500', 'benchmark', { data: [] });
            expect(result).toBe(true);
            expect(App.chartSeries).toHaveLength(2);
            expect(App.chartSeries[1].ticker).toBe('SPY');
        });

        test('prevents duplicate tickers', () => {
            const result = App.addSeries('AAPL', 'Apple', 'benchmark', { data: [] });
            expect(result).toBe(false);
            expect(App.chartSeries).toHaveLength(1);
        });

        test('replaces existing comparison with new one', () => {
            App.addSeries('MSFT', 'Microsoft', 'comparison', { data: [1] });
            expect(App.chartSeries).toHaveLength(2);

            App.addSeries('GOOG', 'Google', 'comparison', { data: [2] });
            expect(App.chartSeries).toHaveLength(2);
            expect(App.chartSeries[1].ticker).toBe('GOOG');
        });

        test('enforces max benchmarks limit', () => {
            for (let i = 0; i < MAX_BENCHMARKS; i++) {
                App.addSeries(`B${i}`, `Bench ${i}`, 'benchmark', { data: [] });
            }
            expect(App.chartSeries).toHaveLength(1 + MAX_BENCHMARKS);

            const result = App.addSeries('EXTRA', 'Extra', 'benchmark', { data: [] });
            expect(result).toBe(false);
            expect(App.chartSeries).toHaveLength(1 + MAX_BENCHMARKS);
            expect(global.alert).toHaveBeenCalled();
        });

        test('assigns palette colors by position', () => {
            App.addSeries('SPY', 'S&P 500', 'benchmark', { data: [] });
            expect(App.chartSeries[1].color).toBe(SERIES_PALETTE[1].color);
            expect(App.chartSeries[1].dash).toBe(SERIES_PALETTE[1].dash);
        });
    });

    describe('removeSeries', () => {
        beforeEach(() => {
            App.chartSeries = [
                { ticker: 'AAPL', type: 'primary', color: '#0072B2', dash: 'solid' },
                { ticker: 'SPY', type: 'benchmark', color: '#E69F00', dash: 'dash' },
            ];
        });

        test('cannot remove primary series (index 0)', () => {
            const result = App.removeSeries('AAPL');
            expect(result).toBeNull();
            expect(App.chartSeries).toHaveLength(2);
        });

        test('removes non-primary series', () => {
            const removed = App.removeSeries('SPY');
            expect(removed.ticker).toBe('SPY');
            expect(App.chartSeries).toHaveLength(1);
        });

        test('returns null when ticker not found', () => {
            const result = App.removeSeries('NONEXIST');
            expect(result).toBeNull();
        });

        test('reassigns palette positions after removal', () => {
            App.addSeries('QQQ', 'Nasdaq', 'benchmark', { data: [] });
            App.removeSeries('SPY');
            // QQQ should now be at position 1
            expect(App.chartSeries[1].color).toBe(SERIES_PALETTE[1].color);
        });
    });

    describe('getSeriesByType', () => {
        beforeEach(() => {
            App.chartSeries = [
                { ticker: 'AAPL', type: 'primary' },
                { ticker: 'SPY', type: 'benchmark' },
                { ticker: 'QQQ', type: 'benchmark' },
                { ticker: 'MSFT', type: 'comparison' },
            ];
        });

        test('returns all series of a given type', () => {
            const benchmarks = App.getSeriesByType('benchmark');
            expect(benchmarks).toHaveLength(2);
            expect(benchmarks[0].ticker).toBe('SPY');
        });

        test('returns empty array when no series of type found', () => {
            expect(App.getSeriesByType('nonexistent')).toHaveLength(0);
        });
    });

    describe('clearBenchmarkSeries', () => {
        test('removes all benchmark series, keeps primary and comparison', () => {
            App.chartSeries = [
                { ticker: 'AAPL', type: 'primary', color: '', dash: '' },
                { ticker: 'MSFT', type: 'comparison', color: '', dash: '' },
                { ticker: 'SPY', type: 'benchmark', color: '', dash: '' },
                { ticker: 'QQQ', type: 'benchmark', color: '', dash: '' },
            ];
            App.clearBenchmarkSeries();
            expect(App.chartSeries).toHaveLength(2);
            expect(App.chartSeries.some(s => s.type === 'benchmark')).toBe(false);
        });

        test('does nothing if no benchmarks present', () => {
            App.chartSeries = [{ ticker: 'AAPL', type: 'primary', color: '', dash: '' }];
            App.clearBenchmarkSeries();
            expect(App.chartSeries).toHaveLength(1);
        });
    });

    describe('clearAllSeries', () => {
        test('removes all non-primary series', () => {
            App.chartSeries = [
                { ticker: 'AAPL', type: 'primary' },
                { ticker: 'MSFT', type: 'comparison' },
                { ticker: 'SPY', type: 'benchmark' },
            ];
            App.clearAllSeries();
            expect(App.chartSeries).toHaveLength(1);
            expect(App.chartSeries[0].type).toBe('primary');
        });

        test('leaves empty array if no primary series', () => {
            App.chartSeries = [{ ticker: 'SPY', type: 'benchmark' }];
            App.clearAllSeries();
            expect(App.chartSeries).toHaveLength(0);
        });
    });

    describe('isMultiSeriesMode', () => {
        test('returns true when more than one series', () => {
            App.chartSeries = [{ ticker: 'A' }, { ticker: 'B' }];
            expect(App.isMultiSeriesMode()).toBe(true);
        });

        test('returns false with exactly one series', () => {
            App.chartSeries = [{ ticker: 'A' }];
            expect(App.isMultiSeriesMode()).toBe(false);
        });

        test('returns false with no series', () => {
            App.chartSeries = [];
            expect(App.isMultiSeriesMode()).toBe(false);
        });
    });

    describe('Acceptance Criteria AC1.1: chartSeries structure', () => {
        test('AC1.1: Each series entry has correct structure with all required fields', () => {
            App.chartSeries = [
                { ticker: 'AAPL', label: 'AAPL', type: 'primary', data: {}, color: '#0072B2', dash: 'solid' }
            ];
            App.addSeries('SPY', 'S&P 500', 'benchmark', { close: [100, 101] });

            const spy = App.chartSeries[1];
            expect(spy).toHaveProperty('ticker', 'SPY');
            expect(spy).toHaveProperty('label', 'S&P 500');
            expect(spy).toHaveProperty('type', 'benchmark');
            expect(spy).toHaveProperty('data');
            expect(spy).toHaveProperty('color');
            expect(spy).toHaveProperty('dash');
        });
    });

    describe('Acceptance Criteria AC1.F2: Max benchmarks enforcement', () => {
        test('AC1.F2: Adding 6th benchmark is rejected with max 5 enforced', () => {
            App.chartSeries = [
                { ticker: 'AAPL', label: 'AAPL', type: 'primary', data: {}, color: '#0072B2', dash: 'solid' }
            ];
            for (let i = 0; i < MAX_BENCHMARKS; i++) {
                App.addSeries(`B${i}`, `Bench${i}`, 'benchmark', { data: [] });
            }
            const result = App.addSeries('B5', 'Bench5', 'benchmark', { data: [] });
            expect(result).toBe(false);
            expect(App.getSeriesByType('benchmark')).toHaveLength(MAX_BENCHMARKS);
        });
    });
});

// ========================================================
// Benchmark localStorage persistence (AC7.1, AC7.F1)
// Tests the REAL App.saveBenchmarkSelections / loadBenchmarkSelections
// ========================================================

describe('Benchmark localStorage persistence (AC7.1, AC7.F1)', () => {
    beforeEach(() => {
        localStorage.clear();
        resetApp();
    });

    test('AC7.1: saveBenchmarkSelections stores tickers as JSON array', () => {
        App.chartSeries = [
            { ticker: 'AAPL', type: 'primary' },
            { ticker: 'SPY', type: 'benchmark' },
            { ticker: 'QQQ', type: 'benchmark' },
        ];
        App.saveBenchmarkSelections();

        const stored = JSON.parse(localStorage.getItem('stockAnalyzer_benchmarks'));
        expect(stored).toEqual(['SPY', 'QQQ']);
    });

    test('AC7.1: saveBenchmarkSelections stores empty array when no benchmarks', () => {
        App.chartSeries = [{ ticker: 'AAPL', type: 'primary' }];
        App.saveBenchmarkSelections();

        const stored = JSON.parse(localStorage.getItem('stockAnalyzer_benchmarks'));
        expect(stored).toEqual([]);
    });

    test('AC7.F1: loadBenchmarkSelections returns empty array for null', () => {
        expect(App.loadBenchmarkSelections()).toEqual([]);
    });

    test('AC7.F1: loadBenchmarkSelections returns empty for corrupted data', () => {
        localStorage.setItem('stockAnalyzer_benchmarks', 'not valid json{');
        expect(App.loadBenchmarkSelections()).toEqual([]);
        // Should also clear the corrupted data
        expect(localStorage.getItem('stockAnalyzer_benchmarks')).toBeNull();
    });

    test('AC7.F1: loadBenchmarkSelections returns empty for non-array JSON', () => {
        localStorage.setItem('stockAnalyzer_benchmarks', '{"foo": "bar"}');
        expect(App.loadBenchmarkSelections()).toEqual([]);
    });

    test('AC7.F1: loadBenchmarkSelections filters non-string elements', () => {
        localStorage.setItem('stockAnalyzer_benchmarks', '[1, null, "SPY", "", true, "QQQ"]');
        expect(App.loadBenchmarkSelections()).toEqual(['SPY', 'QQQ']);
    });

    test('round-trip: save then load returns same tickers', () => {
        App.chartSeries = [
            { ticker: 'AAPL', type: 'primary' },
            { ticker: 'SPY', type: 'benchmark' },
            { ticker: 'QQQ', type: 'benchmark' },
            { ticker: 'DIA', type: 'benchmark' },
        ];
        App.saveBenchmarkSelections();
        const restored = App.loadBenchmarkSelections();
        expect(restored).toEqual(['SPY', 'QQQ', 'DIA']);
    });

    test('round-trip: empty benchmarks survive save/load', () => {
        App.chartSeries = [{ ticker: 'AAPL', type: 'primary' }];
        App.saveBenchmarkSelections();
        expect(App.loadBenchmarkSelections()).toEqual([]);
    });
});
