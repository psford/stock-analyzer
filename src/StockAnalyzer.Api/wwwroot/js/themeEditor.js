/**
 * Theme Editor Module
 * Connects the Claude-powered theme generator with the preview component.
 */
const ThemeEditor = (function() {
    'use strict';

    // Configuration
    const GENERATOR_URL = 'http://localhost:8001';
    const STORAGE_KEY = 'stockAnalyzer_customThemes';

    // State
    let currentTheme = null;
    let preview = null;
    let isGenerating = false;

    // DOM elements (cached after init)
    let elements = {};

    /**
     * Initialize the editor
     */
    function init() {
        cacheElements();
        setupEventListeners();
        loadSavedThemes();
        initPreview();
    }

    /**
     * Cache DOM element references
     */
    function cacheElements() {
        elements = {
            // Inputs
            themeName: document.getElementById('theme-name'),
            themePrompt: document.getElementById('theme-prompt'),
            baseTheme: document.getElementById('base-theme'),
            refineFeedback: document.getElementById('refine-feedback'),

            // Buttons
            generateBtn: document.getElementById('generate-btn'),
            refineBtn: document.getElementById('refine-btn'),
            copyJsonBtn: document.getElementById('copy-json-btn'),
            saveBtn: document.getElementById('save-btn'),
            applyBtn: document.getElementById('apply-btn'),

            // Sections
            refineSection: document.getElementById('refine-section'),
            actionsSection: document.getElementById('actions-section'),

            // Status messages
            generateStatus: document.getElementById('generate-status'),
            refineStatus: document.getElementById('refine-status'),

            // Preview
            previewContainer: document.getElementById('preview-container'),

            // JSON display
            jsonToggle: document.getElementById('json-toggle'),
            jsonContent: document.getElementById('json-content'),
            jsonDisplay: document.getElementById('json-display'),

            // Loading
            loadingOverlay: document.getElementById('loading-overlay'),
            loadingText: document.getElementById('loading-text'),

            // Saved themes
            savedThemesList: document.getElementById('saved-themes-list')
        };
    }

    /**
     * Setup event listeners
     */
    function setupEventListeners() {
        // Generate button
        elements.generateBtn.addEventListener('click', handleGenerate);

        // Refine button
        elements.refineBtn.addEventListener('click', handleRefine);

        // Copy JSON button
        elements.copyJsonBtn.addEventListener('click', handleCopyJson);

        // Save button
        elements.saveBtn.addEventListener('click', handleSave);

        // Apply button
        elements.applyBtn.addEventListener('click', handleApply);

        // JSON toggle
        elements.jsonToggle.addEventListener('click', toggleJsonDisplay);

        // Enter key in prompt textarea triggers generate
        elements.themePrompt.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && e.ctrlKey) {
                e.preventDefault();
                handleGenerate();
            }
        });

        // Enter key in refine textarea triggers refine
        elements.refineFeedback.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && e.ctrlKey) {
                e.preventDefault();
                handleRefine();
            }
        });
    }

    /**
     * Initialize the preview component
     */
    function initPreview() {
        preview = ThemePreview.create(elements.previewContainer);

        // Load default theme
        preview.applyTheme({
            id: 'default',
            variables: {
                'bg-primary': '#0a0a0f',
                'bg-secondary': '#12121a',
                'bg-tertiary': '#1a1a24',
                'text-primary': '#e0e0ff',
                'text-secondary': '#8888aa',
                'text-muted': '#555566',
                'accent': '#ff71ce',
                'border-primary': '#2a2a4a',
                'success': '#05ffa1',
                'error': '#ff3366',
                'btn-primary-text': '#ffffff'
            }
        });
    }

    /**
     * Handle generate button click
     */
    async function handleGenerate() {
        const prompt = elements.themePrompt.value.trim();
        const name = elements.themeName.value.trim() || 'Custom Theme';
        const baseTheme = elements.baseTheme.value;

        if (!prompt) {
            showStatus(elements.generateStatus, 'error', 'Please describe your theme');
            return;
        }

        if (isGenerating) return;

        try {
            isGenerating = true;
            setButtonLoading(elements.generateBtn, true);
            showLoading('Generating your theme...');
            clearStatus(elements.generateStatus);

            const theme = await generateTheme(prompt, name, baseTheme);

            currentTheme = theme;
            preview.applyTheme(theme);
            updateJsonDisplay(theme);
            showSections();
            showStatus(elements.generateStatus, 'success', `Generated: ${theme.name}`);

        } catch (error) {
            console.error('Generate error:', error);
            showStatus(elements.generateStatus, 'error', getErrorMessage(error));
        } finally {
            isGenerating = false;
            setButtonLoading(elements.generateBtn, false);
            hideLoading();
        }
    }

    /**
     * Handle refine button click
     */
    async function handleRefine() {
        if (!currentTheme) {
            showStatus(elements.refineStatus, 'error', 'Generate a theme first');
            return;
        }

        const feedback = elements.refineFeedback.value.trim();
        if (!feedback) {
            showStatus(elements.refineStatus, 'error', 'Please describe what to change');
            return;
        }

        if (isGenerating) return;

        try {
            isGenerating = true;
            setButtonLoading(elements.refineBtn, true);
            showLoading('Refining your theme...');
            clearStatus(elements.refineStatus);

            const theme = await refineTheme(currentTheme, feedback);

            currentTheme = theme;
            preview.applyTheme(theme);
            updateJsonDisplay(theme);
            showStatus(elements.refineStatus, 'success', 'Theme refined!');
            elements.refineFeedback.value = '';

        } catch (error) {
            console.error('Refine error:', error);
            showStatus(elements.refineStatus, 'error', getErrorMessage(error));
        } finally {
            isGenerating = false;
            setButtonLoading(elements.refineBtn, false);
            hideLoading();
        }
    }

    /**
     * Handle copy JSON button click
     */
    async function handleCopyJson() {
        if (!currentTheme) return;

        try {
            await navigator.clipboard.writeText(JSON.stringify(currentTheme, null, 2));
            showStatus(elements.generateStatus, 'success', 'JSON copied to clipboard!');
            setTimeout(() => clearStatus(elements.generateStatus), 2000);
        } catch (error) {
            showStatus(elements.generateStatus, 'error', 'Failed to copy');
        }
    }

    /**
     * Handle save button click
     */
    function handleSave() {
        if (!currentTheme) return;

        // Use the name from the input field, falling back to theme's internal name
        const themeName = elements.themeName.value.trim() || currentTheme.name || 'Custom Theme';
        const themeId = themeName.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');

        // Update the theme object with the user's chosen name
        currentTheme.name = themeName;
        currentTheme.id = themeId;

        const saved = getSavedThemes();
        const existingIndex = saved.findIndex(t => t.theme.id === themeId);

        const entry = {
            id: themeId,
            name: themeName,
            savedAt: Date.now(),
            theme: currentTheme
        };

        if (existingIndex >= 0) {
            saved[existingIndex] = entry;
        } else {
            saved.unshift(entry);
        }

        localStorage.setItem(STORAGE_KEY, JSON.stringify(saved));
        loadSavedThemes();
        showStatus(elements.generateStatus, 'success', 'Theme saved!');
        setTimeout(() => clearStatus(elements.generateStatus), 2000);
    }

    /**
     * Handle apply button click
     */
    function handleApply() {
        if (!currentTheme) return;

        // Use ThemeLoader to apply globally
        if (typeof ThemeLoader !== 'undefined' && ThemeLoader.applyThemeJson) {
            ThemeLoader.applyThemeJson(currentTheme);
            showStatus(elements.generateStatus, 'success', 'Theme applied to app!');
        } else {
            showStatus(elements.generateStatus, 'info', 'Theme ready. Paste JSON in the main app to apply.');
        }
        setTimeout(() => clearStatus(elements.generateStatus), 3000);
    }

    /**
     * Toggle JSON display visibility
     */
    function toggleJsonDisplay() {
        elements.jsonToggle.classList.toggle('expanded');
        elements.jsonContent.classList.toggle('visible');
    }

    /**
     * API: Generate theme
     */
    async function generateTheme(prompt, name, baseTheme) {
        const res = await fetch(`${GENERATOR_URL}/generate`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                prompt: prompt,
                name: name,
                base_theme: baseTheme
            })
        });

        if (!res.ok) {
            const errorText = await res.text();
            throw new Error(errorText);
        }

        const data = await res.json();
        return data.theme;
    }

    /**
     * API: Refine theme
     */
    async function refineTheme(theme, feedback) {
        const res = await fetch(`${GENERATOR_URL}/refine`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                theme: theme,
                feedback: feedback
            })
        });

        if (!res.ok) {
            const errorText = await res.text();
            throw new Error(errorText);
        }

        const data = await res.json();
        return data.theme;
    }

    /**
     * Get saved themes from localStorage
     */
    function getSavedThemes() {
        try {
            return JSON.parse(localStorage.getItem(STORAGE_KEY) || '[]');
        } catch {
            return [];
        }
    }

    /**
     * Load and display saved themes
     */
    function loadSavedThemes() {
        const saved = getSavedThemes();

        if (saved.length === 0) {
            elements.savedThemesList.innerHTML = '<div class="empty-state">No saved themes yet</div>';
            return;
        }

        elements.savedThemesList.innerHTML = saved.map((entry, index) => `
            <div class="saved-theme-item" data-index="${index}">
                <div class="theme-info">
                    <div class="theme-name">${escapeHtml(entry.name)}</div>
                    <div class="theme-date">${formatDate(entry.savedAt)}</div>
                </div>
                <button class="delete-btn" data-index="${index}" title="Delete">&times;</button>
            </div>
        `).join('');

        // Add click handlers
        elements.savedThemesList.querySelectorAll('.saved-theme-item').forEach(item => {
            item.addEventListener('click', (e) => {
                if (e.target.classList.contains('delete-btn')) {
                    const index = parseInt(e.target.dataset.index);
                    deleteSavedTheme(index);
                } else {
                    const index = parseInt(item.dataset.index);
                    loadSavedTheme(index);
                }
            });
        });
    }

    /**
     * Load a saved theme into the editor
     */
    function loadSavedTheme(index) {
        const saved = getSavedThemes();
        if (index < 0 || index >= saved.length) return;

        const entry = saved[index];
        currentTheme = entry.theme;

        elements.themeName.value = entry.name;
        preview.applyTheme(currentTheme);
        updateJsonDisplay(currentTheme);
        showSections();
        showStatus(elements.generateStatus, 'info', `Loaded: ${entry.name}`);
        setTimeout(() => clearStatus(elements.generateStatus), 2000);
    }

    /**
     * Delete a saved theme
     */
    function deleteSavedTheme(index) {
        const saved = getSavedThemes();
        if (index < 0 || index >= saved.length) return;

        saved.splice(index, 1);
        localStorage.setItem(STORAGE_KEY, JSON.stringify(saved));
        loadSavedThemes();
    }

    /**
     * Show refine and actions sections
     */
    function showSections() {
        elements.refineSection.style.display = 'block';
        elements.actionsSection.style.display = 'block';
    }

    /**
     * Update JSON display
     */
    function updateJsonDisplay(theme) {
        elements.jsonDisplay.textContent = JSON.stringify(theme, null, 2);
    }

    /**
     * Show loading overlay
     */
    function showLoading(message) {
        elements.loadingText.textContent = message;
        elements.loadingOverlay.classList.add('visible');
    }

    /**
     * Hide loading overlay
     */
    function hideLoading() {
        elements.loadingOverlay.classList.remove('visible');
    }

    /**
     * Set button loading state
     */
    function setButtonLoading(button, loading) {
        button.disabled = loading;
        if (loading) {
            button.dataset.originalText = button.textContent;
            button.innerHTML = '<span>Working...</span>';
        } else if (button.dataset.originalText) {
            button.innerHTML = `<span>${button.dataset.originalText}</span>`;
        }
    }

    /**
     * Show status message
     */
    function showStatus(element, type, message) {
        element.className = 'status-message ' + type;
        element.textContent = message;
    }

    /**
     * Clear status message
     */
    function clearStatus(element) {
        element.className = 'status-message';
        element.textContent = '';
    }

    /**
     * Get user-friendly error message
     */
    function getErrorMessage(error) {
        const msg = error.message || String(error);

        if (msg.includes('Failed to fetch') || msg.includes('NetworkError')) {
            return 'Theme generator service not running. Start it with: python helpers/theme_generator.py';
        }
        if (msg.includes('credit') || msg.includes('balance')) {
            return 'API credits exhausted. Add credits at console.anthropic.com';
        }
        if (msg.includes('timeout')) {
            return 'Request timed out. Try a simpler prompt.';
        }

        return msg.length > 100 ? msg.substring(0, 100) + '...' : msg;
    }

    /**
     * Escape HTML for safe display
     */
    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    /**
     * Format date for display
     */
    function formatDate(timestamp) {
        const date = new Date(timestamp);
        const now = new Date();
        const diff = now - date;

        if (diff < 60000) return 'Just now';
        if (diff < 3600000) return `${Math.floor(diff / 60000)}m ago`;
        if (diff < 86400000) return `${Math.floor(diff / 3600000)}h ago`;
        if (diff < 604800000) return `${Math.floor(diff / 86400000)}d ago`;

        return date.toLocaleDateString();
    }

    // Public API
    return {
        init: init
    };
})();

// Export for module systems
if (typeof module !== 'undefined' && module.exports) {
    module.exports = ThemeEditor;
}
