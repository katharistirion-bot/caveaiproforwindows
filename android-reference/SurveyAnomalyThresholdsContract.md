# Survey anomaly thresholds (Android ↔ Windows)

Normative shared constants for offline QC. Android uses a **pre-commit guard** on the next leg; Windows uses a **batch MAD scan** over all traverse legs. Values below must stay aligned across repos.

| Constant | Windows (`SurveyAnomalyThresholds.cs`) | Android (`SurveyAnomalyThresholds.kt`) | Notes |
|----------|----------------------------------------|----------------------------------------|-------|
| MAD multiplier | `MadMultiplier = 3.5` | — | Batch scan only (Windows) |
| MAD critical factor | `MadCriticalRelativeFactor = 1.4` | — | `z > MadMultiplier × 1.4` → CRITICAL |
| Min series sample | `MinSeriesSampleCount = 4` | — | MAD needs ≥4 legs |
| Loop skip (m) | `LoopMisclosureSkipMetres = 0.05` | — | Same as `LoopClosureSeverityClassifier.GoodThresholdMetres` |
| Loop warning (m) | `LoopMisclosureWarningMetres = 0.35` | — | Anomaly INFO/WARNING boundary |
| Loop critical (m) | `LoopMisclosureCriticalMetres = 1.0` | — | Same as `LoopClosureSeverityClassifier.LargeThresholdMetres` |
| Distance outlier factor | `AndroidDistanceOutlierMeanFactor = 3.0` | `DistanceOutlierMeanFactor = 3f` | Conceptual parity with ~3.5 MAD |
| Clino jump (°) | `AndroidClinoJumpDegrees = 40.0` | `ClinoJumpDegrees = 40f` | Pre-commit guard |
| Min prior traverse | — | `MinPriorTraverseLegs = 3` | Android only |
| Recent window | — | `RecentWindowLegs = 8` | Android only |
| Reverse azimuth band | — | `170°–190°` | Android only |
| Depth jump factor | — | `DepthJumpFactor = 3f` | Android only |

**Change process:** update both repos and this table in the same release when adjusting breakpoints.
