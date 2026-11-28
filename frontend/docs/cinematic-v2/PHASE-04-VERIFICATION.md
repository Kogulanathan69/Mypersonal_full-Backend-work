# BuildMind Cinematic V2 — Phase 04 Verification

## Recovered state

- Branch: `cinematic-v2`.
- Starting HEAD: `a1a18c569750a2849032a60e77abb9aa66ebd5a5`.
- Starting worktree: clean.
- Original checkpoint and protected backup were not modified.

## Tool readiness

Available: Node.js, Python 3.13, Git, system Chrome. Unavailable: Blender, Blender CLI/Python, FFmpeg, ImageMagick, `cwebp`, `avifenc`, `gltfpack`, Pillow, NumPy, and Trimesh. No software was installed.

The observed CPU is an Intel Core i7-6500U at 2.50 GHz with four logical processors. Intel documents this model as two physical cores/four threads with Intel HD Graphics 520. Disk free space was approximately 95.63 GiB at readiness. RAM and VRAM could not be measured through the restricted system interface. `nvidia-smi` is unavailable, so no NVIDIA/CUDA capability is established.

Blender is essential because it provides one authoritative scene for modelling, physically based materials, lighting, matched cameras, high-quality source renders, and controlled export. Required official installation: Blender 5.2 LTS 64-bit for Windows from `https://www.blender.org/download/`, using the official Windows installer or portable ZIP, with Blender's bundled Python and command-line rendering. Installation requires separate approval. Blender's current Windows minimum is a 64-bit OS, four CPU cores with SSE4.2, 8 GB RAM, and a GPU with 2 GB VRAM plus OpenGL 4.3/Vulkan 1.3; the recommendation is Windows 11, eight CPU cores, 32 GB RAM, and 8 GB VRAM. The observed two-core CPU is below the current four-core minimum, and installed RAM/GPU compatibility remain unverified. This machine should therefore not be approved as the final production renderer without a separate compatibility test; a supported render workstation or render service may be required and would need explicit authorization.

## Completed validation

- Authoritative geometry JSON derived from live procedural data without modifying the application.
- Nine-shot camera register shares geometry version `BM-CV2-RES-001`.
- Seven rooms, 14 wall runs, openings, slabs, louvres, site, pool, planting anchors, and path recorded.
- Opening extents validated against each wall length and height.
- Three deterministic SVG previews generated and hashed.
- Corrected contact sheet reviewed visually after local Chrome rasterization.
- No external assets, licences, uploads, downloads, package changes, or Angular/backend modifications.

## Unmet production gates

The high-quality model, physically based materials, final lighting, exterior/interior renders, mobile crops, reduced-motion production posters, optional sequences, and compressed website derivatives were not produced. Geometry continuity is validated at data/schematic level only; final exterior/interior visual continuity and matched-pixel registration cannot be accepted before actual renders exist.

Status: **PHASE 04 PARTIALLY COMPLETE — ASSET PRODUCTION BLOCKED**.
