/**
 * DragMeasure — Click-and-drag chart interactions for Plotly charts.
 *
 * Left-click drag:       Pan — shifts x-axis range to scroll through data.
 * Shift+left-click drag: Performance measurement — floating bubble with % return, $ change.
 * Right-click drag:      Zoom to selection — crops chart to the dragged date range.
 *
 * Pan state machine:     IDLE → (left mousedown) → PANNING → (mouseup) → IDLE
 * Measure state machine: IDLE → (shift+left mousedown) → MEASURING → (mouseup) → PINNED → (click/Esc) → IDLE
 * Zoom state machine:    IDLE → (right mousedown) → ZOOMING → (mouseup) → IDLE (chart zoomed)
 *
 * Renders HTML overlay (vertical lines + shaded region + floating bubble)
 * on top of Plotly SVG. Zero Plotly API calls during drag for performance.
 *
 * Phase 2 hooks: onPinned callback, _buildBubbleHTML extensible, export-ready data object.
 */
const DragMeasure = {
    // --- State ---
    state: 'idle', // 'idle' | 'panning' | 'measuring' | 'pinned' | 'zooming'
    startDataIndex: null,
    endDataIndex: null,
    _rafPending: false,
    _startClientX: null, // raw pixel X at mousedown (for zoom/pan)
    _panLastX: null,     // last clientX during pan (for delta calculation)
    _wheelAccum: 0,      // accumulated wheel delta (throttled via rAF)
    _wheelFraction: 0.5, // cursor position fraction for wheel zoom center
    _wheelRafId: null,   // rAF handle for wheel zoom

    // --- DOM references ---
    chartEl: null,
    overlayEl: null,
    shadedRegion: null,
    startLine: null,
    endLine: null,
    bubble: null,
    zoomRegion: null,

    // --- Options (set via attach) ---
    dataSource: null,         // () => [{date, open, high, low, close, ...}] or [{date, percentChange, ...}]
    dataType: 'price',        // 'price' (stock charts) or 'percent' (portfolio charts)
    isComparisonMode: null,   // () => boolean
    getComparisonData: null,  // () => {data: [...]}
    getPrimarySymbol: null,   // () => 'AAPL'
    getComparisonSymbol: null,// () => 'SPY'
    onPinned: null,           // Phase 2: callback(data) when measurement is pinned
    onRangeExtend: null,      // callback(fromDate, toDate) when scroll zoom exceeds data bounds
    _rangeExtendTimer: null,  // debounce timer for range extension fetch

    // --- Bound handlers (for removeEventListener) ---
    _boundMouseDown: null,
    _boundMouseMove: null,
    _boundMouseUp: null,
    _boundDocClick: null,
    _boundKeyDown: null,
    _boundContextMenu: null,
    _boundWheel: null,
    _boundDblClick: null,

    /**
     * Attach drag-measure to a Plotly chart element.
     * Call after Plotly renders (after _smartPlot resolves).
     */
    attach(chartElementId, options) {
        this.detach(); // Clean up previous attachment

        this.chartEl = document.getElementById(chartElementId);
        if (!this.chartEl) return;

        this.dataSource = options.dataSource;
        this.dataType = options.dataType || 'price';
        this.isComparisonMode = options.isComparisonMode || (() => false);
        this.getComparisonData = options.getComparisonData || (() => null);
        this.getPrimarySymbol = options.getPrimarySymbol || (() => '');
        this.getComparisonSymbol = options.getComparisonSymbol || (() => '');
        this.onPinned = options.onPinned || null;
        this.onRangeExtend = options.onRangeExtend || null;

        this._createOverlayDOM();

        this._boundMouseDown = this._onMouseDown.bind(this);
        this._boundMouseMove = this._onMouseMove.bind(this);
        this._boundMouseUp = this._onMouseUp.bind(this);
        this._boundDocClick = this._onDocumentClick.bind(this);
        this._boundKeyDown = this._onKeyDown.bind(this);
        this._boundContextMenu = this._onContextMenu.bind(this);
        this._boundWheel = this._onWheel.bind(this);
        this._boundDblClick = this._onDblClick.bind(this);

        // Attach to Plotly's interactive drag surface
        const nsewDrag = this.chartEl.querySelector('.nsewdrag');
        if (nsewDrag) {
            nsewDrag.addEventListener('mousedown', this._boundMouseDown);
            nsewDrag.addEventListener('contextmenu', this._boundContextMenu);
            nsewDrag.addEventListener('wheel', this._boundWheel, { passive: false });
            nsewDrag.addEventListener('dblclick', this._boundDblClick);
            nsewDrag.style.cursor = 'grab';
        }

        document.addEventListener('keydown', this._boundKeyDown);
    },

    /**
     * Detach and clean up all listeners and DOM.
     */
    detach() {
        this._reset();

        if (this.chartEl) {
            const nsewDrag = this.chartEl.querySelector('.nsewdrag');
            if (nsewDrag && this._boundMouseDown) {
                nsewDrag.removeEventListener('mousedown', this._boundMouseDown);
                nsewDrag.style.cursor = '';
            }
            if (nsewDrag && this._boundContextMenu) {
                nsewDrag.removeEventListener('contextmenu', this._boundContextMenu);
            }
            if (nsewDrag && this._boundWheel) {
                nsewDrag.removeEventListener('wheel', this._boundWheel);
            }
            if (nsewDrag && this._boundDblClick) {
                nsewDrag.removeEventListener('dblclick', this._boundDblClick);
            }
        }

        if (this._boundKeyDown) {
            document.removeEventListener('keydown', this._boundKeyDown);
        }

        this._destroyOverlayDOM();

        if (this._rangeExtendTimer) {
            clearTimeout(this._rangeExtendTimer);
            this._rangeExtendTimer = null;
        }

        this.chartEl = null;
        this.dataSource = null;
        this.dataType = 'price';
        this.isComparisonMode = null;
        this.getComparisonData = null;
        this.getPrimarySymbol = null;
        this.getComparisonSymbol = null;
        this.onPinned = null;
        this.onRangeExtend = null;
    },

    // ========== EVENT HANDLERS ==========

    _onContextMenu(e) {
        // Suppress browser context menu on the chart area
        e.preventDefault();
    },

    _onMouseDown(e) {
        const isLeftClick = e.button === 0;
        const isRightClick = e.button === 2;
        if (!isLeftClick && !isRightClick) return;

        // If pinned measurement, any click clears it
        if (this.state === 'pinned') {
            this._reset();
            if (isRightClick) return; // Don't start zoom immediately after clearing
            // For left click, fall through to start new interaction
        }

        const data = this.dataSource();
        if (!data || data.length === 0) return;

        e.preventDefault();
        e.stopPropagation();

        // Dismiss any active significant-move hover card
        const hoverCard = document.getElementById('wiki-hover-card');
        if (hoverCard && !hoverCard.classList.contains('hidden')) {
            hoverCard.classList.add('hidden');
        }

        this._startClientX = e.clientX;

        if (isLeftClick && e.shiftKey) {
            // Shift+left-click: measurement mode
            const date = this._pixelToDate(e.clientX);
            this.startDataIndex = this._findNearestDataPoint(date, data);
            this.endDataIndex = this.startDataIndex;

            this.state = 'measuring';
            this._positionVerticalLine(this.startLine, this.startDataIndex);
            this.startLine.classList.remove('hidden');
            this.overlayEl.style.pointerEvents = 'auto';

            const nsewDrag = this.chartEl.querySelector('.nsewdrag');
            if (nsewDrag) nsewDrag.style.cursor = 'crosshair';
        } else if (isLeftClick) {
            // Left-click: pan mode
            this.state = 'panning';
            this._panLastX = e.clientX;

            const nsewDrag = this.chartEl.querySelector('.nsewdrag');
            if (nsewDrag) nsewDrag.style.cursor = 'grabbing';
        } else {
            // Right-click: zoom selection
            const date = this._pixelToDate(e.clientX);
            this.startDataIndex = this._findNearestDataPoint(date, data);
            this.endDataIndex = this.startDataIndex;

            this.state = 'zooming';
            this.zoomRegion.classList.remove('hidden');
            this.overlayEl.style.pointerEvents = 'auto';
        }

        document.addEventListener('mousemove', this._boundMouseMove);
        document.addEventListener('mouseup', this._boundMouseUp);
    },

    _onMouseMove(e) {
        if (this.state !== 'measuring' && this.state !== 'zooming' && this.state !== 'panning') return;

        if (this.state === 'panning') {
            // Pan: shift x-axis range based on pixel delta (no rAF batching — direct for responsiveness)
            const deltaX = e.clientX - this._panLastX;
            if (deltaX !== 0) {
                this._panLastX = e.clientX;
                this._applyPan(deltaX);
            }
            return;
        }

        if (!this._rafPending) {
            this._rafPending = true;
            const clientX = e.clientX;
            const clientY = e.clientY;
            requestAnimationFrame(() => {
                this._rafPending = false;
                if (this.state === 'measuring') {
                    this._updateMeasureVisuals(clientX);
                    this._updateBubble(clientX, clientY);
                } else if (this.state === 'zooming') {
                    this._updateZoomVisuals(clientX);
                }
            });
        }
    },

    _onMouseUp(e) {
        document.removeEventListener('mousemove', this._boundMouseMove);
        document.removeEventListener('mouseup', this._boundMouseUp);

        if (this.state === 'panning') {
            this.state = 'idle';
            this._panLastX = null;
            const nsewDrag = this.chartEl.querySelector('.nsewdrag');
            if (nsewDrag) nsewDrag.style.cursor = 'grab';
            return;
        }

        if (this.state === 'measuring') {
            // Final visual update
            this._updateMeasureVisuals(e.clientX);
            this._updateBubble(e.clientX, e.clientY);

            this.state = 'pinned';
            this.overlayEl.style.pointerEvents = 'none';

            // Phase 2 hook
            if (this.onPinned) {
                this.onPinned(this._getPinnedData());
            }

            // Listen for dismiss click (delayed to avoid triggering immediately)
            setTimeout(() => {
                document.addEventListener('click', this._boundDocClick);
            }, 0);

        } else if (this.state === 'zooming') {
            this._applyZoom(e.clientX);
            this._reset();
        }
    },

    _onDocumentClick(e) {
        if (this.bubble && !this.bubble.contains(e.target)) {
            this._reset();
        }
    },

    _onKeyDown(e) {
        if (e.key === 'Escape' && this.state !== 'idle') {
            e.preventDefault();
            if (this.state === 'measuring' || this.state === 'zooming' || this.state === 'panning') {
                document.removeEventListener('mousemove', this._boundMouseMove);
                document.removeEventListener('mouseup', this._boundMouseUp);
            }
            this._reset();
            const nsewDrag = this.chartEl && this.chartEl.querySelector('.nsewdrag');
            if (nsewDrag) nsewDrag.style.cursor = 'grab';
        }
    },

    // ========== ZOOM ==========

    _updateZoomVisuals(clientX) {
        const bounds = this._getPlotAreaBounds();
        if (!bounds) return;

        const chartRect = this.chartEl.getBoundingClientRect();
        const startX = this._startClientX - chartRect.left;
        const endX = clientX - chartRect.left;

        // Clamp to plot area
        const plotLeft = bounds.left;
        const plotRight = bounds.left + bounds.width;
        const clampedStart = Math.max(plotLeft, Math.min(plotRight, startX));
        const clampedEnd = Math.max(plotLeft, Math.min(plotRight, endX));

        const left = Math.min(clampedStart, clampedEnd);
        const width = Math.abs(clampedEnd - clampedStart);

        this.zoomRegion.style.left = left + 'px';
        this.zoomRegion.style.top = bounds.top + 'px';
        this.zoomRegion.style.width = width + 'px';
        this.zoomRegion.style.height = bounds.height + 'px';
        this.zoomRegion.classList.remove('hidden');
    },

    _applyZoom(clientX) {
        const data = this.dataSource();
        if (!data || data.length === 0) return;

        // Minimum drag distance to trigger zoom (prevents accidental right-clicks)
        if (Math.abs(clientX - this._startClientX) < 10) return;

        const startDate = this._pixelToDate(this._startClientX);
        const endDate = this._pixelToDate(clientX);

        // Ensure chronological order
        const [dateA, dateB] = startDate < endDate
            ? [startDate, endDate]
            : [endDate, startDate];

        // Format as ISO date strings for Plotly
        const fmt = (d) => d.toISOString().split('T')[0];

        // Apply zoom via Plotly.relayout (single API call)
        if (this.chartEl && typeof Plotly !== 'undefined') {
            Plotly.relayout(this.chartEl, {
                'xaxis.range': [fmt(dateA), fmt(dateB)]
            });
        }
    },

    // ========== PAN ==========

    _applyPan(deltaPixels) {
        if (!this.chartEl || typeof Plotly === 'undefined') return;

        const fullLayout = this.chartEl._fullLayout;
        if (!fullLayout) return;

        const xaxis = fullLayout.xaxis;
        const range = xaxis.range;
        const startMs = new Date(range[0]).getTime();
        const endMs = new Date(range[1]).getTime();
        const spanMs = endMs - startMs;

        const plotArea = this.chartEl.querySelector('.nsewdrag');
        if (!plotArea) return;
        const plotWidth = plotArea.getBoundingClientRect().width;

        // Convert pixel delta to time delta (negative because dragging right = earlier dates)
        const timeDelta = -(deltaPixels / plotWidth) * spanMs;

        const newStartMs = startMs + timeDelta;
        const newEndMs = endMs + timeDelta;

        // Don't pan past the last data point (no future dates)
        const data = this.dataSource();
        if (data && data.length > 0) {
            const dataEndMs = new Date(data[data.length - 1].date).getTime();
            if (newEndMs > dataEndMs + 86400000) return; // Allow 1 day buffer
        }

        const fmt = (ms) => new Date(ms).toISOString().split('T')[0];
        Plotly.relayout(this.chartEl, {
            'xaxis.range': [fmt(newStartMs), fmt(newEndMs)]
        });
    },

    // ========== SCROLL WHEEL ZOOM ==========

    _onWheel(e) {
        e.preventDefault();

        if (this.state !== 'idle' && this.state !== 'pinned') return;
        if (!this.chartEl) return;

        // Accumulate wheel delta — multiple rapid events get batched into one rAF
        this._wheelAccum += e.deltaY;

        // Update cursor position for zoom centering
        const plotArea = this.chartEl.querySelector('.nsewdrag');
        if (plotArea) {
            const plotRect = plotArea.getBoundingClientRect();
            this._wheelFraction = Math.max(0, Math.min(1, (e.clientX - plotRect.left) / plotRect.width));
        }

        // Schedule a single rAF to apply the accumulated zoom
        if (!this._wheelRafId) {
            this._wheelRafId = requestAnimationFrame(() => this._applyWheelZoom());
        }
    },

    _applyWheelZoom() {
        this._wheelRafId = null;

        if (!this.chartEl || typeof Plotly === 'undefined') {
            this._wheelAccum = 0;
            return;
        }

        const fullLayout = this.chartEl._fullLayout;
        if (!fullLayout) { this._wheelAccum = 0; return; }

        const xaxis = fullLayout.xaxis;
        const range = xaxis.range;
        const startMs = new Date(range[0]).getTime();
        const endMs = new Date(range[1]).getTime();
        const spanMs = endMs - startMs;

        // Convert accumulated delta to a zoom factor
        // Clamp to avoid extreme jumps from fast scroll wheels
        const clampedDelta = Math.max(-300, Math.min(300, this._wheelAccum));
        const zoomSpeed = 0.0005;
        const factor = Math.exp(clampedDelta * zoomSpeed);

        this._wheelAccum = 0;

        // Right boundary: never extend past the last data point (no future dates)
        const data = this.dataSource();
        if (!data || data.length === 0) return;
        const dataEndMs = new Date(data[data.length - 1].date).getTime();

        // New range centered on cursor position
        const fraction = this._wheelFraction;
        const centerMs = startMs + fraction * spanMs;
        const newSpanMs = spanMs * factor;

        // Minimum zoom: 5 trading days
        if (newSpanMs < 5 * 86400000) return;

        let newStartMs = centerMs - fraction * newSpanMs;
        let newEndMs = centerMs + (1 - fraction) * newSpanMs;

        // Clamp right edge to last data point — only historical extension allowed
        if (newEndMs > dataEndMs) {
            newEndMs = dataEndMs;
            newStartMs = newEndMs - newSpanMs;
        }

        const fmt = (ms) => new Date(ms).toISOString().split('T')[0];

        Plotly.relayout(this.chartEl, {
            'xaxis.range': [fmt(newStartMs), fmt(newEndMs)]
        });

        // Check if the new range extends beyond current data on the LEFT — fetch more history
        this._checkRangeExtension(newStartMs, newEndMs);
    },

    /**
     * If visible range extends past data bounds on the left (history), debounce a fetch.
     * Only extends backwards — right boundary is always clamped to last data point.
     * Waits 400ms after last scroll to avoid hammering the API during rapid scrolling.
     */
    _checkRangeExtension(visibleStartMs, visibleEndMs) {
        if (!this.onRangeExtend) return;

        const data = this.dataSource();
        if (!data || data.length === 0) return;

        const dataStartMs = new Date(data[0].date).getTime();
        const dataEndMs = new Date(data[data.length - 1].date).getTime();

        // Only trigger if the visible range extends BEFORE the earliest data point
        if (visibleStartMs >= dataStartMs) return;

        // Debounce — reset timer on each scroll event
        if (this._rangeExtendTimer) {
            clearTimeout(this._rangeExtendTimer);
        }

        const fmt = (ms) => new Date(ms).toISOString().split('T')[0];
        const fromDate = fmt(visibleStartMs);
        const toDate = fmt(dataEndMs); // Always cap at last data point, never future

        this._rangeExtendTimer = setTimeout(() => {
            this._rangeExtendTimer = null;
            this.onRangeExtend(fromDate, toDate);
        }, 400);
    },

    _onDblClick(e) {
        e.preventDefault();
        e.stopPropagation();

        if (!this.chartEl || typeof Plotly === 'undefined') return;

        // If pinned, dismiss measurement first
        if (this.state === 'pinned') {
            this._reset();
        }

        // Reset zoom to full data range
        Plotly.relayout(this.chartEl, {
            'xaxis.autorange': true
        });
    },

    // ========== COORDINATE CONVERSION ==========

    _pixelToDate(clientX) {
        const fullLayout = this.chartEl._fullLayout;
        if (!fullLayout) return new Date();

        const xaxis = fullLayout.xaxis;
        const plotArea = this.chartEl.querySelector('.nsewdrag');
        if (!plotArea) return new Date();

        const plotRect = plotArea.getBoundingClientRect();
        const plotX = clientX - plotRect.left;

        try {
            const fraction = plotX / plotRect.width;
            const range = xaxis.range;
            const startMs = new Date(range[0]).getTime();
            const endMs = new Date(range[1]).getTime();
            return new Date(startMs + fraction * (endMs - startMs));
        } catch {
            return new Date();
        }
    },

    _dateIndexToPixelX(dataIndex) {
        const data = this.dataSource();
        if (!data || !data[dataIndex]) return 0;

        const dateStr = data[dataIndex].date;
        const fullLayout = this.chartEl._fullLayout;
        if (!fullLayout) return 0;

        const xaxis = fullLayout.xaxis;
        const plotArea = this.chartEl.querySelector('.nsewdrag');
        if (!plotArea) return 0;

        const plotRect = plotArea.getBoundingClientRect();
        const chartRect = this.chartEl.getBoundingClientRect();

        const range = xaxis.range;
        const startMs = new Date(range[0]).getTime();
        const endMs = new Date(range[1]).getTime();
        const dateMs = new Date(dateStr).getTime();
        const fraction = (dateMs - startMs) / (endMs - startMs);

        return (plotRect.left - chartRect.left) + fraction * plotRect.width;
    },

    _findNearestDataPoint(targetDate, dataArray) {
        const targetMs = targetDate.getTime();
        let lo = 0, hi = dataArray.length - 1;

        while (lo < hi) {
            const mid = (lo + hi) >> 1;
            if (new Date(dataArray[mid].date).getTime() < targetMs) {
                lo = mid + 1;
            } else {
                hi = mid;
            }
        }

        if (lo > 0) {
            const diffLo = Math.abs(new Date(dataArray[lo].date).getTime() - targetMs);
            const diffPrev = Math.abs(new Date(dataArray[lo - 1].date).getTime() - targetMs);
            if (diffPrev < diffLo) return lo - 1;
        }
        return lo;
    },

    // ========== VISUAL UPDATES (MEASURE) ==========

    _getPlotAreaBounds() {
        const plotArea = this.chartEl.querySelector('.nsewdrag');
        if (!plotArea) return null;
        const plotRect = plotArea.getBoundingClientRect();
        const chartRect = this.chartEl.getBoundingClientRect();
        return {
            top: plotRect.top - chartRect.top,
            left: plotRect.left - chartRect.left,
            width: plotRect.width,
            height: plotRect.height
        };
    },

    _positionVerticalLine(lineEl, dataIndex) {
        const x = this._dateIndexToPixelX(dataIndex);
        const bounds = this._getPlotAreaBounds();
        if (!bounds) return;

        lineEl.style.left = x + 'px';
        lineEl.style.top = bounds.top + 'px';
        lineEl.style.height = bounds.height + 'px';
        lineEl.classList.remove('hidden');
    },

    _updateMeasureVisuals(clientX) {
        const data = this.dataSource();
        if (!data || data.length === 0) return;

        const currentDate = this._pixelToDate(clientX);
        this.endDataIndex = this._findNearestDataPoint(currentDate, data);

        // Position end line
        this._positionVerticalLine(this.endLine, this.endDataIndex);

        // Position shaded region
        const startX = this._dateIndexToPixelX(this.startDataIndex);
        const endX = this._dateIndexToPixelX(this.endDataIndex);
        const left = Math.min(startX, endX);
        const width = Math.abs(endX - startX);
        const bounds = this._getPlotAreaBounds();
        if (!bounds) return;

        this.shadedRegion.style.left = left + 'px';
        this.shadedRegion.style.top = bounds.top + 'px';
        this.shadedRegion.style.width = width + 'px';
        this.shadedRegion.style.height = bounds.height + 'px';
        this.shadedRegion.classList.remove('hidden');
    },

    _updateBubble(clientX, clientY) {
        const data = this.dataSource();
        if (!data || data.length === 0) return;

        const [firstIdx, secondIdx] = this.startDataIndex <= this.endDataIndex
            ? [this.startDataIndex, this.endDataIndex]
            : [this.endDataIndex, this.startDataIndex];

        const startPoint = data[firstIdx];
        const endPoint = data[secondIdx];

        const isComparison = this.isComparisonMode();
        this.bubble.innerHTML = isComparison
            ? this._buildComparisonBubbleHTML(startPoint, endPoint)
            : this._buildSingleBubbleHTML(startPoint, endPoint);

        this.bubble.classList.remove('hidden');
        this._positionBubble(clientX, clientY);
    },

    // ========== BUBBLE CONTENT ==========

    _formatDate(dateStr) {
        // Handle both "2026-01-15" and "2026-01-15T00:00:00" formats
        const dateOnly = dateStr.includes('T') ? dateStr.split('T')[0] : dateStr;
        const dt = new Date(dateOnly + 'T12:00:00');
        return dt.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
    },

    _buildSingleBubbleHTML(startPoint, endPoint) {
        if (this.dataType === 'percent') {
            return this._buildPercentBubbleHTML(startPoint, endPoint);
        }

        const startPrice = startPoint.close;
        const endPrice = endPoint.close;
        const dollarChange = endPrice - startPrice;
        const pctChange = ((endPrice - startPrice) / startPrice) * 100;
        const isPositive = pctChange >= 0;
        const colorClass = isPositive ? 'dm-positive' : 'dm-negative';
        const arrow = isPositive ? '&#9650;' : '&#9660;';

        return `
            <div class="dm-dates">
                ${this._formatDate(startPoint.date)} &rarr; ${this._formatDate(endPoint.date)}
            </div>
            <div class="dm-main-row">
                <span class="dm-pct ${colorClass}">
                    ${arrow} ${pctChange >= 0 ? '+' : ''}${pctChange.toFixed(2)}%
                </span>
                <span class="dm-dollar">
                    ${dollarChange >= 0 ? '+' : ''}$${Math.abs(dollarChange).toFixed(2)}
                </span>
            </div>
            <div class="dm-prices">
                $${startPrice.toFixed(2)} &rarr; $${endPrice.toFixed(2)}
            </div>
        `;
    },

    _buildPercentBubbleHTML(startPoint, endPoint) {
        const startPct = startPoint.percentChange;
        const endPct = endPoint.percentChange;
        const diff = endPct - startPct;
        const isPositive = diff >= 0;
        const colorClass = isPositive ? 'dm-positive' : 'dm-negative';
        const arrow = isPositive ? '&#9650;' : '&#9660;';

        return `
            <div class="dm-dates">
                ${this._formatDate(startPoint.date)} &rarr; ${this._formatDate(endPoint.date)}
            </div>
            <div class="dm-main-row">
                <span class="dm-pct ${colorClass}">
                    ${arrow} ${diff >= 0 ? '+' : ''}${diff.toFixed(2)}%
                </span>
            </div>
            <div class="dm-prices">
                ${startPct >= 0 ? '+' : ''}${startPct.toFixed(2)}% &rarr; ${endPct >= 0 ? '+' : ''}${endPct.toFixed(2)}%
            </div>
        `;
    },

    _buildComparisonBubbleHTML(startPoint, endPoint) {
        const isPercent = this.dataType === 'percent';
        const primaryPct = isPercent
            ? endPoint.percentChange - startPoint.percentChange
            : ((endPoint.close - startPoint.close) / startPoint.close) * 100;
        const primaryPositive = primaryPct >= 0;
        const primaryColor = primaryPositive ? 'dm-positive' : 'dm-negative';
        const primaryArrow = primaryPositive ? '&#9650;' : '&#9660;';

        let compRow = '';
        const compData = this.getComparisonData();
        if (compData && compData.data) {
            const compArray = compData.data;
            const compStart = compArray.find(d => d.date === startPoint.date);
            const compEnd = compArray.find(d => d.date === endPoint.date);

            if (compStart && compEnd) {
                const compPct = isPercent
                    ? compEnd.percentChange - compStart.percentChange
                    : ((compEnd.close - compStart.close) / compStart.close) * 100;
                const compPositive = compPct >= 0;
                const compColor = compPositive ? 'dm-positive' : 'dm-negative';
                const compArrow = compPositive ? '&#9650;' : '&#9660;';

                compRow = `
                    <div class="dm-comp-row">
                        <span class="dm-comp-label">${this.getComparisonSymbol()}</span>
                        <span class="dm-pct ${compColor}">
                            ${compArrow} ${compPct >= 0 ? '+' : ''}${compPct.toFixed(2)}%
                        </span>
                    </div>
                `;
            }
        }

        return `
            <div class="dm-dates">
                ${this._formatDate(startPoint.date)} &rarr; ${this._formatDate(endPoint.date)}
            </div>
            <div class="dm-comp-row">
                <span class="dm-primary-label">${this.getPrimarySymbol()}</span>
                <span class="dm-pct ${primaryColor}">
                    ${primaryArrow} ${primaryPct >= 0 ? '+' : ''}${primaryPct.toFixed(2)}%
                </span>
            </div>
            ${compRow}
        `;
    },

    _positionBubble(clientX, clientY) {
        const chartRect = this.chartEl.getBoundingClientRect();
        const padding = 15;

        let left = clientX - chartRect.left + padding;
        let top = clientY - chartRect.top - 70;

        const bubbleRect = this.bubble.getBoundingClientRect();
        const bw = bubbleRect.width || 200;
        const bh = bubbleRect.height || 80;

        if (left + bw > chartRect.width - padding) {
            left = clientX - chartRect.left - bw - padding;
        }
        if (top < padding) top = padding;
        if (top + bh > chartRect.height - padding) {
            top = chartRect.height - bh - padding;
        }

        this.bubble.style.left = left + 'px';
        this.bubble.style.top = top + 'px';
    },

    // ========== PHASE 2 DATA HOOK ==========

    _getPinnedData() {
        const data = this.dataSource();
        const [firstIdx, secondIdx] = this.startDataIndex <= this.endDataIndex
            ? [this.startDataIndex, this.endDataIndex]
            : [this.endDataIndex, this.startDataIndex];

        const startPoint = data[firstIdx];
        const endPoint = data[secondIdx];

        if (this.dataType === 'percent') {
            return {
                symbol: this.getPrimarySymbol(),
                dataType: 'percent',
                startDate: startPoint.date,
                endDate: endPoint.date,
                startPercent: startPoint.percentChange,
                endPercent: endPoint.percentChange,
                pctChange: endPoint.percentChange - startPoint.percentChange,
                comparisonSymbol: this.isComparisonMode() ? this.getComparisonSymbol() : null
            };
        }

        const pctChange = ((endPoint.close - startPoint.close) / startPoint.close) * 100;
        return {
            symbol: this.getPrimarySymbol(),
            dataType: 'price',
            startDate: startPoint.date,
            endDate: endPoint.date,
            startPrice: startPoint.close,
            endPrice: endPoint.close,
            dollarChange: endPoint.close - startPoint.close,
            pctChange: pctChange,
            comparisonSymbol: this.isComparisonMode() ? this.getComparisonSymbol() : null
        };
    },

    // ========== DOM MANAGEMENT ==========

    _createOverlayDOM() {
        const pos = window.getComputedStyle(this.chartEl).position;
        if (pos === 'static') {
            this.chartEl.style.position = 'relative';
        }

        this.overlayEl = document.createElement('div');
        this.overlayEl.id = 'drag-measure-overlay';
        this.overlayEl.style.pointerEvents = 'none';

        // Measure: blue shaded region
        this.shadedRegion = document.createElement('div');
        this.shadedRegion.id = 'drag-measure-region';
        this.shadedRegion.className = 'hidden';
        this.overlayEl.appendChild(this.shadedRegion);

        // Measure: start/end vertical lines
        this.startLine = document.createElement('div');
        this.startLine.id = 'drag-measure-start-line';
        this.startLine.className = 'hidden';
        this.overlayEl.appendChild(this.startLine);

        this.endLine = document.createElement('div');
        this.endLine.id = 'drag-measure-end-line';
        this.endLine.className = 'hidden';
        this.overlayEl.appendChild(this.endLine);

        // Measure: floating bubble
        this.bubble = document.createElement('div');
        this.bubble.id = 'drag-measure-bubble';
        this.bubble.className = 'hidden';
        this.overlayEl.appendChild(this.bubble);

        // Zoom: orange/amber selection region
        this.zoomRegion = document.createElement('div');
        this.zoomRegion.id = 'drag-zoom-region';
        this.zoomRegion.className = 'hidden';
        this.overlayEl.appendChild(this.zoomRegion);

        this.chartEl.appendChild(this.overlayEl);
    },

    _destroyOverlayDOM() {
        if (this.overlayEl && this.overlayEl.parentNode) {
            this.overlayEl.parentNode.removeChild(this.overlayEl);
        }
        this.overlayEl = null;
        this.shadedRegion = null;
        this.startLine = null;
        this.endLine = null;
        this.bubble = null;
        this.zoomRegion = null;
    },

    _reset() {
        this.state = 'idle';
        this.startDataIndex = null;
        this.endDataIndex = null;
        this._rafPending = false;
        this._startClientX = null;
        this._wheelAccum = 0;
        if (this._wheelRafId) {
            cancelAnimationFrame(this._wheelRafId);
            this._wheelRafId = null;
        }

        if (this.overlayEl) {
            this.overlayEl.style.pointerEvents = 'none';
        }
        if (this.shadedRegion) this.shadedRegion.classList.add('hidden');
        if (this.startLine) this.startLine.classList.add('hidden');
        if (this.endLine) this.endLine.classList.add('hidden');
        if (this.bubble) this.bubble.classList.add('hidden');
        if (this.zoomRegion) this.zoomRegion.classList.add('hidden');

        document.removeEventListener('click', this._boundDocClick);
        document.removeEventListener('mousemove', this._boundMouseMove);
        document.removeEventListener('mouseup', this._boundMouseUp);
    }
};
