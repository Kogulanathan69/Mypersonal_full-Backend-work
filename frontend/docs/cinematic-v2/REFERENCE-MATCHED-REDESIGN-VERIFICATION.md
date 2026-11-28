# Reference-matched redesign verification

Date: 27 November 2026  
Rollback checkpoint: `f2208afbc13f7ea52ef570e133c7d36e0744b29c`

## Browser implementation

The first viewport is now one continuous architectural field rather than a split SaaS layout. It
uses an ivory navigation surface, charcoal wordmark and navigation text, a two-line editorial serif
headline with an italic second line, restrained gold/charcoal actions, small planning feature icons,
minimal scroll guidance and a right-edge chapter system. The Three.js canvas spans the viewport;
still media uses a directional mask instead of a rounded card, border or shadow.

The scroll sequence remains live and reversible:

1. land boundary;
2. blueprint;
3. wireframe construction;
4. material transformation;
5. exterior;
6. interior;
7. editorial transition;
8. product experience.

## Reference comparison

Aligned with the approved GIF:

- stable navigation and left-aligned editorial lockup;
- two-line serif headline and italic `with confidence.` treatment;
- architecture occupies the environmental field rather than a separate card;
- compact pill actions, vertical chapter markers and top-right scene annotation;
- continuous live Three.js/GSAP sequence rather than the GIF itself.

Intentional difference required by the written brief:

- the implemented palette is warm ivory, charcoal and restrained gold rather than the GIF's dark
  nocturnal ground.

Remaining difference:

- the exterior and interior cannot yet match the GIF's implied photographic production quality.
  This is a rendering/model-detail limitation, not a browser-layout limitation.

## Architectural asset work

Generator `build_residence.py` 1.2.0 loads audited 1K CC0 Poly Haven concrete, fine-grained timber
and residential-garden HDRI inputs. Concrete/timber use diffuse, roughness and OpenGL-normal maps.
Vegetation canopy meshes now use smooth UV spheres instead of faceted icospheres. The authoritative
residence geometry, nine registered cameras, CPU-only Cycles device and two-thread limit remain
unchanged.

Bounded comparisons:

| Render     | Settings            |    Time |       Peak | Result                                                                     |
| ---------- | ------------------- | ------: | ---------: | -------------------------------------------------------------------------- |
| SHOT-05 R1 | 640×360, 16 samples | 37.80 s | 401.35 MiB | Rejected: underexposed and faceted planting                                |
| SHOT-05 R2 | 640×360, 16 samples | 37.51 s | 406.10 MiB | Improved daylight, mapped concrete/timber and smooth planting; review-only |
| SHOT-07 R1 | 640×360, 16 samples | 66.97 s | 406.10 MiB | Improved interior material/light continuity; review-only                   |

The R2 exterior and R1 interior replace the unchanged legacy website stills. They are demonstrably
improved but remain review-scale, not final photographic masters.

## Verification

- typecheck: passed;
- lint: passed;
- raw-colour token scan: 0 violations across 162 files;
- WCAG contrast: 56/56 pairings passed;
- unit tests: 222/222 passed after one unrelated session-dialog timing flake was rerun unchanged;
- production build: passed, 392.66 kB raw / 99.66 kB estimated initial transfer;
- mock build: passed;
- Playwright production browser suite: 4/4 passed serially;
- screenshots regenerated for all eight scenes, desktop widths, mobile widths, mobile navigation and
  reduced motion.
- `evidence/reference-vs-implemented.png` records the approved GIF frame and implemented production
  hero side by side.

## Photographic-quality blocker and practical route

The local two-core CPU can generate controlled review frames, but the authoritative model has only
19,768 polygons after smoothed planting and lacks production botanical assets, high-detail landscape,
bespoke tropical backplates and detailed interior joinery. Higher sample counts would reduce noise,
not supply that missing scene detail. Genuine photographic acceptance therefore requires one of:

1. complete the existing Blender scene on a supported 8-core/32 GB workstation, using audited
   botanical/landscape assets and production-resolution CC0 maps;
2. use a CPU render farm or Blender-compatible cloud renderer after a separate data/licence/security
   review;
3. commission final exterior/interior architectural visualisation while retaining the registered
   camera and geometry package for continuity.

Team Captain visual acceptance remains pending and is not inferred from automated verification.
