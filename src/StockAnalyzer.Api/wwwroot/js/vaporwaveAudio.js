/**
 * VAPORWAVE AMBIENT AUDIO GENERATOR
 *
 * Generates dreamy, slowed-down ambient soundscapes using only Web Audio API.
 * No external audio files - everything is synthesized in real-time.
 *
 * Features:
 * - Detuned saw/sine pads with heavy reverb
 * - Slow filter sweeps
 * - Generative chord progressions (Dm7, Am7, Em7, Gmaj7)
 * - Smooth fade transitions between chords
 */

const VaporwaveAudio = (() => {
    let audioCtx = null;
    let masterGain = null;
    let reverbNode = null;
    let filterNode = null;
    let isPlaying = false;
    let oscillators = [];
    let filterLFO = null;
    let chordInterval = null;
    let nodes = null;

    // Vaporwave chord frequencies (slowed down by ~0.8x for that characteristic pitch)
    const CHORDS = {
        // Dm7 - dreamy, melancholic
        Dm7: [146.83 * 0.8, 174.61 * 0.8, 220.00 * 0.8, 261.63 * 0.8],
        // Am7 - spacious, nostalgic
        Am7: [110.00 * 0.8, 130.81 * 0.8, 164.81 * 0.8, 196.00 * 0.8],
        // Em7 - ethereal, floating
        Em7: [82.41 * 0.8, 98.00 * 0.8, 123.47 * 0.8, 146.83 * 0.8],
        // Gmaj7 - bright, uplifting
        Gmaj7: [98.00 * 0.8, 123.47 * 0.8, 146.83 * 0.8, 185.00 * 0.8]
    };

    const CHORD_PROGRESSION = ['Dm7', 'Am7', 'Em7', 'Gmaj7'];
    let currentChordIndex = 0;

    // Create impulse response for reverb (convolution)
    function createReverbImpulse(duration = 4, decay = 3) {
        const sampleRate = audioCtx.sampleRate;
        const length = sampleRate * duration;
        const impulse = audioCtx.createBuffer(2, length, sampleRate);

        for (let channel = 0; channel < 2; channel++) {
            const channelData = impulse.getChannelData(channel);
            for (let i = 0; i < length; i++) {
                // Exponential decay with random noise
                channelData[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / length, decay);
            }
        }
        return impulse;
    }

    // Initialize audio context and effects chain
    function init() {
        audioCtx = new (window.AudioContext || window.webkitAudioContext)();

        // Master gain (overall volume)
        masterGain = audioCtx.createGain();
        masterGain.gain.value = 0.15; // Keep it ambient, not overwhelming
        masterGain.connect(audioCtx.destination);

        // Low-pass filter for that muffled vaporwave sound
        filterNode = audioCtx.createBiquadFilter();
        filterNode.type = 'lowpass';
        filterNode.frequency.value = 800;
        filterNode.Q.value = 1;
        filterNode.connect(masterGain);

        // Convolution reverb for spacious sound
        reverbNode = audioCtx.createConvolver();
        reverbNode.buffer = createReverbImpulse(5, 2.5);

        // Wet/dry mix for reverb
        const reverbGain = audioCtx.createGain();
        reverbGain.gain.value = 0.7;
        reverbNode.connect(reverbGain);
        reverbGain.connect(filterNode);

        // Dry signal
        const dryGain = audioCtx.createGain();
        dryGain.gain.value = 0.3;
        dryGain.connect(filterNode);

        return { reverbNode, dryGain };
    }

    // Create a detuned pad oscillator
    function createPadVoice(frequency, { reverbNode, dryGain }) {
        // Two oscillators slightly detuned for richness
        const osc1 = audioCtx.createOscillator();
        const osc2 = audioCtx.createOscillator();

        osc1.type = 'sawtooth';
        osc2.type = 'sine';

        osc1.frequency.value = frequency;
        osc2.frequency.value = frequency * 1.002; // Slight detune

        // Individual voice gains
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

    // Play a chord with smooth transitions
    function playChord(chordName, nodes) {
        const frequencies = CHORDS[chordName];
        const now = audioCtx.currentTime;

        // Fade out existing oscillators
        oscillators.forEach(voice => {
            voice.gain1.gain.linearRampToValueAtTime(0, now + 2);
            voice.gain2.gain.linearRampToValueAtTime(0, now + 2);
            setTimeout(() => {
                try {
                    voice.osc1.stop();
                    voice.osc2.stop();
                } catch (e) {
                    // Already stopped
                }
            }, 3000);
        });

        oscillators = [];

        // Create new voices for each note in the chord
        frequencies.forEach((freq, i) => {
            setTimeout(() => {
                if (isPlaying) {
                    const voice = createPadVoice(freq, nodes);
                    // Fade in
                    voice.gain1.gain.setValueAtTime(0, audioCtx.currentTime);
                    voice.gain2.gain.setValueAtTime(0, audioCtx.currentTime);
                    voice.gain1.gain.linearRampToValueAtTime(0.08, audioCtx.currentTime + 1.5);
                    voice.gain2.gain.linearRampToValueAtTime(0.12, audioCtx.currentTime + 1.5);
                    oscillators.push(voice);
                }
            }, i * 200); // Stagger note entry for arpeggio effect
        });
    }

    // Slow filter sweep LFO
    function startFilterSweep() {
        filterLFO = setInterval(() => {
            if (!isPlaying || !filterNode) return;
            const now = audioCtx.currentTime;
            const minFreq = 400;
            const maxFreq = 1200;
            // Random target within range
            const target = minFreq + Math.random() * (maxFreq - minFreq);
            filterNode.frequency.linearRampToValueAtTime(target, now + 8);
        }, 8000);
    }

    // Chord progression loop
    function startChordProgression(nodes) {
        playChord(CHORD_PROGRESSION[currentChordIndex], nodes);

        chordInterval = setInterval(() => {
            if (!isPlaying) return;
            currentChordIndex = (currentChordIndex + 1) % CHORD_PROGRESSION.length;
            playChord(CHORD_PROGRESSION[currentChordIndex], nodes);
        }, 12000); // Change chord every 12 seconds
    }

    // Public API
    return {
        start() {
            if (isPlaying) return;

            nodes = init();
            isPlaying = true;

            startChordProgression(nodes);
            startFilterSweep();

            console.log('Vaporwave ambient audio started');
        },

        stop() {
            if (!isPlaying) return;
            isPlaying = false;

            clearInterval(chordInterval);
            clearInterval(filterLFO);
            chordInterval = null;
            filterLFO = null;

            // Fade out all oscillators
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
                            } catch (e) {
                                // Already stopped
                            }
                        }, 1500);
                    } catch (e) {
                        // Node already disconnected
                    }
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

            console.log('Vaporwave ambient audio stopped');
        },

        isPlaying() {
            return isPlaying;
        },

        // Get current audio level for visualizer (0-1)
        getLevel() {
            if (!isPlaying || !masterGain) return 0;
            // Simulated level based on chord phase - real analyser would need more setup
            const t = Date.now() / 1000;
            const base = 0.3 + 0.2 * Math.sin(t * 0.5);
            const variation = 0.1 * Math.sin(t * 2.3) + 0.05 * Math.sin(t * 5.7);
            return Math.max(0, Math.min(1, base + variation));
        }
    };
})();

// Export for module systems if needed
if (typeof module !== 'undefined' && module.exports) {
    module.exports = VaporwaveAudio;
}
