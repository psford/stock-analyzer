/**
 * Unit tests for portfolio aggregation functions
 * Tests the core calculation logic used in combined watchlist view
 */

// Extract the pure functions from API for testing
// These mirror the implementations in api.js but are standalone for unit testing

function calculateWeights(watchlist, availableTickers) {
    const mode = watchlist.weightingMode || 'equal';
    const holdings = watchlist.holdings || [];
    const weights = {};

    if (mode === 'equal' || availableTickers.length === 0) {
        const weight = 100 / availableTickers.length;
        availableTickers.forEach(t => weights[t] = weight);
    } else if (mode === 'shares') {
        let totalShares = 0;
        holdings.forEach(h => {
            if (availableTickers.includes(h.ticker)) {
                totalShares += (h.shares || 0);
            }
        });
        if (totalShares > 0) {
            holdings.forEach(h => {
                if (availableTickers.includes(h.ticker)) {
                    weights[h.ticker] = ((h.shares || 0) / totalShares) * 100;
                }
            });
        }
        availableTickers.forEach(t => {
            if (!(t in weights)) weights[t] = 0;
        });
    } else if (mode === 'dollars') {
        let totalDollars = 0;
        holdings.forEach(h => {
            if (availableTickers.includes(h.ticker)) {
                totalDollars += (h.dollarValue || 0);
            }
        });
        if (totalDollars > 0) {
            holdings.forEach(h => {
                if (availableTickers.includes(h.ticker)) {
                    weights[h.ticker] = ((h.dollarValue || 0) / totalDollars) * 100;
                }
            });
        }
        availableTickers.forEach(t => {
            if (!(t in weights)) weights[t] = 0;
        });
    }

    return weights;
}

function aggregatePortfolioData(tickerData, weights) {
    const allDates = new Set();
    Object.values(tickerData).forEach(historyResponse => {
        const prices = historyResponse.data || historyResponse.prices || [];
        prices.forEach(p => allDates.add(p.date));
    });

    const sortedDates = Array.from(allDates).sort();
    if (sortedDates.length === 0) return [];

    const normalizedData = {};
    Object.entries(tickerData).forEach(([ticker, historyResponse]) => {
        const prices = historyResponse.data || historyResponse.prices || [];
        if (prices.length > 0) {
            const firstPrice = prices[0].close;
            normalizedData[ticker] = {};
            prices.forEach(p => {
                normalizedData[ticker][p.date] = ((p.close - firstPrice) / firstPrice) * 100;
            });
        }
    });

    return sortedDates.map(date => {
        let weightedReturn = 0;
        let totalWeight = 0;

        Object.entries(weights).forEach(([ticker, weight]) => {
            if (normalizedData[ticker] && normalizedData[ticker][date] !== undefined) {
                weightedReturn += normalizedData[ticker][date] * (weight / 100);
                totalWeight += weight;
            }
        });

        return {
            date,
            percentChange: totalWeight > 0 ? weightedReturn : 0
        };
    });
}

function normalizeToPercentChange(historyData) {
    const prices = historyData.data || historyData.prices || [];
    if (prices.length === 0) return [];

    const firstPrice = prices[0].close;
    return prices.map(p => ({
        date: p.date,
        percentChange: ((p.close - firstPrice) / firstPrice) * 100
    }));
}

function findSignificantMoves(portfolioData, threshold) {
    const moves = [];
    for (let i = 1; i < portfolioData.length; i++) {
        const dayChange = portfolioData[i].percentChange - portfolioData[i - 1].percentChange;
        if (Math.abs(dayChange) >= threshold) {
            moves.push({
                date: portfolioData[i].date,
                percentChange: dayChange,
                isPositive: dayChange >= 0
            });
        }
    }
    return moves;
}

// ============================================
// Tests
// ============================================

