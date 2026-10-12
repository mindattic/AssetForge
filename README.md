# AssetForge

**Work in progress.** Shared art-pipeline tooling for MindAttic projects — background/whitespace cleanup
and portrait crop framing today, mesh normalization planned. One engine, consumed by every project's own
CLI wrapper instead of re-solved per repo.

## Packages

- `MindAttic.AssetForge` — the library. Background cleanup (Color-to-Alpha un-mixing, border-vs-enclosed
  blob erasure) and portrait crop framing (`face_crop`/`figure_crop` heuristics).
- `MindAttic.AssetForge.Cli` (`assetforge`) — command-line wrapper over the library:

  ```
  assetforge clean <input.png> <output.png> [--fuzz 3] [--hole-area 2] [--mode Auto|BorderOnly|Skip] [--border-width 12] [--dilate 2]
  assetforge crop  <input.png> [--kind Face|Figure]
  ```

## Status

First consumer: GridGameGaiden's new-portrait import pipeline. Mesh support (normalize orientation/scale
from known asset-pack conventions, reformat to glTF) is tracked but not yet built — see that project's
`docs` for the originating plan.

## Known limitation

`BackgroundMode.Auto`'s small-enclosed-blob erasure can still misfire on art with fine internal line work
(crosshatching, panel cracks) that are a similar stroke width to the subject's own outline — the lines
can fragment one large pale region into many small, individually "small enough" pockets. There is no
general automatic fix for this that doesn't risk bridging real background through the outline itself
(tried and reverted — see git history); per-file review and a `BorderOnly`/`Skip` mode override remains
the intended answer for images that hit this.

## Licensing note

`SixLabors.ImageSharp` requires a commercial license for Release builds as of the version pinned here
(v4) — Debug builds work without one. Resolve this (license, pin to an older Apache-2.0-era version, or
swap to a different imaging library) before this package is actually published/distributed.
