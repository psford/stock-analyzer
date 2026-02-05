# Research & Prototypes

This folder contains UI experiments, prototypes, and research that may be referenced for future features.

## Contents

### vaporwave-concepts.html
**Created:** 2026-02-03

5 vaporwave UI concepts with working CSS/JS:
1. **Sunset Grid** — Neon sun, perspective grid, classic vaporwave
2. **Windows 95** — Retro Win95 chrome (standalone theme candidate)
3. **Neon Noir** — Cyberpunk rain, scanlines, neon glow (implementing as theme)
4. **Glitch** — RGB split, VHS artifacts, digital decay
5. **Virtual Plaza** — Japanese mall aesthetic, floating kanji

Also includes a **generative ambient audio system** using Web Audio API (no external files):
- Detuned oscillators (80% pitch = vaporwave signature)
- Convolution reverb (5-second impulse response)
- Slow filter sweeps
- Dm7 → Am7 → Em7 → Gmaj7 chord progression

To view: Open directly in browser or serve from any static file server.

---

## Notes for Future Implementation

- Win95 theme should be broken out separately (not vaporwave aesthetic)
- Neon Noir selected for first vaporwave theme implementation
- Audio system can be integrated with any theme that wants ambient sound