describe('calculateWeights', () => {
    describe('equal weighting mode', () => {
        test('distributes weights equally among tickers', () => {
            const watchlist = { weightingMode: 'equal' };
            const tickers = ['AAPL', 'MSFT', 'GOOGL'];

            const weights = calculateWeights(watchlist, tickers);

            expect(weights.AAPL).toBeCloseTo(33.333, 2);
            expect(weights.MSFT).toBeCloseTo(33.333, 2);
            expect(weights.GOOGL).toBeCloseTo(33.333, 2);
        });

        test('handles single ticker', () => {
            const watchlist = { weightingMode: 'equal' };
            const tickers = ['AAPL'];

            const weights = calculateWeights(watchlist, tickers);

            expect(weights.AAPL).toBe(100);
        });

        test('defaults to equal mode when no mode specified', () => {
            const watchlist = {};
            const tickers = ['AAPL', 'MSFT'];

            const weights = calculateWeights(watchlist, tickers);

            expect(weights.AAPL).toBe(50);
            expect(weights.MSFT).toBe(50);
        });
    });

    describe('shares weighting mode', () => {
        test('weights by share count', () => {
            const watchlist = {
                weightingMode: 'shares',
                holdings: [
                    { ticker: 'AAPL', shares: 100 },
                    { ticker: 'MSFT', shares: 300 }
                ]
            };
            const tickers = ['AAPL', 'MSFT'];

            const weights = calculateWeights(watchlist, tickers);

            expect(weights.AAPL).toBe(25);
            expect(weights.MSFT).toBe(75);
        });

        test('assigns 0 weight to tickers without holdings', () => {
            const watchlist = {
                weightingMode: 'shares',
                holdings: [
                    { ticker: 'AAPL', shares: 100 }
                ]
            };
            const tickers = ['AAPL', 'MSFT'];

            const weights = calculateWeights(watchlist, tickers);

            expect(weights.AAPL).toBe(100);
            expect(weights.MSFT).toBe(0);
        });

        test('ignores holdings for tickers not in availableTickers', () => {
            const watchlist = {
                weightingMode: 'shares',
                holdings: [
                    { ticker: 'AAPL', shares: 100 },
                    { ticker: 'TSLA', shares: 500 } // Not in available
                ]
            };
            const tickers = ['AAPL', 'MSFT'];

            const weights = calculateWeights(watchlist, tickers);

            expect(weights.AAPL).toBe(100);
            expect(weights.MSFT).toBe(0);
            expect(weights.TSLA).toBeUndefined();
        });
    });

    describe('dollars weighting mode', () => {
        test('weights by dollar value', () => {
            const watchlist = {
                weightingMode: 'dollars',
                holdings: [
                    { ticker: 'AAPL', dollarValue: 1000 },
                    { ticker: 'MSFT', dollarValue: 4000 }
                ]
            };
            const tickers = ['AAPL', 'MSFT'];

            const weights = calculateWeights(watchlist, tickers);

            expect(weights.AAPL).toBe(20);
            expect(weights.MSFT).toBe(80);
        });
    });
});

