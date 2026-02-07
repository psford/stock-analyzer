/**
 * CANVAS EFFECTS MODULE
 *
 * Provides canvas-based visual effects that CSS cannot achieve.
 * Each effect has proper lifecycle management (start/stop/cleanup).
 *
 * Usage:
 *   CanvasEffects.start('matrixRain', container, { color: '#00ff41', speed: 1.2 });
 *   CanvasEffects.stop('matrixRain');
 *   CanvasEffects.stopAll();
 */
const CanvasEffects = (function() {
    'use strict';

    // Active effect instances
    const activeEffects = new Map();

    /**
     * Matrix Rain Effect
     * Falling columns of characters like in The Matrix
     */
    const matrixRain = {
        create: function(container, options = {}) {
            const config = {
                color: options.color || '#00ff41',
                backgroundColor: options.backgroundColor || 'rgba(0, 0, 0, 0.05)',
                fontSize: options.fontSize || 14,
                speed: options.speed || 1,
                density: options.density || 0.98,
                characters: options.characters || 'アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲン0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ',
                glowIntensity: options.glowIntensity || 0.8
            };

            // Create canvas
            const canvas = document.createElement('canvas');
            canvas.className = 'canvas-effect-matrix-rain';
            canvas.style.cssText = `
                position: absolute;
                top: 0;
                left: 0;
                width: 100%;
                height: 100%;
                z-index: 1;
                pointer-events: none;
            `;
            container.style.position = 'relative';
            container.insertBefore(canvas, container.firstChild);

            const ctx = canvas.getContext('2d');
            let animationId = null;
            let columns = [];
            let lastTime = 0;
            const frameInterval = 1000 / (30 * config.speed); // ~30fps base, adjusted by speed

            // Resize handler
            function resize() {
                canvas.width = container.offsetWidth;
                canvas.height = container.offsetHeight;

                // Initialize columns
                const columnCount = Math.floor(canvas.width / config.fontSize);
                columns = [];
                for (let i = 0; i < columnCount; i++) {
                    // Random starting positions
                    columns[i] = Math.random() * canvas.height / config.fontSize;
                }
            }

            // Draw frame
            function draw(timestamp) {
                if (timestamp - lastTime < frameInterval) {
                    animationId = requestAnimationFrame(draw);
                    return;
                }
                lastTime = timestamp;

                // Semi-transparent black to create fade trail
                ctx.fillStyle = config.backgroundColor;
                ctx.fillRect(0, 0, canvas.width, canvas.height);

                // Set up text style
                ctx.font = `${config.fontSize}px monospace`;

                for (let i = 0; i < columns.length; i++) {
                    // Pick random character
                    const char = config.characters[Math.floor(Math.random() * config.characters.length)];
                    const x = i * config.fontSize;
                    const y = columns[i] * config.fontSize;

                    // Leading character is brighter (white/bright green)
                    ctx.fillStyle = '#ffffff';
                    ctx.fillText(char, x, y);

                    // Add glow effect for leading character
                    if (config.glowIntensity > 0) {
                        ctx.shadowColor = config.color;
                        ctx.shadowBlur = 10 * config.glowIntensity;
                        ctx.fillText(char, x, y);
                        ctx.shadowBlur = 0;
                    }

                    // Draw a few trailing characters in green
                    ctx.fillStyle = config.color;
                    for (let j = 1; j <= 3; j++) {
                        const trailY = y - (j * config.fontSize);
                        if (trailY > 0) {
                            const trailChar = config.characters[Math.floor(Math.random() * config.characters.length)];
                            ctx.globalAlpha = 1 - (j * 0.25);
                            ctx.fillText(trailChar, x, trailY);
                        }
                    }
                    ctx.globalAlpha = 1;

                    // Move column down or reset to top
                    if (y > canvas.height && Math.random() > config.density) {
                        columns[i] = 0;
                    } else {
                        columns[i]++;
                    }
                }

                animationId = requestAnimationFrame(draw);
            }

            // Start
            resize();
            window.addEventListener('resize', resize);
            animationId = requestAnimationFrame(draw);

            // Return cleanup function
            return {
                canvas: canvas,
                stop: function() {
                    if (animationId) {
                        cancelAnimationFrame(animationId);
                        animationId = null;
                    }
                    window.removeEventListener('resize', resize);
                    if (canvas.parentNode) {
                        canvas.parentNode.removeChild(canvas);
                    }
                }
            };
        }
    };

    /**
     * Rain Effect
     * Falling raindrops with streaks
     */
    const rain = {
        create: function(container, options = {}) {
            const config = {
                color: options.color || 'rgba(174, 194, 224, 0.7)',
                count: options.count || 200,
                speed: options.speed || 1,
                angle: options.angle || 15, // degrees from vertical
                length: options.length || { min: 15, max: 30 },
                width: options.width || 1.5
            };

            const canvas = document.createElement('canvas');
            canvas.className = 'canvas-effect-rain';
            canvas.style.cssText = `
                position: fixed;
                top: 0;
                left: 0;
                width: 100%;
                height: 100%;
                z-index: 9995;
                pointer-events: none;
            `;
            container.style.position = 'relative';
            container.insertBefore(canvas, container.firstChild);

            const ctx = canvas.getContext('2d');
            let animationId = null;
            let drops = [];

            function resize() {
                canvas.width = window.innerWidth;
                canvas.height = window.innerHeight;
                initDrops();
            }

            function initDrops() {
                drops = [];
                for (let i = 0; i < config.count; i++) {
                    drops.push({
                        x: Math.random() * canvas.width * 1.5, // Extra width for angle
                        y: Math.random() * canvas.height,
                        length: config.length.min + Math.random() * (config.length.max - config.length.min),
                        speedY: (10 + Math.random() * 10) * config.speed,
                        opacity: 0.4 + Math.random() * 0.5
                    });
                }
            }

            function draw() {
                ctx.clearRect(0, 0, canvas.width, canvas.height);

                const angleRad = (config.angle * Math.PI) / 180;
                const dx = Math.sin(angleRad);
                const dy = Math.cos(angleRad);

                ctx.strokeStyle = config.color;
                ctx.lineWidth = config.width;
                ctx.lineCap = 'round';

                for (let drop of drops) {
                    ctx.globalAlpha = drop.opacity;
                    ctx.beginPath();
                    ctx.moveTo(drop.x, drop.y);
                    ctx.lineTo(
                        drop.x - dx * drop.length,
                        drop.y + dy * drop.length
                    );
                    ctx.stroke();

                    // Move drop
                    drop.y += drop.speedY;
                    drop.x -= drop.speedY * dx * 0.3; // Slight horizontal drift

                    // Reset if off screen
                    if (drop.y > canvas.height + drop.length) {
                        drop.y = -drop.length;
                        drop.x = Math.random() * canvas.width * 1.5;
                        drop.opacity = 0.4 + Math.random() * 0.5;
                    }
                }
                ctx.globalAlpha = 1;

                animationId = requestAnimationFrame(draw);
            }

            resize();
            window.addEventListener('resize', resize);
            animationId = requestAnimationFrame(draw);

            return {
                canvas: canvas,
                stop: function() {
                    if (animationId) {
                        cancelAnimationFrame(animationId);
                        animationId = null;
                    }
                    window.removeEventListener('resize', resize);
                    if (canvas.parentNode) {
                        canvas.parentNode.removeChild(canvas);
                    }
                }
            };
        }
    };

    /**
     * Snow Effect
     * Falling snowflakes
     */
    const snow = {
        create: function(container, options = {}) {
            const config = {
                color: options.color || '#ffffff',
                count: options.count || 100,
                speed: options.speed || 1,
                wind: options.wind || 0.5,
                size: options.size || { min: 2, max: 5 }
            };

            const canvas = document.createElement('canvas');
            canvas.className = 'canvas-effect-snow';
            canvas.style.cssText = `
                position: absolute;
                top: 0;
                left: 0;
                width: 100%;
                height: 100%;
                z-index: 1;
                pointer-events: none;
            `;
            container.style.position = 'relative';
            container.insertBefore(canvas, container.firstChild);

            const ctx = canvas.getContext('2d');
            let animationId = null;
            let flakes = [];

            function resize() {
                canvas.width = container.offsetWidth;
                canvas.height = container.offsetHeight;
                initFlakes();
            }

            function initFlakes() {
                flakes = [];
                for (let i = 0; i < config.count; i++) {
                    flakes.push({
                        x: Math.random() * canvas.width,
                        y: Math.random() * canvas.height,
                        size: config.size.min + Math.random() * (config.size.max - config.size.min),
                        speedY: 0.5 + Math.random() * config.speed,
                        speedX: (Math.random() - 0.5) * config.wind,
                        opacity: 0.5 + Math.random() * 0.5
                    });
                }
            }

            function draw() {
                ctx.clearRect(0, 0, canvas.width, canvas.height);

                for (let flake of flakes) {
                    ctx.beginPath();
                    ctx.arc(flake.x, flake.y, flake.size, 0, Math.PI * 2);
                    ctx.fillStyle = config.color;
                    ctx.globalAlpha = flake.opacity;
                    ctx.fill();

                    // Move flake
                    flake.y += flake.speedY;
                    flake.x += flake.speedX + Math.sin(flake.y * 0.01) * 0.5;

                    // Reset if off screen
                    if (flake.y > canvas.height) {
                        flake.y = -flake.size;
                        flake.x = Math.random() * canvas.width;
                    }
                    if (flake.x > canvas.width) flake.x = 0;
                    if (flake.x < 0) flake.x = canvas.width;
                }
                ctx.globalAlpha = 1;

                animationId = requestAnimationFrame(draw);
            }

            resize();
            window.addEventListener('resize', resize);
            animationId = requestAnimationFrame(draw);

            return {
                canvas: canvas,
                stop: function() {
                    if (animationId) {
                        cancelAnimationFrame(animationId);
                        animationId = null;
                    }
                    window.removeEventListener('resize', resize);
                    if (canvas.parentNode) {
                        canvas.parentNode.removeChild(canvas);
                    }
                }
            };
        }
    };

    /**
     * Particles Effect
     * Floating particles that drift around
     */
    const particles = {
        create: function(container, options = {}) {
            const config = {
                color: options.color || '#ffffff',
                count: options.count || 50,
                speed: options.speed || 0.5,
                size: options.size || { min: 1, max: 3 },
                connections: options.connections !== false,
                connectionDistance: options.connectionDistance || 100,
                connectionColor: options.connectionColor || options.color || '#ffffff'
            };

            const canvas = document.createElement('canvas');
            canvas.className = 'canvas-effect-particles';
            canvas.style.cssText = `
                position: absolute;
                top: 0;
                left: 0;
                width: 100%;
                height: 100%;
                z-index: 1;
                pointer-events: none;
            `;
            container.style.position = 'relative';
            container.insertBefore(canvas, container.firstChild);

            const ctx = canvas.getContext('2d');
            let animationId = null;
            let particleList = [];

            function resize() {
                canvas.width = container.offsetWidth;
                canvas.height = container.offsetHeight;
                initParticles();
            }

            function initParticles() {
                particleList = [];
                for (let i = 0; i < config.count; i++) {
                    particleList.push({
                        x: Math.random() * canvas.width,
                        y: Math.random() * canvas.height,
                        size: config.size.min + Math.random() * (config.size.max - config.size.min),
                        vx: (Math.random() - 0.5) * config.speed,
                        vy: (Math.random() - 0.5) * config.speed,
                        opacity: 0.3 + Math.random() * 0.7
                    });
                }
            }

            function draw() {
                ctx.clearRect(0, 0, canvas.width, canvas.height);

                // Draw connections
                if (config.connections) {
                    ctx.strokeStyle = config.connectionColor;
                    for (let i = 0; i < particleList.length; i++) {
                        for (let j = i + 1; j < particleList.length; j++) {
                            const dx = particleList[i].x - particleList[j].x;
                            const dy = particleList[i].y - particleList[j].y;
                            const dist = Math.sqrt(dx * dx + dy * dy);
                            if (dist < config.connectionDistance) {
                                ctx.globalAlpha = (1 - dist / config.connectionDistance) * 0.3;
                                ctx.beginPath();
                                ctx.moveTo(particleList[i].x, particleList[i].y);
                                ctx.lineTo(particleList[j].x, particleList[j].y);
                                ctx.stroke();
                            }
                        }
                    }
                }

                // Draw particles
                ctx.fillStyle = config.color;
                for (let p of particleList) {
                    ctx.globalAlpha = p.opacity;
                    ctx.beginPath();
                    ctx.arc(p.x, p.y, p.size, 0, Math.PI * 2);
                    ctx.fill();

                    // Move
                    p.x += p.vx;
                    p.y += p.vy;

                    // Bounce off edges
                    if (p.x < 0 || p.x > canvas.width) p.vx *= -1;
                    if (p.y < 0 || p.y > canvas.height) p.vy *= -1;
                }
                ctx.globalAlpha = 1;

                animationId = requestAnimationFrame(draw);
            }

            resize();
            window.addEventListener('resize', resize);
            animationId = requestAnimationFrame(draw);

            return {
                canvas: canvas,
                stop: function() {
                    if (animationId) {
                        cancelAnimationFrame(animationId);
                        animationId = null;
                    }
                    window.removeEventListener('resize', resize);
                    if (canvas.parentNode) {
                        canvas.parentNode.removeChild(canvas);
                    }
                }
            };
        }
    };

    // Effect registry
    const effects = {
        rain: rain,
        matrixRain: matrixRain,
        snow: snow,
        particles: particles
    };

    // Public API
    return {
        /**
         * Start an effect
         * @param {string} name - Effect name (matrixRain, snow, particles)
         * @param {HTMLElement} container - Container element
         * @param {Object} options - Effect-specific options
         */
        start: function(name, container, options = {}) {
            if (!effects[name]) {
                console.warn(`Canvas effect '${name}' not found`);
                return false;
            }

            // Stop existing instance of this effect
            this.stop(name);

            // Create new instance
            const instance = effects[name].create(container, options);
            activeEffects.set(name, instance);
            return true;
        },

        /**
         * Stop a specific effect
         * @param {string} name - Effect name
         */
        stop: function(name) {
            const instance = activeEffects.get(name);
            if (instance) {
                instance.stop();
                activeEffects.delete(name);
            }
        },

        /**
         * Stop all active effects
         */
        stopAll: function() {
            for (const [name, instance] of activeEffects) {
                instance.stop();
            }
            activeEffects.clear();
        },

        /**
         * Check if an effect is running
         * @param {string} name - Effect name
         * @returns {boolean}
         */
        isRunning: function(name) {
            return activeEffects.has(name);
        },

        /**
         * Get list of available effects
         * @returns {string[]}
         */
        getAvailable: function() {
            return Object.keys(effects);
        }
    };
})();

// Export for module systems
if (typeof module !== 'undefined' && module.exports) {
    module.exports = CanvasEffects;
}
