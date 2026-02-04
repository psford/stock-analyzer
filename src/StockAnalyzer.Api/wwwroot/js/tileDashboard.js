/**
 * TileDashboard — GridStack.js v12 tile dashboard integration
 *
 * Adds draggable/resizable tiles with physics animations, layout persistence,
 * and panel management to the Stock Analyzer results section.
 *
 * ZERO modifications to existing JS files (app.js, charts.js, etc.).
 * Initialization: MutationObserver watches #results-section for class
 * changes. When `hidden` is removed (by App.showResults()), GridStack.init() fires.
 */
const TileDashboard = (() => {
    'use strict';

    // ========== CONSTANTS ==========

    const TILE_IDS = [
        'tile-chart', 'tile-watchlist', 'tile-info', 'tile-metrics',
        'tile-performance', 'tile-moves', 'tile-news'
    ];

    const TILE_NAMES = {
        'tile-chart': 'Stock Chart',
        'tile-watchlist': 'My Watchlists',
        'tile-info': 'Company Info',
        'tile-metrics': 'Key Metrics',
        'tile-performance': 'Performance',
        'tile-moves': 'Significant Moves',
        'tile-news': 'Recent News'
    };

    const STORAGE_KEY = 'stockanalyzer_tile_layout';
    const LAYOUT_VERSION = 8; // Bumped: watchlist tile h=3, metrics repositioned

    // ========== STATE ==========

    let grid = null;
    let initialized = false;
    let tileVisibility = {};
    let chartResizeObserver = null;
    const tileContentCache = {};
    const tileGridOpts = {};
    const tileExpansions = {}; // tracks which neighbor expanded when a tile was closed

    // ========== INITIALIZATION ==========

    /**
     * Boot: called on DOMContentLoaded.
     * Sets up MutationObserver on #results-section.
     * GridStack.init() is deferred until the section becomes visible.
     */
    function boot() {
        const resultsSection = document.getElementById('results-section');
        if (!resultsSection) return;

        // Watch for class attribute changes (hidden being removed by App.showResults)
        const observer = new MutationObserver((mutations) => {
            for (const mut of mutations) {
                if (mut.type === 'attributes' && mut.attributeName === 'class') {
                    if (!resultsSection.classList.contains('hidden') && !initialized) {
                        // Results just became visible — init GridStack
                        requestAnimationFrame(() => {
                            initGridStack();
                        });
                    }
                }
            }
        });

        observer.observe(resultsSection, {
            attributes: true,
            attributeFilter: ['class']
        });
    }

    /**
     * Initialize GridStack on the visible #tile-grid.
     * Caches tile content for reopen, inits Physics, restores saved layout.
     */
    function initGridStack() {
        if (initialized) return;
        initialized = true;

        // Cache tile content and grid options BEFORE GridStack modifies DOM
        TILE_IDS.forEach(id => {
            const el = document.querySelector(`[gs-id="${id}"]`);
            if (el) {
                const contentDiv = el.querySelector('.grid-stack-item-content');
                tileContentCache[id] = contentDiv ? contentDiv.innerHTML : '';
                tileGridOpts[id] = {
                    x: parseInt(el.getAttribute('gs-x')) || 0,
                    y: parseInt(el.getAttribute('gs-y')) || 0,
                    w: parseInt(el.getAttribute('gs-w')) || 6,
                    h: parseInt(el.getAttribute('gs-h')) || 3,
                    minW: parseInt(el.getAttribute('gs-min-w')) || 2,
                    minH: parseInt(el.getAttribute('gs-min-h')) || 2,
                };
            }
        });

        // Initialize visibility
        TILE_IDS.forEach(id => { tileVisibility[id] = true; });

        // Init GridStack (discovers static HTML items)
        grid = GridStack.init({
            column: 12,
            cellHeight: 70,
            margin: 12,
            animate: true,
            float: false,
            handle: '.tile-header',
            resizable: { handles: 'n, e, se, s, sw, w' },
            columnOpts: {
                breakpointForWindow: true,
                breakpoints: [{ w: 768, c: 1 }]
            }
        }, '#tile-grid');

        // Restore saved layout if exists (only if version matches)
        restoreLayout();

        // Save layout on any change
        grid.on('change', saveLayout);

        // Close button delegation
        document.getElementById('tile-grid').addEventListener('click', (e) => {
            const btn = e.target.closest('.tile-close');
            if (btn) {
                e.stopPropagation();
                closeTile(btn.dataset.tileId);
            }
        });

        // Init sub-systems
        Physics.init(grid);
        setupPanelDropdown();
        updatePanelDropdown();
        initChartResize();
        setupWatchlistToggle();
    }

    // ========== CHART RESIZE ==========

    /**
     * Sets up ResizeObserver on #tile-chart-body to call
     * Plotly.Plots.resize() when tile dimensions change.
     * CSS !important rule makes #stock-chart fill tile regardless of app.js pixel height.
     */
    function initChartResize() {
        if (chartResizeObserver) chartResizeObserver.disconnect();

        const chartEl = document.getElementById('stock-chart');
        const tileBody = document.getElementById('tile-chart-body');
        if (!tileBody || !chartEl) return;

        chartResizeObserver = new ResizeObserver(() => {
            // Only resize if Plotly chart is rendered
            if (chartEl.data) {
                Plotly.Plots.resize(chartEl);
            }
        });
        chartResizeObserver.observe(tileBody);
    }

    // ========== LAYOUT PERSISTENCE ==========

    function saveLayout() {
        if (!grid) return;
        const items = grid.getGridItems().map(el => {
            const node = el.gridstackNode;
            return { id: node.id, x: node.x, y: node.y, w: node.w, h: node.h };
        });
        localStorage.setItem(STORAGE_KEY, JSON.stringify({
            version: LAYOUT_VERSION, tiles: items, visibility: { ...tileVisibility }
        }));
    }

    function loadSavedLayout() {
        try {
            const raw = localStorage.getItem(STORAGE_KEY);
            if (!raw) return null;
            const parsed = JSON.parse(raw);
            if (parsed.version !== LAYOUT_VERSION) {
                localStorage.removeItem(STORAGE_KEY);
                return null;
            }
            return parsed;
        } catch { return null; }
    }

    function restoreLayout() {
        const saved = loadSavedLayout();
        if (!saved) return;

        // Enable float to apply saved positions without collision resolution
        grid.float(true);
        grid.batchUpdate();
        saved.tiles.forEach(t => {
            const el = document.querySelector(`[gs-id="${t.id}"]`);
            if (el) grid.update(el, { x: t.x, y: t.y, w: t.w, h: t.h });
        });
        grid.batchUpdate(false);
        grid.float(false);

        // Remove tiles that were hidden
        if (saved.visibility) {
            TILE_IDS.forEach(id => {
                if (saved.visibility[id] === false) {
                    const el = document.querySelector(`[gs-id="${id}"]`);
                    if (el) grid.removeWidget(el, true, false);
                    tileVisibility[id] = false;
                }
            });
        }
    }

    function resetLayout() {
        localStorage.removeItem(STORAGE_KEY);

        // Snapshot all search form state so the reload restores data, not just layout
        const formState = {};
        const textIds = ['ticker-input', 'compare-input'];
        const selectIds = ['chart-type', 'end-date-preset', 'start-date-preset'];
        const checkIds = ['ma-20', 'ma-50', 'ma-200', 'show-rsi', 'show-macd',
                          'show-bollinger', 'show-stochastic', 'show-markers'];
        const rangeIds = ['threshold-slider'];

        textIds.forEach(id => {
            const el = document.getElementById(id);
            if (el) formState[id] = el.value;
        });
        selectIds.forEach(id => {
            const el = document.getElementById(id);
            if (el) formState[id] = el.value;
        });
        checkIds.forEach(id => {
            const el = document.getElementById(id);
            if (el) formState[id] = el.checked;
        });
        rangeIds.forEach(id => {
            const el = document.getElementById(id);
            if (el) formState[id] = el.value;
        });

        // Animal radio
        const animalRadio = document.querySelector('input[name="animal-type"]:checked');
        if (animalRadio) formState['animal-type'] = animalRadio.value;

        sessionStorage.setItem('stockanalyzer_reset_form', JSON.stringify(formState));
        location.reload();
    }

    // ========== CLOSE / REOPEN ==========

    function closeTile(tileId) {
        const el = document.querySelector(`[gs-id="${tileId}"]`);
        if (!el || !grid) return;

        // Before removing: expand row neighbor(s) to fill the gap horizontally
        const node = el.gridstackNode;
        if (node) {
            expandRowNeighbor(tileId, node);
        }

        if (tileId === 'tile-chart' && chartResizeObserver) {
            chartResizeObserver.disconnect();
            chartResizeObserver = null;
        }
        grid.removeWidget(el, true, false);
        tileVisibility[tileId] = false;
        updatePanelDropdown();
        saveLayout();

        // Sync watchlist toggle button
        if (tileId === 'tile-watchlist') {
            const toggleBtn = document.getElementById('watchlist-toggle-btn');
            if (toggleBtn) toggleBtn.classList.remove('watchlist-toggle-active');
        }
    }

    /**
     * When a tile is closed, find its left or right neighbor on the same row
     * and expand it to fill the gap. Stores the expansion so reopenTile can reverse it.
     */
    function expandRowNeighbor(closingId, closingNode) {
        const items = grid.getGridItems();
        let expanded = false;

        for (const item of items) {
            const n = item.gridstackNode;
            if (!n || n.id === closingId) continue;
            // Must share at least one row
            if (n.y >= closingNode.y + closingNode.h || n.y + n.h <= closingNode.y) continue;

            // Left neighbor: ends where closing tile starts
            if (n.x + n.w === closingNode.x) {
                tileExpansions[closingId] = { neighborId: n.id, origW: n.w, origX: n.x };
                grid.update(item, { w: n.w + closingNode.w });
                expanded = true;
                break;
            }
            // Right neighbor: starts where closing tile ends
            if (n.x === closingNode.x + closingNode.w) {
                tileExpansions[closingId] = { neighborId: n.id, origW: n.w, origX: n.x };
                grid.update(item, { x: closingNode.x, w: n.w + closingNode.w });
                expanded = true;
                break;
            }
        }
        if (!expanded) delete tileExpansions[closingId];
    }

    function reopenTile(tileId) {
        const content = tileContentCache[tileId];
        const opts = tileGridOpts[tileId];
        if (content === undefined || !opts || !grid) return;

        // Shrink the neighbor that was expanded when this tile was closed
        const expansion = tileExpansions[tileId];
        if (expansion) {
            const neighborEl = document.querySelector(`[gs-id="${expansion.neighborId}"]`);
            if (neighborEl) {
                grid.update(neighborEl, { x: expansion.origX, w: expansion.origW });
            }
            delete tileExpansions[tileId];
        }

        // Build a fresh grid-stack-item element
        const wrapper = document.createElement('div');
        wrapper.classList.add('grid-stack-item');
        wrapper.setAttribute('gs-id', tileId);
        wrapper.setAttribute('gs-min-w', opts.minW);
        wrapper.setAttribute('gs-min-h', opts.minH);

        const contentDiv = document.createElement('div');
        contentDiv.classList.add('grid-stack-item-content');
        contentDiv.innerHTML = content;
        wrapper.appendChild(contentDiv);

        // Place at original position if we just freed space, else auto-position
        const addOpts = expansion
            ? { x: opts.x, y: opts.y, w: opts.w, h: opts.h }
            : { w: opts.w, h: opts.h, autoPosition: true };
        grid.addWidget(wrapper, addOpts);
        tileVisibility[tileId] = true;
        updatePanelDropdown();
        saveLayout();

        // Re-init chart ResizeObserver if needed
        if (tileId === 'tile-chart') {
            setTimeout(initChartResize, 200);
        }

        // Re-init watchlist content and sync toggle button
        if (tileId === 'tile-watchlist') {
            const toggleBtn = document.getElementById('watchlist-toggle-btn');
            if (toggleBtn) toggleBtn.classList.add('watchlist-toggle-active');
            setTimeout(() => {
                if (typeof Watchlist !== 'undefined') {
                    Watchlist.loadWatchlists();
                }
            }, 200);
        }
    }

    // ========== WATCHLIST TOGGLE ==========

    function setupWatchlistToggle() {
        const btn = document.getElementById('watchlist-toggle-btn');
        if (!btn) return;

        btn.addEventListener('click', () => {
            if (tileVisibility['tile-watchlist']) {
                closeTile('tile-watchlist');
            } else {
                reopenTile('tile-watchlist');
            }
        });

        // Set initial state
        if (tileVisibility['tile-watchlist']) {
            btn.classList.add('watchlist-toggle-active');
        }
    }

    // ========== PANEL DROPDOWN ==========

    function setupPanelDropdown() {
        const btn = document.getElementById('panel-toggle-btn');
        const dropdown = document.getElementById('panel-dropdown');
        if (!btn || !dropdown) return;
        btn.addEventListener('click', (e) => { e.stopPropagation(); dropdown.classList.toggle('open'); });
        document.addEventListener('click', () => dropdown.classList.remove('open'));
        dropdown.addEventListener('click', (e) => e.stopPropagation());
    }

    function updatePanelDropdown() {
        const dropdown = document.getElementById('panel-dropdown');
        if (!dropdown) return;
        dropdown.innerHTML = TILE_IDS.map(id => `
            <label>
                <input type="checkbox" data-tile-id="${id}" ${tileVisibility[id] ? 'checked' : ''}>
                ${TILE_NAMES[id]}
            </label>
        `).join('') +
        '<div class="panel-dropdown-divider"></div>' +
        '<button class="reset-btn" id="reset-layout-btn">Reset Layout</button>';

        dropdown.querySelectorAll('input[type="checkbox"]').forEach(cb => {
            cb.addEventListener('change', (e) => {
                if (e.target.checked) reopenTile(e.target.dataset.tileId);
                else closeTile(e.target.dataset.tileId);
            });
        });
        const resetBtn = document.getElementById('reset-layout-btn');
        if (resetBtn) resetBtn.addEventListener('click', resetLayout);
    }

    // ========== PHYSICS ENGINE ==========

    const Physics = {
        // Configuration
        MAGNETIC_THRESHOLD: 50,   // px — distance within which magnetic pull activates
        MAGNETIC_STRENGTH: 0.35,  // 0-1 — how much to pull toward snap target
        SETTLE_MS: 400,           // ms — snap settle animation duration

        // State
        _grid: null,
        _audioCtx: null,
        _audioEnabled: false,
        _lockedTiles: new Set(),
        _lastDragPos: null,
        _lastDragTime: null,
        _velocity: { x: 0, y: 0 },
        _coupledResize: null,

        init(gridInstance) {
            this._grid = gridInstance;
            this._attachGridEvents();
            this._attachLockEvents();
            this._attachAudioToggle();
        },

        // ---------- GRID EVENTS ----------

        _attachGridEvents() {
            const g = this._grid;
            g.on('dragstart', (event, el) => this._onDragStart(el));
            g.on('drag', (event, el) => this._onDrag(el));
            g.on('dragstop', (event, el) => this._onDragStop(el));
            g.on('resizestart', (event, el) => this._onResizeStart(el));
            g.on('resize', (event, el) => this._onResize(el));
            g.on('resizestop', (event, el) => this._onResizeStop(el));
        },

        _onDragStart(el) {
            // Lift effect
            el.classList.add('tile-dragging');
            el.style.willChange = 'transform';

            // Glow grid dots to show snap points
            this._grid.el.classList.add('drag-active');

            // Cache placeholder reference for duration of drag
            this._placeholder = this._grid.el.querySelector('.grid-stack-placeholder');

            // Reset velocity tracking
            this._lastDragPos = null;
            this._lastDragTime = null;
            this._velocity = { x: 0, y: 0 };
            this._dropRect = null;

            // Capture exact element position at pointerup — fires BEFORE GridStack cleanup
            const captureDropPos = () => {
                this._dropRect = el.getBoundingClientRect();
                document.removeEventListener('pointerup', captureDropPos, true);
                document.removeEventListener('touchend', captureDropPos, true);
            };
            this._cleanupDropCapture = () => {
                document.removeEventListener('pointerup', captureDropPos, true);
                document.removeEventListener('touchend', captureDropPos, true);
            };
            document.addEventListener('pointerup', captureDropPos, true);
            document.addEventListener('touchend', captureDropPos, true);

            // MutationObserver FLIP: watch gs-x attribute changes on neighbors
            // GridStack v12 uses generated stylesheet rules like [gs-x="3"]{left:25%}
            // CSS transitions don't fire across attribute-selector rule changes,
            // so we use WAAPI FLIP animations triggered by MutationObserver instead.
            this._flipDraggedEl = el;
            this._flipObserver = new MutationObserver((mutations) => {
                const cols = this._grid.opts.column || 12;
                const colWidth = this._grid.el.offsetWidth / cols;
                for (const mut of mutations) {
                    if (mut.attributeName !== 'gs-x') continue;
                    const tileEl = mut.target;
                    if (tileEl === el || !tileEl.classList.contains('grid-stack-item')) continue;
                    const oldX = parseInt(mut.oldValue) || 0;
                    const newX = parseInt(tileEl.getAttribute('gs-x')) || 0;
                    if (oldX === newX) continue;
                    const dx = (oldX - newX) * colWidth;
                    if (Math.abs(dx) > 1) {
                        // Cancel any in-progress FLIP and accumulate offset
                        let currentOffset = 0;
                        if (tileEl._flipAnim && tileEl._flipAnim.playState === 'running') {
                            try {
                                const ct = getComputedStyle(tileEl).transform;
                                if (ct && ct !== 'none') currentOffset = new DOMMatrix(ct).m41;
                            } catch(e) {}
                            tileEl._flipAnim.cancel();
                        }
                        const totalDx = dx + currentOffset;
                        if (Math.abs(totalDx) > 1) {
                            tileEl._flipAnim = tileEl.animate([
                                { transform: `translateX(${totalDx}px)` },
                                { transform: 'translateX(0)' }
                            ], {
                                duration: 380,
                                easing: 'cubic-bezier(0.25, 1.1, 0.5, 1)',
                                fill: 'none'
                            });
                            tileEl._flipAnim.onfinish = () => { tileEl._flipAnim = null; };
                        }
                    }
                }
            });
            this._flipObserver.observe(this._grid.el, {
                subtree: true,
                attributes: true,
                attributeFilter: ['gs-x'],
                attributeOldValue: true
            });

            // Haptic feedback on mobile
            if (navigator.vibrate) navigator.vibrate(10);
        },

        _onDrag(el) {
            // Track velocity for inertia feel
            const now = performance.now();
            const rect = el.getBoundingClientRect();
            const pos = { x: rect.left, y: rect.top };

            if (this._lastDragPos && this._lastDragTime) {
                const dt = (now - this._lastDragTime) / 1000;
                if (dt > 0.005) {
                    const alpha = 0.3;
                    this._velocity.x = alpha * ((pos.x - this._lastDragPos.x) / dt) + (1 - alpha) * this._velocity.x;
                    this._velocity.y = alpha * ((pos.y - this._lastDragPos.y) / dt) + (1 - alpha) * this._velocity.y;
                }
            }

            this._lastDragPos = pos;
            this._lastDragTime = now;

            // Magnetic attraction toward placeholder
            this._applyMagneticPull(el);
        },

        _applyMagneticPull(el) {
            // Use cached placeholder (refreshed on dragstart)
            if (!this._placeholder || !this._placeholder.isConnected) {
                this._placeholder = this._grid.el.querySelector('.grid-stack-placeholder');
            }
            if (!this._placeholder) {
                el.style.transform = 'scale(1.025)';
                return;
            }

            const elRect = el.getBoundingClientRect();
            const phRect = this._placeholder.getBoundingClientRect();

            // Vector from dragged tile center to placeholder center
            const dx = (phRect.left + phRect.width / 2) - (elRect.left + elRect.width / 2);
            const dy = (phRect.top + phRect.height / 2) - (elRect.top + elRect.height / 2);
            const dist = Math.sqrt(dx * dx + dy * dy);

            if (dist < this.MAGNETIC_THRESHOLD && dist > 2) {
                // Quadratic ease — stronger pull as you get closer
                const t = 1 - (dist / this.MAGNETIC_THRESHOLD);
                const strength = t * t * this.MAGNETIC_STRENGTH;
                const pullX = dx * strength;
                const pullY = dy * strength;
                el.style.transform = `translate(${pullX}px, ${pullY}px) scale(1.025)`;
            } else {
                el.style.transform = 'scale(1.025)';
            }
        },

        _onDragStop(el) {
            // Use pointerup-captured rect (before GridStack cleanup), fall back to last drag pos
            const dropRect = this._dropRect;
            if (this._cleanupDropCapture) {
                this._cleanupDropCapture();
                this._cleanupDropCapture = null;
            }
            const dropPos = dropRect
                ? { x: dropRect.left, y: dropRect.top }
                : (this._lastDragPos ? { ...this._lastDragPos } : null);

            // Clean up drag state
            el.classList.remove('tile-dragging');
            el.style.willChange = '';
            el.style.transform = '';
            this._grid.el.classList.remove('drag-active');
            this._placeholder = null;

            // Clean up FLIP MutationObserver
            if (this._flipObserver) {
                this._flipObserver.disconnect();
                this._flipObserver = null;
            }
            this._flipDraggedEl = null;

            // Suppress left/top transition during fixed→absolute position switch
            el.classList.add('tile-just-dropped');

            const card = el.querySelector('.tile-card');

            if (dropPos && card) {
                // GridStack has already repositioned — get snap position
                const snapRect = el.getBoundingClientRect();
                const dx = dropPos.x - snapRect.left;
                const dy = dropPos.y - snapRect.top;

                if (Math.abs(dx) > 2 || Math.abs(dy) > 2) {
                    // Disable CSS snapSettle animation — JS drives this
                    card.style.animation = 'none';
                    // Place card visually at the drop position (offset from snap)
                    card.style.transition = 'none';
                    card.style.transform = `translate(${dx}px, ${dy}px) scale(1.015)`;
                    card.offsetHeight; // force reflow

                    // Animate card sliding into snap position
                    card.style.transition = 'transform 0.35s cubic-bezier(0.25, 1.1, 0.5, 1)';
                    card.style.transform = 'translate(0, 0) scale(1)';

                    const cleanup = () => {
                        card.style.transition = '';
                        card.style.transform = '';
                        card.style.animation = '';
                        el.classList.remove('tile-just-dropped');
                    };
                    card.addEventListener('transitionend', cleanup, { once: true });
                    setTimeout(cleanup, 400); // fallback if transitionend doesn't fire
                } else {
                    // Tile barely moved — just do the quick scale settle
                    setTimeout(() => el.classList.remove('tile-just-dropped'), this.SETTLE_MS);
                }
            } else {
                setTimeout(() => el.classList.remove('tile-just-dropped'), this.SETTLE_MS);
            }

            // Snap sound
            if (this._audioEnabled) this._playSnap();

            // Haptic feedback on mobile
            if (navigator.vibrate) navigator.vibrate([5, 30, 5]);
        },

        _onResizeStart(el) {
            el.style.willChange = 'width, height';
            this._grid.el.classList.add('drag-active');

            // MutationObserver FLIP for neighbor horizontal animation during resize
            this._flipDraggedEl = el;
            this._flipObserver = new MutationObserver((mutations) => {
                const cols = this._grid.opts.column || 12;
                const colWidth = this._grid.el.offsetWidth / cols;
                for (const mut of mutations) {
                    if (mut.attributeName !== 'gs-x') continue;
                    const tileEl = mut.target;
                    if (tileEl === el || !tileEl.classList.contains('grid-stack-item')) continue;
                    const oldX = parseInt(mut.oldValue) || 0;
                    const newX = parseInt(tileEl.getAttribute('gs-x')) || 0;
                    if (oldX === newX) continue;
                    const dx = (oldX - newX) * colWidth;
                    if (Math.abs(dx) > 1) {
                        let currentOffset = 0;
                        if (tileEl._flipAnim && tileEl._flipAnim.playState === 'running') {
                            try {
                                const ct = getComputedStyle(tileEl).transform;
                                if (ct && ct !== 'none') currentOffset = new DOMMatrix(ct).m41;
                            } catch(e) {}
                            tileEl._flipAnim.cancel();
                        }
                        const totalDx = dx + currentOffset;
                        if (Math.abs(totalDx) > 1) {
                            tileEl._flipAnim = tileEl.animate([
                                { transform: `translateX(${totalDx}px)` },
                                { transform: 'translateX(0)' }
                            ], {
                                duration: 380,
                                easing: 'cubic-bezier(0.25, 1.1, 0.5, 1)',
                                fill: 'none'
                            });
                            tileEl._flipAnim.onfinish = () => { tileEl._flipAnim = null; };
                        }
                    }
                }
            });
            this._flipObserver.observe(this._grid.el, {
                subtree: true,
                attributes: true,
                attributeFilter: ['gs-x'],
                attributeOldValue: true
            });

            // --- Coupled resize: adjacent tiles shrink/grow instead of being pushed ---
            // Strategy: temporarily remove the neighbor from GridStack's engine so
            // the engine sees empty space and allows the resize. We position the
            // neighbor manually via DOM attributes during resize, then re-add it
            // to the engine on resizestop.
            const node = el.gridstackNode;
            if (node) {
                const { right, left } = this._findRowNeighbors(node);
                if (right || left) {
                    // Compute maxW: tile can grow until the neighbor hits its minW
                    let maxW = 12;
                    if (right) maxW = Math.min(maxW, node.w + right.w - (right.minW || 2));
                    if (left) maxW = Math.min(maxW, node.w + left.w - (left.minW || 2));

                    const buildNeighborState = (n) => ({
                        node: n,
                        el: n.el,
                        initialX: n.x,
                        initialY: n.y,
                        initialW: n.w,
                        initialH: n.h,
                        minW: n.minW || 2,
                    });

                    this._coupledResize = {
                        el,
                        initialX: node.x,
                        initialW: node.w,
                        originalMaxW: node.maxW,
                        rightNeighbor: right ? buildNeighborState(right) : null,
                        leftNeighbor: left ? buildNeighborState(left) : null,
                    };

                    // Enable float to prevent compaction of other tiles when
                    // the neighbor is removed from the engine
                    this._grid.float(true);

                    // Remove neighbor(s) from engine so GridStack won't push them
                    const engine = this._grid.engine;
                    if (right) {
                        const idx = engine.nodes.indexOf(right);
                        if (idx !== -1) engine.nodes.splice(idx, 1);
                    }
                    if (left) {
                        const idx = engine.nodes.indexOf(left);
                        if (idx !== -1) engine.nodes.splice(idx, 1);
                    }

                    // Set maxW so tile can't grow past what the neighbor can absorb
                    this._grid.update(el, { maxW });
                }
            }
        },

        _onResize(el) {
            const cr = this._coupledResize;
            if (!cr || cr.el !== el) return;
            const node = el.gridstackNode;
            if (!node) return;

            const deltaW = node.w - cr.initialW;
            const deltaX = node.x - cr.initialX;

            if (deltaW === 0 && deltaX === 0) return;

            // East resize: x unchanged, w changed → right neighbor shrinks
            if (deltaX === 0 && deltaW !== 0 && cr.rightNeighbor) {
                const rn = cr.rightNeighbor;
                const newW = Math.max(rn.minW, rn.initialW - deltaW);
                const newX = rn.initialX + (rn.initialW - newW);
                // Update the node object directly
                rn.node.x = newX;
                rn.node.w = newW;
                // Update DOM attributes
                rn.el.setAttribute('gs-x', newX);
                rn.el.setAttribute('gs-w', newW);
                // Update inline styles (GridStack v12 uses calc() with CSS vars)
                rn.el.style.left = newX === 0 ? '' : `calc(${newX} * var(--gs-column-width))`;
                rn.el.style.width = `calc(${newW} * var(--gs-column-width))`;
            }

            // West resize: x changed → left neighbor shrinks
            if (deltaX !== 0 && cr.leftNeighbor) {
                const ln = cr.leftNeighbor;
                const newW = Math.max(ln.minW, ln.initialW + deltaX);
                ln.node.x = ln.initialX;
                ln.node.w = newW;
                ln.el.setAttribute('gs-x', ln.initialX);
                ln.el.setAttribute('gs-w', newW);
                // Update inline styles
                ln.el.style.left = ln.initialX === 0 ? '' : `calc(${ln.initialX} * var(--gs-column-width))`;
                ln.el.style.width = `calc(${newW} * var(--gs-column-width))`;
            }
        },

        /**
         * Find horizontally adjacent tiles on the same row(s).
         * Returns { right, left } where each is a gridstackNode or null.
         */
        _findRowNeighbors(node) {
            const nodes = this._grid.engine.nodes;
            let right = null, left = null;
            for (const n of nodes) {
                if (n === node) continue;
                // Must overlap vertically (share at least one row)
                if (n.y >= node.y + node.h || n.y + n.h <= node.y) continue;
                // Right neighbor: starts where this tile ends
                if (n.x === node.x + node.w) right = n;
                // Left neighbor: ends where this tile starts
                if (n.x + n.w === node.x) left = n;
            }
            return { right, left };
        },

        _onResizeStop(el) {
            el.style.willChange = '';
            this._grid.el.classList.remove('drag-active');

            // Clean up coupled resize: re-add neighbor to engine and restore maxW
            if (this._coupledResize) {
                const cr = this._coupledResize;
                const engine = this._grid.engine;

                // Re-add neighbor node(s) to the engine
                if (cr.rightNeighbor) engine.nodes.push(cr.rightNeighbor.node);
                if (cr.leftNeighbor) engine.nodes.push(cr.leftNeighbor.node);
                engine.sortNodes();

                // Tell GridStack the final position of each neighbor so it
                // takes ownership of their inline styles again
                if (cr.rightNeighbor) {
                    const rn = cr.rightNeighbor;
                    this._grid.update(rn.el, { x: rn.node.x, w: rn.node.w });
                }
                if (cr.leftNeighbor) {
                    const ln = cr.leftNeighbor;
                    this._grid.update(ln.el, { x: ln.node.x, w: ln.node.w });
                }

                // Restore original maxW and disable float mode
                this._grid.update(el, { maxW: cr.originalMaxW || 12 });
                this._grid.float(false);
                this._coupledResize = null;
            }

            // Clean up FLIP MutationObserver
            if (this._flipObserver) {
                this._flipObserver.disconnect();
                this._flipObserver = null;
            }
            this._flipDraggedEl = null;

            // Snap settle
            el.classList.add('tile-just-dropped');
            setTimeout(() => el.classList.remove('tile-just-dropped'), this.SETTLE_MS);

            if (this._audioEnabled) this._playSnap();
            if (navigator.vibrate) navigator.vibrate([5, 30, 5]);
        },

        // ---------- SNAP AUDIO ----------

        _playSnap() {
            try {
                if (!this._audioCtx) {
                    this._audioCtx = new (window.AudioContext || window.webkitAudioContext)();
                }
                const ctx = this._audioCtx;
                if (ctx.state === 'suspended') ctx.resume();

                // Create a short, satisfying "tick" — two layered tones
                const now = ctx.currentTime;

                // High tick
                const osc1 = ctx.createOscillator();
                const gain1 = ctx.createGain();
                osc1.connect(gain1);
                gain1.connect(ctx.destination);
                osc1.frequency.value = 1200;
                osc1.type = 'sine';
                gain1.gain.setValueAtTime(0.08, now);
                gain1.gain.exponentialRampToValueAtTime(0.001, now + 0.04);
                osc1.start(now);
                osc1.stop(now + 0.04);

                // Low thud
                const osc2 = ctx.createOscillator();
                const gain2 = ctx.createGain();
                osc2.connect(gain2);
                gain2.connect(ctx.destination);
                osc2.frequency.value = 300;
                osc2.type = 'sine';
                gain2.gain.setValueAtTime(0.06, now);
                gain2.gain.exponentialRampToValueAtTime(0.001, now + 0.06);
                osc2.start(now);
                osc2.stop(now + 0.06);
            } catch (e) {
                // Audio not available — silently ignore
            }
        },

        _attachAudioToggle() {
            const btn = document.getElementById('audio-toggle');
            if (!btn) return;
            btn.addEventListener('click', () => {
                this._audioEnabled = !this._audioEnabled;
                btn.classList.toggle('audio-enabled', this._audioEnabled);
                btn.title = `Toggle snap sounds (${this._audioEnabled ? 'on' : 'off'})`;

                // Play a test snap when enabling
                if (this._audioEnabled) this._playSnap();
            });
        },

        // ---------- TILE LOCKING ----------

        _attachLockEvents() {
            const tileGrid = document.getElementById('tile-grid');
            if (!tileGrid) return;
            tileGrid.addEventListener('click', (e) => {
                const btn = e.target.closest('.tile-lock');
                if (!btn) return;
                e.stopPropagation();
                this._toggleLock(btn.dataset.tileId);
            });
        },

        _toggleLock(tileId) {
            const el = document.querySelector(`[gs-id="${tileId}"]`);
            if (!el || !this._grid) return;

            const isLocked = this._lockedTiles.has(tileId);

            if (isLocked) {
                // Unlock
                this._lockedTiles.delete(tileId);
                el.classList.remove('tile-locked');
                this._grid.update(el, { noMove: false, noResize: false, locked: false });
            } else {
                // Lock
                this._lockedTiles.add(tileId);
                el.classList.add('tile-locked');
                this._grid.update(el, { noMove: true, noResize: true, locked: true });

                if (this._audioEnabled) this._playSnap();
                if (navigator.vibrate) navigator.vibrate(15);
            }
        },

        isLocked(tileId) {
            return this._lockedTiles.has(tileId);
        }
    };

    // ========== PUBLIC API ==========

    return {
        boot,
        resetLayout,
        isInitialized: () => initialized
    };
})();

