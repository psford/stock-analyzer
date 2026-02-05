/**
 * THEME-DRIVEN AMBIENT AUDIO GENERATOR
 *
 * Music theory-based procedural synthesizer that reads parameters from theme JSON.
 * Generates ambient soundscapes using Web Audio API - no external files.
 *
 * Parameters from theme.audio:
 *   key, mode, chordProgression, chordVoicing, tempo, chordDuration,
 *   octave, texture, oscillator, detune, filterCutoff, reverb, pitchShift
 */

const ThemeAudio = (() => {
    'use strict';

    // ===== MUSIC THEORY CONSTANTS =====

    // Note frequencies (A4 = 440Hz standard)
    const NOTE_FREQUENCIES = {
        'C': 261.63, 'C#': 277.18, 'Db': 277.18,
        'D': 293.66, 'D#': 311.13, 'Eb': 311.13,
        'E': 329.63,
        'F': 349.23, 'F#': 369.99, 'Gb': 369.99,
        'G': 392.00, 'G#': 415.30, 'Ab': 415.30,
        'A': 440.00, 'A#': 466.16, 'Bb': 466.16,
        'B': 493.88
    };

    // Scale intervals (semitones from root)
    const SCALES = {
        major:          [0, 2, 4, 5, 7, 9, 11],
        minor:          [0, 2, 3, 5, 7, 8, 10],
        harmonic_minor: [0, 2, 3, 5, 7, 8, 11],
        melodic_minor:  [0, 2, 3, 5, 7, 9, 11],
        dorian:         [0, 2, 3, 5, 7, 9, 10],
        phrygian:       [0, 1, 3, 5, 7, 8, 10],
        lydian:         [0, 2, 4, 6, 7, 9, 11],
        mixolydian:     [0, 2, 4, 5, 7, 9, 10],
        locrian:        [0, 1, 3, 5, 6, 8, 10]
    };

    // Roman numeral to scale degree (0-indexed)
    const ROMAN_TO_DEGREE = {
        'i': 0, 'I': 0,
        'ii': 1, 'II': 1, 'bII': 1,
        'iii': 2, 'III': 2, 'bIII': 2,
        'iv': 3, 'IV': 3,
        'v': 4, 'V': 4, 'bV': 4,
        'vi': 5, 'VI': 5, 'bVI': 5,
        'vii': 6, 'VII': 6, 'bVII': 6
    };

    // Chord intervals from root
    const CHORD_VOICINGS = {
        triad:     [0, 4, 7],           // 1, 3, 5
        seventh:   [0, 4, 7, 11],       // 1, 3, 5, 7
        ninth:     [0, 4, 7, 11, 14],   // 1, 3, 5, 7, 9
        suspended: [0, 5, 7],           // 1, 4, 5
        power:     [0, 7, 12]           // 1, 5, octave
    };

    // ===== DEFAULT CONFIG (vaporwave-ish) =====
    const DEFAULT_CONFIG = {
        key: 'D',
        mode: 'minor',
        chordProgression: ['i', 'iv', 'bVII', 'bVI'],  // Dm, Gm, C, Bb
        chordVoicing: 'seventh',
        tempo: 0,
        chordDuration: 12,
        octave: 3,
        texture: 'pad',
        oscillator: 'sawtooth',
        detune: 1.002,
        filterCutoff: { min: 400, max: 1200 },
        reverb: { duration: 5, decay: 2.5, wetDry: 0.7 },
        pitchShift: 0.8,
        style: 'ambient'
    };

    // ===== STATE =====
    let audioCtx = null;
    let masterGain = null;
    let reverbNode = null;
    let filterNode = null;
    let isPlaying = false;
    let oscillators = [];
    let filterLFO = null;
    let chordInterval = null;
    let currentChordIndex = 0;
    let config = { ...DEFAULT_CONFIG };
    let nodes = null;

    // ===== MUSIC THEORY FUNCTIONS =====

    /**
     * Get frequency for a note at a given octave
     */
    function getFrequency(note, octave) {
        const baseFreq = NOTE_FREQUENCIES[note] || NOTE_FREQUENCIES['A'];
        // Middle C (C4) = 261.63, so adjust octave relative to 4
        return baseFreq * Math.pow(2, octave - 4);
    }

    /**
     * Get scale degrees for a mode
     */
    function getScaleIntervals(mode) {
        return SCALES[mode] || SCALES.minor;
    }

    /**
     * Convert Roman numeral chord to frequencies
     */
    function romanToFrequencies(roman, rootNote, mode, octave, voicing, pitchShift) {
        const scaleIntervals = getScaleIntervals(mode);
        const voicingIntervals = CHORD_VOICINGS[voicing] || CHORD_VOICINGS.triad;

        // Handle flat adjustments (bVI, bVII)
        const isFlat = roman.startsWith('b');
        const cleanRoman = isFlat ? roman.substring(1) : roman;
        const degree = ROMAN_TO_DEGREE[cleanRoman] ?? 0;

        // Get root note of chord
        const rootSemitones = scaleIntervals[degree] || 0;
        const flatAdjust = isFlat ? -1 : 0;

        // Root frequency
        const rootFreq = getFrequency(rootNote, octave);
        const chordRootFreq = rootFreq * Math.pow(2, (rootSemitones + flatAdjust) / 12);

        // Build chord from voicing
        const frequencies = voicingIntervals.map(interval => {
            const freq = chordRootFreq * Math.pow(2, interval / 12);
            return freq * pitchShift;
        });

        return frequencies;
    }

    /**
     * Get chord frequencies from progression
     */
    function getChordFrequencies(chordIndex) {
        const roman = config.chordProgression[chordIndex % config.chordProgression.length];
        return romanToFrequencies(
            roman,
            config.key,
            config.mode,
            config.octave,
            config.chordVoicing,
            config.pitchShift
        );
    }

    // ===== AUDIO ENGINE =====

    /**
     * Create impulse response for reverb
     */
    function createReverbImpulse() {
        const duration = config.reverb.duration;
        const decay = config.reverb.decay;
        const sampleRate = audioCtx.sampleRate;
        const length = sampleRate * duration;
        const impulse = audioCtx.createBuffer(2, length, sampleRate);

        for (let channel = 0; channel < 2; channel++) {
            const channelData = impulse.getChannelData(channel);
            for (let i = 0; i < length; i++) {
                channelData[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / length, decay);
            }
        }
        return impulse;
    }

    /**
     * Initialize audio context and effects chain
     */
    function init() {
        audioCtx = new (window.AudioContext || window.webkitAudioContext)();

        // Master gain - louder base volume
        masterGain = audioCtx.createGain();
        masterGain.gain.value = 0.4;
        masterGain.connect(audioCtx.destination);

        // Low-pass filter
        filterNode = audioCtx.createBiquadFilter();
        filterNode.type = 'lowpass';
        filterNode.frequency.value = config.filterCutoff.min;
        filterNode.Q.value = 1;
        filterNode.connect(masterGain);

        // Convolution reverb
        reverbNode = audioCtx.createConvolver();
        reverbNode.buffer = createReverbImpulse();

        // Wet/dry mix
        const wetGain = audioCtx.createGain();
        wetGain.gain.value = config.reverb.wetDry;
        reverbNode.connect(wetGain);
        wetGain.connect(filterNode);

        const dryGain = audioCtx.createGain();
        dryGain.gain.value = 1 - config.reverb.wetDry;
        dryGain.connect(filterNode);

        return { reverbNode, dryGain };
    }

    /**
     * Create a pad voice (two detuned oscillators)
     */
    function createPadVoice(frequency, { reverbNode, dryGain }) {
        const osc1 = audioCtx.createOscillator();
        const osc2 = audioCtx.createOscillator();

        osc1.type = config.oscillator;
        osc2.type = config.oscillator === 'sawtooth' ? 'sine' : config.oscillator;

        osc1.frequency.value = frequency;
        osc2.frequency.value = frequency * config.detune;

        const gain1 = audioCtx.createGain();
        const gain2 = audioCtx.createGain();
        gain1.gain.value = 0.1;
        gain2.gain.value = 0.15;

        osc1.connect(gain1);
        osc2.connect(gain2);

        gain1.connect(reverbNode);
        gain1.connect(dryGain);
        gain2.connect(reverbNode);
        gain2.connect(dryGain);

        osc1.start();
        osc2.start();

        return { osc1, osc2, gain1, gain2 };
    }

    /**
     * Play a chord with smooth transitions
     */
    function playChord(chordIndex, nodes) {
        const frequencies = getChordFrequencies(chordIndex);
        const now = audioCtx.currentTime;

        // Fade out existing oscillators
        oscillators.forEach(voice => {
            voice.gain1.gain.linearRampToValueAtTime(0, now + 2);
            voice.gain2.gain.linearRampToValueAtTime(0, now + 2);
            setTimeout(() => {
                try {
                    voice.osc1.stop();
                    voice.osc2.stop();
                } catch (e) { /* Already stopped */ }
            }, 3000);
        });

        oscillators = [];

        // Create new voices based on texture
        const staggerMs = config.texture === 'arpeggiated' ? 300 :
                          config.texture === 'drone' ? 0 : 200;

        frequencies.forEach((freq, i) => {
            setTimeout(() => {
                if (isPlaying) {
                    const voice = createPadVoice(freq, nodes);
                    voice.gain1.gain.setValueAtTime(0, audioCtx.currentTime);
                    voice.gain2.gain.setValueAtTime(0, audioCtx.currentTime);

                    // Different attack based on texture
                    const attackTime = config.texture === 'staccato' ? 0.1 :
                                       config.texture === 'swelling' ? 3 :
                                       1.5;
                    voice.gain1.gain.linearRampToValueAtTime(0.08, audioCtx.currentTime + attackTime);
                    voice.gain2.gain.linearRampToValueAtTime(0.12, audioCtx.currentTime + attackTime);
                    oscillators.push(voice);
                }
            }, i * staggerMs);
        });
    }

    /**
     * Filter sweep LFO
     */
    function startFilterSweep() {
        const sweepTime = config.chordDuration * 0.8;
        filterLFO = setInterval(() => {
            if (!isPlaying || !filterNode) return;
            const now = audioCtx.currentTime;
            const { min, max } = config.filterCutoff;
            const target = min + Math.random() * (max - min);
            filterNode.frequency.linearRampToValueAtTime(target, now + sweepTime);
        }, sweepTime * 1000);
    }

    /**
     * Chord progression loop
     */
    function startChordProgression(nodes) {
        playChord(currentChordIndex, nodes);

        chordInterval = setInterval(() => {
            if (!isPlaying) return;
            currentChordIndex = (currentChordIndex + 1) % config.chordProgression.length;
            playChord(currentChordIndex, nodes);
        }, config.chordDuration * 1000);
    }

    // ===== PUBLIC API =====

    return {
        /**
         * Configure audio from theme params
         */
        configure(audioParams) {
            if (!audioParams) return;
            config = { ...DEFAULT_CONFIG, ...audioParams };
            // Deep merge nested objects
            if (audioParams.filterCutoff) {
                config.filterCutoff = { ...DEFAULT_CONFIG.filterCutoff, ...audioParams.filterCutoff };
            }
            if (audioParams.reverb) {
                config.reverb = { ...DEFAULT_CONFIG.reverb, ...audioParams.reverb };
            }
            console.log('ThemeAudio configured:', config.key, config.mode, config.chordProgression);
        },

        /**
         * Start playback
         */
        start() {
            if (isPlaying) return;

            currentChordIndex = 0;
            nodes = init();
            isPlaying = true;

            startChordProgression(nodes);
            startFilterSweep();

            console.log('ThemeAudio started:', config.key, config.mode);
        },

        /**
         * Stop playback
         */
        stop() {
            if (!isPlaying) return;
            isPlaying = false;

            clearInterval(chordInterval);
            clearInterval(filterLFO);
            chordInterval = null;
            filterLFO = null;

            if (audioCtx) {
                const now = audioCtx.currentTime;
                oscillators.forEach(voice => {
                    try {
                        voice.gain1.gain.linearRampToValueAtTime(0, now + 1);
                        voice.gain2.gain.linearRampToValueAtTime(0, now + 1);
                        setTimeout(() => {
                            try {
                                voice.osc1.stop();
                                voice.osc2.stop();
                            } catch (e) { /* Already stopped */ }
                        }, 1500);
                    } catch (e) { /* Node disconnected */ }
                });
            }
            oscillators = [];

            setTimeout(() => {
                if (audioCtx) {
                    audioCtx.close();
                    audioCtx = null;
                }
                nodes = null;
            }, 2000);

            console.log('ThemeAudio stopped');
        },

        /**
         * Check if playing
         */
        isPlaying() {
            return isPlaying;
        },

        /**
         * Get simulated level for visualizer
         */
        getLevel() {
            if (!isPlaying || !masterGain) return 0;
            const t = Date.now() / 1000;
            const base = 0.3 + 0.2 * Math.sin(t * 0.5);
            const variation = 0.1 * Math.sin(t * 2.3) + 0.05 * Math.sin(t * 5.7);
            return Math.max(0, Math.min(1, base + variation));
        },

        /**
         * Get current config (for debugging)
         */
        getConfig() {
            return { ...config };
        },

        /**
         * Set volume (0-100)
         */
        setVolume(vol) {
            const gain = Math.max(0, Math.min(1, vol / 100));
            if (masterGain) {
                masterGain.gain.setValueAtTime(gain, audioCtx?.currentTime || 0);
            }
        },

        /**
         * Get current volume (0-100)
         */
        getVolume() {
            return masterGain ? Math.round(masterGain.gain.value * 100) : 40;
        }
    };
})();

// Listen for theme audio changes
window.addEventListener('themeaudiochange', (e) => {
    const wasPlaying = ThemeAudio.isPlaying();
    if (wasPlaying) {
        ThemeAudio.stop();
    }
    ThemeAudio.configure(e.detail.audio);
    if (wasPlaying) {
        // Small delay to let old audio clean up
        setTimeout(() => ThemeAudio.start(), 500);
    }
});

// Export for module systems
if (typeof module !== 'undefined' && module.exports) {
    module.exports = ThemeAudio;
}
