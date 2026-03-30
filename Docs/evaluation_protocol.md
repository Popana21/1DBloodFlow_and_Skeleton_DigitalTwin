# Evaluation Protocol and Metrics (Implemented)

This document describes the evaluation pipeline implemented in `Assets/Scripts/Evaluation/EvaluationRunner.cs`.

## 1) What Was Implemented

The `EvaluationRunner` exports a JSON report with:

- Topology integrity metrics
- Ontology/side consistency metrics
- Layout rule compliance metrics
- Geometry readability metrics (crossings/overlaps)
- Runtime performance metrics (frame-time/FPS/memory delta)

Output location at runtime:

- `Application.persistentDataPath/Evaluation/evaluation_report.json`

## 2) How to Run in Unity

1. Add `EvaluationRunner` to an active GameObject in the scene that already includes:
- `EcsWorldBootstrap`
- `NetworkLoadSystem`
- `ChainMappingSystem`
- `VesselBuildSystem`
- `SegmentAlignSystem`

2. In `EvaluationRunner` inspector:
- assign `chainMappingSystem` (recommended)
- assign `projectionCamera` (optional, for 2D crossing metric in your preferred view)
- keep `runOnStart = true`

3. Press Play and wait until evaluation completes.

4. Copy `evaluation_report.json` into your thesis data folder.

## 3) Metric Definitions

### Topology

- `jsonVesselCount`, `worldVesselCount`
- `jsonNodeCount`, `worldNodeCount`
- `vesselCoveragePct`, `nodeCoveragePct`
- `missingVesselCount`, `extraVesselCount`, `missingNodeCount`
- `endpointMismatchCount`, `lengthMismatchCount`
- `duplicateLabelCount`

### Ontology

- Region count distribution (`regionCounts`)
- Side count distribution (`sideCounts`)
- Side suffix consistency:
  - `sideSuffixKnownCount`
  - `sideSuffixMatchCount`
  - `sideSuffixMatchPct`

### Layout (algorithm compliance)

- `bifurcationCount`
- `primaryAlignmentMeanAbsErrorDeg`
- `branchAngleMeanAbsErrorDeg`
- `torsoPerpendicularMeanAbsErrorDeg`
- `sideRuleCompliancePct`
- `verticalUpCompliancePct` (head/neck upward direction)

### Geometry readability

- `crossingCount2D` (projected with `projectionCamera`, or XY fallback)
- `overlapCount` (3D segment proximity threshold)

### Runtime

- `meanFrameMs`, `p95FrameMs`, `p99FrameMs`
- `meanFps`
- `monoMemoryDeltaMb`

## 4) Baseline Static Dataset Facts (Current Project JSON)

From `Assets/StreamingAssets/vessels.json`:

- Vessels: `77`
- Unique nodes: `78`
- Side-labeled right vessels: `27`
- Side-labeled left vessels: `28`
- Midline/unknown side labels: `22`

These are static input facts (pre-runtime) and should match topology-oriented report fields.

## 5) Interpretation Guide (for Thesis)

- High `vesselCoveragePct` and `nodeCoveragePct` support structural correctness.
- Low `endpointMismatchCount` and `lengthMismatchCount` support data fidelity.
- High `verticalUpCompliancePct` (head/neck) supports anatomical realism.
- High `sideRuleCompliancePct` supports branch-side placement logic.
- Lower `crossingCount2D` and `overlapCount` indicate improved visual readability.
- Stable `p95FrameMs` and high `meanFps` support practical usability.

## 6) Suggested Acceptance Targets

- `vesselCoveragePct = 100%`
- `nodeCoveragePct = 100%`
- `missingVesselCount = 0`
- `missingNodeCount = 0`
- `endpointMismatchCount = 0`
- `verticalUpCompliancePct >= 95%`
- `sideRuleCompliancePct >= 90%`
- `crossingCount2D` and `overlapCount` lower than baseline mapper

## 7) Reporting Template (Copy Into Thesis)

Use the generated JSON to fill a compact results table with:

- Topology correctness
- Anatomical/layout correctness
- Visual readability
- Runtime performance

Then add a short interpretation paragraph:

- Which metrics improved
- Which constraints remain challenging
- Why this supports relevance/usefulness compared with prior tools
