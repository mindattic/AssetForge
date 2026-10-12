# AGENTS.md: AssetForge

Shared workflow: read [`../MINDATTIC_AGENT.md`](../MINDATTIC_AGENT.md) and `../mindattic-agent-standard/AGENTS.md`. This file adds project context only.

## Purpose

Shared art-pipeline tooling for MindAttic projects, so background cleanup / crop framing / mesh
normalization aren't re-solved inside every game repo. First consumer: GridGameGaiden's new-portrait
import pipeline.

## Rules

- `MindAttic.AssetForge` is pure image-domain logic — no game-specific knowledge (no `classes.json`, no
  one project's UI-geometry math). Project-specific orchestration belongs in that project's own repo,
  calling into this library/CLI, not in here.
- Whole-number package versions (HOUSE-LAW-1). Done means clean build + green tests (HOUSE-LAW-8).
  Commit to main (HOUSE-LAW-10).
- `BackgroundCleaner`'s `Auto` mode has a known limitation on fine internal line art — see README's
  "Known limitation" before trying to make the area-cutoff heuristic smarter; bridging across thin lines
  was tried and reverted because it risks merging true background through the subject's own outline.