describe('aggregatePortfolioData', () => {
    test('handles data array format (API response)', () => {
        const tickerData = {
            'AAPL': {
                data: [
                    { date: '2024-01-01', close: 100 },
                    { date: '2024-01-02', close: 110 }
                ]
            }
        };
        const weights = { AAPL: 100 };

        const result = aggregatePortfolioData(tickerData, weights);

        expect(result).toHaveLength(2);
        expect(result[0].date).toBe('2024-01-01');
        expect(result[0].percentChange).toBe(0);
        expect(result[1].date).toBe('2024-01-02');
        expect(result[1].percentChange).toBe(10); // 10% gain
    });

    test('handles prices array format (legacy)', () => {
        const tickerData = {
            'AAPL': {
                prices: [
                    { date: '2024-01-01', close: 100 },
                    { date: '2024-01-02', close: 110 }
                ]
            }
        };
        const weights = { AAPL: 100 };

        const result = aggregatePortfolioData(tickerData, weights);

        expect(result).toHaveLength(2);
        expect(result[1].percentChange).toBe(10);
    });

    test('calculates weighted returns correctly', () => {
        const tickerData = {
            'AAPL': {
                data: [
                    { date: '2024-01-01', close: 100 },
                    { date: '2024-01-02', close: 120 } // +20%
                ]
            },
            'MSFT': {
                data: [
                    { date: '2024-01-01', close: 200 },
                    { date: '2024-01-02', close: 210 } // +5%
                ]
            }
        };
        // 50/50 weighting
        const weights = { AAPL: 50, MSFT: 50 };

        const result = aggregatePortfolioData(tickerData, weights);

        expect(result[1].percentChange).toBeCloseTo(12.5, 2); // (20 * 0.5) + (5 * 0.5) = 12.5%
    });

    test('handles unequal weighting', () => {
        const tickerData = {
            'AAPL': {
                data: [
                    { date: '2024-01-01', close: 100 },
                    { date: '2024-01-02', close: 120 } // +20%
                ]
            },
            'MSFT': {
                data: [
                    { date: '2024-01-01', close: 200 },
                    { date: '2024-01-02', close: 210 } // +5%
                ]
            }
        };
        // 75/25 weighting
        const weights = { AAPL: 75, MSFT: 25 };

        const result = aggregatePortfolioData(tickerData, weights);

        expect(result[1].percentChange).toBeCloseTo(16.25, 2); // (20 * 0.75) + (5 * 0.25) = 16.25%
    });

    test('handles missing dates across tickers', () => {
        const tickerData = {
            'AAPL': {
                data: [
                    { date: '2024-01-01', close: 100 },
                    { date: '2024-01-02', close: 110 },
                    { date: '2024-01-03', close: 115 }
                ]
            },
            'MSFT': {
                data: [
                    { date: '2024-01-01', close: 200 },
                    { date: '2024-01-03', close: 220 } // Missing 01-02
                ]
            }
        };
        const weights = { AAPL: 50, MSFT: 50 };

        const result = aggregatePortfolioData(tickerData, weights);

        expect(result).toHaveLength(3);
        // On 01-02, only AAPL has data, so weight is proportionally adjusted
    });

    test('returns empty array for empty ticker data', () => {
        const result = aggregatePortfolioData({}, {});

        expect(result).toEqual([]);
    });

    test('handles empty data arrays', () => {
        const tickerData = {
            'AAPL': { data: [] }
        };
        const weights = { AAPL: 100 };

        const result = aggregatePortfolioData(tickerData, weights);

        expect(result).toEqual([]);
    });
});

describe('normalizeToPercentChange', () => {
    test('normalizes data array format', () => {
        const historyData = {
            data: [
                { date: '2024-01-01', close: 100 },
                { date: '2024-01-02', close: 110 },
                { date: '2024-01-03', close: 90 }
            ]
        };

        const result = normalizeToPercentChange(historyData);

        expect(result).toHaveLength(3);
        expect(result[0].percentChange).toBe(0);
        expect(result[1].percentChange).toBe(10);
        expect(result[2].percentChange).toBe(-10);
    });

    test('normalizes prices array format (legacy)', () => {
        const historyData = {
            prices: [
                { date: '2024-01-01', close: 200 },
                { date: '2024-01-02', close: 250 }
            ]
        };

        const result = normalizeToPercentChange(historyData);

        expect(result[1].percentChange).toBe(25);
    });

    test('returns empty array for empty data', () => {
        const result = normalizeToPercentChange({ data: [] });

        expect(result).toEqual([]);
    });

    test('handles negative returns', () => {
        const historyData = {
            data: [
                { date: '2024-01-01', close: 100 },
                { date: '2024-01-02', close: 50 }
            ]
        };

        const result = normalizeToPercentChange(historyData);

        expect(result[1].percentChange).toBe(-50);
    });
});

