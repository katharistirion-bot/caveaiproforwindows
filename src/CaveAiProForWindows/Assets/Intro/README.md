# CAVE AI PRO — Intro video (`intro.mp4`)

Place the final intro file here:

```
Assets/Intro/intro.mp4
```

The app copies it to the output folder on build when the file exists. If missing, a built-in animated fallback plays instead (no crash).

## Production spec

| Item | Requirement |
|------|-------------|
| **Duration** | 5–15 seconds |
| **Codec** | H.264 video + AAC audio (Windows Media Foundation / MSIX friendly) |
| **Resolution** | 1920×1080 preferred (16:9); 1280×720 minimum |
| **Frame rate** | 24 or 30 fps |
| **Audio** | Tense / anxiety-building score (orchestral, electronic, or hybrid). Music must be licensed for commercial use in the Microsoft Store build. |
| **Mood** | Dark cave atmosphere — depth, stone, water drip, low light, subtle motion |
| **End card** | Final **2 seconds** hold on the CAVE AI PRO logo (match `Assets/logo.png` / brand lockup) |
| **UI** | English only; no on-screen text required (app shows **Skip video** / **Mute**) |

## Creative direction

1. Open in darkness or narrow passage — slow reveal.
2. Build tension with rising drones, pulses, or strings (μουσική αγωνίας).
3. Brief flashes of survey / cartography motifs optional (abstract, not UI screenshots).
4. Resolve to full logo on black or deep cave backdrop; hold cleanly for the end card.

## After delivery

1. Save as `intro.mp4` in this folder.
2. Rebuild the app — MSBuild includes the file automatically when present.
3. First launch after login shows the intro once; **Help → Intro video…** replays it.

## File checklist

- [ ] `intro.mp4` — master deliverable
- [ ] Licensed music stems / proof on file
- [ ] Logo end frame matches current `Assets/logo.png`
