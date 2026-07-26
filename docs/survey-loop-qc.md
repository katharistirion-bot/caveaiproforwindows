# Survey loop QC (rules-only)

Cross-platform **traverse loop detection** and **misclosure summary** for CaveAI Pro (Android), CAVE AI PRO (Windows), and caveaipro.com workspace QC. No LLM.

Machine-readable contract: [survey-loop-qc-contract.json](./survey-loop-qc-contract.json)

## Detection

All platforms use the same **spanning-tree + chord** algorithm:

1. Build an undirected graph from traverse legs (`toStation !== "-"`).
2. Union-find marks tree edges; shots whose endpoints are already connected are **chords** (closing legs).
3. For each chord, walk the unique tree path between its endpoints and sum ENU shot vectors.
4. Subtract the chord vector; **|Δ|** is the 3D misclosure magnitude (metres).
5. **Path length** is the sum of leg distances around the cycle (including the chord).

Requires at least **3 traverse legs**. Reports up to **80** independent loops.

Vector model (ENU from azimuth / clino / distance):

- `hDist = distance × cos(clino°)`
- `dE = hDist × sin(azimuth°)`
- `dN = hDist × cos(azimuth°)`
- `dZ = distance × sin(clino°)`

## Severity tiers (worst loop)

| Tier | Rule |
|------|------|
| **excellent** | misclosure &lt; 0.05 m |
| **good** | misclosure &lt; 0.25 m **and** ppm &lt; 500 |
| **review** | misclosure &lt; 1.0 m |
| **large** | otherwise |

**ppm** = `(misclosureM / pathLengthM) × 1_000_000` (omit when path length is 0).

These tiers differ from Windows **plan overlay** colours (`LoopClosureSeverity`: good / moderate / large at 0.05 m and 1.0 m only). Overlay and anomaly scanner keep those thresholds; **SURVEY QC tab** and cross-platform summaries use this contract.

## User-facing summary (English)

- **No loops:** `Loops: none detected — log closing shots to known stations to measure misclosure.`
- **With loops:** worst |Δ|, severity tier, worst station cycle (`A→B→C→A`), path length, optional ppm; when multiple loops, total |Δ| across all loops.

## Implementations

| Platform | Detection | Summary UI | Adjustment |
|----------|-----------|------------|------------|
| **Web** | `src/utils/surveyLoopQc.js` | Workspace QC panel, Cave AI `survey_qc_summary` | Compass / WLS in Loop closure assistant (`surveyLoopClosureAdjuster.js`) |
| **Android** | `SurveyLoopQcSummary.kt` → `analyzeSurveyLoops` | Offline assistant, exports, telemetry | — (office adjust on Windows / web) |
| **Windows** | `Services/SurveyLoopQc/SurveyLoopQcAnalyzer.cs` | SURVEY QC tab (read-only) | Loop closure assistant (Compass / WLS) |

## Parity tests

Shared fixture: [test-fixtures/survey-loop-qc-fixture.json](./test-fixtures/survey-loop-qc-fixture.json)

- Web: `npm run test:survey-loop-qc` · `npm run test:survey-loop-adjust`
- Windows: `SurveyLoopQcContractTests` (MSTest)

## Deferred

- Live multi-loop telemetry sync Android → web workspace.