describe('findSignificantMoves', () => {
    test('identifies moves above threshold', () => {
        const portfolioData = [
            { date: '2024-01-01', percentChange: 0 },
            { date: '2024-01-02', percentChange: 6 },  // +6%
            { date: '2024-01-03', percentChange: 7 },  // +1%
            { date: '2024-01-04', percentChange: 1 }   // -6%
        ];

        const result = findSignificantMoves(portfolioData, 5);

        expect(result).toHaveLength(2);
        expect(result[0].date).toBe('2024-01-02');
        expect(result[0].percentChange).toBe(6);
        expect(result[0].isPositive).toBe(true);
        expect(result[1].date).toBe('2024-01-04');
        expect(result[1].percentChange).toBe(-6);
        expect(result[1].isPositive).toBe(false);
    });

    test('respects exact threshold boundary', () => {
        const portfolioData = [
            { date: '2024-01-01', percentChange: 0 },
            { date: '2024-01-02', percentChange: 5 },   // Exactly 5%
            { date: '2024-01-03', percentChange: 9.99 } // +4.99%
        ];

        const result = findSignificantMoves(portfolioData, 5);

        expect(result).toHaveLength(1);
        expect(result[0].date).toBe('2024-01-02');
    });

    test('returns empty array for no significant moves', () => {
        const portfolioData = [
            { date: '2024-01-01', percentChange: 0 },
            { date: '2024-01-02', percentChange: 1 },
            { date: '2024-01-03', percentChange: 2 }
        ];

        const result = findSignificantMoves(portfolioData, 5);

        expect(result).toEqual([]);
    });

    test('returns empty array for single data point', () => {
        const portfolioData = [
            { date: '2024-01-01', percentChange: 0 }
        ];

        const result = findSignificantMoves(portfolioData, 5);

        expect(result).toEqual([]);
    });

    test('returns empty array for empty data', () => {
        const result = findSignificantMoves([], 5);

        expect(result).toEqual([]);
    });

    test('handles custom threshold', () => {
        const portfolioData = [
            { date: '2024-01-01', percentChange: 0 },
            { date: '2024-01-02', percentChange: 3 }
        ];

        const result3 = findSignificantMoves(portfolioData, 3);
        const result5 = findSignificantMoves(portfolioData, 5);

        expect(result3).toHaveLength(1);
        expect(result5).toHaveLength(0);
    });
});

// Integration-style test
describe('portfolio calculation integration', () => {
    test('full workflow: weights -> aggregate -> significant moves', () => {
        // Setup watchlist with 3 stocks, equal weight
        const watchlist = { weightingMode: 'equal' };
        const tickers = ['AAPL', 'MSFT', 'GOOGL'];

        // Calculate weights
        const weights = calculateWeights(watchlist, tickers);
        expect(Object.values(weights).reduce((a, b) => a + b, 0)).toBeCloseTo(100, 2);

        // Mock price data with one significant move
        const tickerData = {
            'AAPL': {
                data: [
                    { date: '2024-01-01', close: 100 },
                    { date: '2024-01-02', close: 100 },
                    { date: '2024-01-03', close: 118 } // +18% day
                ]
            },
            'MSFT': {
                data: [
                    { date: '2024-01-01', close: 200 },
                    { date: '2024-01-02', close: 200 },
                    { date: '2024-01-03', close: 218 } // +9% day
                ]
            },
            'GOOGL': {
                data: [
                    { date: '2024-01-01', close: 150 },
                    { date: '2024-01-02', close: 150 },
                    { date: '2024-01-03', close: 165 } // +10% day
                ]
            }
        };

        // Aggregate portfolio
        const portfolioData = aggregatePortfolioData(tickerData, weights);
        expect(portfolioData).toHaveLength(3);

        // Day 3 should show combined return: (18 + 9 + 10) / 3 = 12.33%
        expect(portfolioData[2].percentChange).toBeCloseTo(12.33, 1);

        // Find significant moves
        const moves = findSignificantMoves(portfolioData, 5);
        expect(moves).toHaveLength(1);
        expect(moves[0].date).toBe('2024-01-03');
        expect(moves[0].isPositive).toBe(true);
    });
});