// Auto-boot on DOMContentLoaded
document.addEventListener('DOMContentLoaded', () => {
    TileDashboard.boot();

    // After a reset-layout reload, restore all search parameters and re-analyze
    const savedForm = sessionStorage.getItem('stockanalyzer_reset_form');
    if (savedForm) {
        sessionStorage.removeItem('stockanalyzer_reset_form');
        try {
            const f = JSON.parse(savedForm);
            const textIds = ['ticker-input', 'compare-input'];
            const selectIds = ['chart-type', 'end-date-preset', 'start-date-preset'];
            const checkIds = ['ma-20', 'ma-50', 'ma-200', 'show-rsi', 'show-macd',
                              'show-bollinger', 'show-stochastic', 'show-markers'];
            const rangeIds = ['threshold-slider'];

            textIds.forEach(id => {
                const el = document.getElementById(id);
                if (el && f[id] !== undefined) el.value = f[id];
            });
            selectIds.forEach(id => {
                const el = document.getElementById(id);
                if (el && f[id] !== undefined) {
                    el.value = f[id];
                    el.dispatchEvent(new Event('change', { bubbles: true }));
                }
            });
            checkIds.forEach(id => {
                const el = document.getElementById(id);
                if (el && f[id] !== undefined) {
                    el.checked = f[id];
                    el.dispatchEvent(new Event('change', { bubbles: true }));
                }
            });
            rangeIds.forEach(id => {
                const el = document.getElementById(id);
                if (el && f[id] !== undefined) {
                    el.value = f[id];
                    el.dispatchEvent(new Event('input', { bubbles: true }));
                }
            });

            // Animal radio
            if (f['animal-type']) {
                const radio = document.querySelector(`input[name="animal-type"][value="${f['animal-type']}"]`);
                if (radio) radio.checked = true;
            }

            // Trigger analysis if we had a ticker
            if (f['ticker-input']) {
                const btn = document.getElementById('search-btn');
                if (btn) btn.click();
            }
        } catch (e) { /* ignore parse errors */ }
    }
});
