# BuildMind Cinematic V2 — Asset and Licence Register

## Reference-matched redesign CC0 acquisition — 27 November 2026

The following 1K production inputs were acquired directly from Poly Haven. Poly Haven publishes
these assets under CC0. They are source inputs for the offline Blender production file and are not
served by the Angular application.

| Asset                           | Use                            | Source                                       | Local SHA-256                                                      |
| ------------------------------- | ------------------------------ | -------------------------------------------- | ------------------------------------------------------------------ |
| Concrete diffuse                | Exterior concrete base colour  | `https://polyhaven.com/a/concrete`           | `046c0e2aebe31e6043a6bc074e779f6a345f1d823d0ca1c69446c5cabadefa8a` |
| Concrete roughness              | Exterior concrete microsurface | `https://polyhaven.com/a/concrete`           | `0dc74e17295160cf2fc214bf22a57fddc68f261e2cbacebb706f5b3fa5d07458` |
| Concrete OpenGL normal          | Exterior concrete relief       | `https://polyhaven.com/a/concrete`           | `298a0e93040c9d76d239a894bdf28a9787755ea5f22baa068ffa8075369ee428` |
| Fine-grained wood colour        | Timber screen base colour      | `https://polyhaven.com/a/fine_grained_wood`  | `d171f45ef01bc6e239b00dfaa4961bcd27f7d8e93e3962da3bd3d0ce703d802c` |
| Fine-grained wood roughness     | Timber screen microsurface     | `https://polyhaven.com/a/fine_grained_wood`  | `f281f682e62fe322303dfeaa13dac18b5cb9e113617fe0aca43605b49161d82b` |
| Fine-grained wood OpenGL normal | Timber screen relief           | `https://polyhaven.com/a/fine_grained_wood`  | `ba95eadc009818e161d0f753191f1bacc3fc861e593be0bc6d82b437f9ec8044` |
| Residential garden 1K HDRI      | Natural exterior daylight      | `https://polyhaven.com/a/residential_garden` | `91fe8d936ee73d626120630adaac0a7fac2ad7f93afdf0aa5550788b9e622a0d` |

License reference: `https://polyhaven.com/license` (CC0). Generator 1.2.0 loads the maps only when
all required files are present and records the source paths in the generated Blender material
metadata. Missing files fall back to the existing deterministic procedural materials.

No new visual or font assets were downloaded, generated or purchased in Phase 02. `PROPOSED` entries are not approved for inclusion.

Phase 04 created only original geometry records, scripts, and technical previews from existing BuildMind procedural data. No third-party texture, HDRI, model, font, or image was acquired.

| Asset                           | Version/source                                                     | Licence/status                                                                                               | Intended use                     | Approval                          |
| ------------------------------- | ------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------ | -------------------------------- | --------------------------------- |
| Three.js                        | npm `three@0.187.0`                                                | MIT; existing                                                                                                | Technical WebGL stages           | Existing                          |
| Three line helpers              | included with Three.js 0.187.0                                     | MIT; existing                                                                                                | Progressive architectural lines  | Existing                          |
| GSAP + ScrollTrigger            | npm `gsap@3.15.1`                                                  | GSAP Standard “No Charge”; proprietary; permitted for ordinary commercial web applications, subject to terms | Scroll progress                  | Product/legal acceptance required |
| Poppins                         | `@fontsource/poppins@5.3.0`                                        | SIL OFL 1.1                                                                                                  | Current UI and canvas labels     | Existing/provisional              |
| Procedural residence/parcel     | BuildMind source                                                   | Original code; ownership confirmation recommended                                                            | Existing technical scene and SVG | Existing                          |
| BM-CV2-RES-001 geometry JSON    | Original BuildMind derivative of the existing procedural residence | Internal original work; ownership confirmation recommended; no external redistribution approved              | Phase 04 authoritative geometry  | Technical approval                |
| Phase 04 SVG technical previews | Original Node-generated output from BM-CV2-RES-001                 | Internal original work; not a photorealistic marketing deliverable                                           | Geometry and continuity review   | Technical approval                |
| Phase 04 contact-sheet PNG      | Local Chrome rasterization of original SVG                         | Internal original work; review-only                                                                          | Visual QA evidence               | Technical approval                |

## Phase 04 provenance detail

All entries below were created locally by Codex for BuildMind on 2026-11-25 from existing BuildMind procedural data. Creator/creation method: original JSON or Node.js script output, with the PNG rasterized locally by system Chrome. Ownership basis: work created for this project from existing internal source. Commercial use, modification, and internal redistribution are permitted subject to BuildMind confirming ownership of the underlying procedural source; external redistribution is not approved. Attribution is not required internally.

| Asset identifier / filename                      |  Bytes | SHA-256                                                            | Intended use              | Approval                                        |
| ------------------------------------------------ | -----: | ------------------------------------------------------------------ | ------------------------- | ----------------------------------------------- |
| BM-CV2-RES-001 / `residence.geometry.json`       |  3,674 | `516490c2fc9b04a4d2ba04349c6806a5af313b986729c3b4db3b3344f6851426` | Authoritative geometry    | Technical approval                              |
| BM-CV2-CAM-001 / `cameras.json`                  |  2,355 | `ef6f975131b1062bd9d0eb368c82cbc1137366763d7eb359b74da10c33327dca` | Camera planning           | Technical approval; render verification pending |
| BM-CV2-TECH-CONTACT / `00-contact-sheet.svg`     |  3,270 | `b307db95b804e006aae22037e42085708f7a25f56982697f88900d7f665ccc23` | Technical review          | Review-only                                     |
| BM-CV2-TECH-PLAN / `01-plan-registration.svg`    |  2,247 | `43e2a40e5c90432db855645cd1540bb78f2d293911049a1ab997d09fec14d938` | Plan continuity review    | Review-only                                     |
| BM-CV2-TECH-MASS / `02-massing-registration.svg` |  2,101 | `eca9619945909568b4106c6f1fbdbc066173618fea2db4898a54690d8e4ab29c` | Massing continuity review | Review-only                                     |
| BM-CV2-TECH-CONTACT-PNG / `00-contact-sheet.png` | 45,359 | `6f7b1d5e4033688d6012fb7cadd4e5db410ad2e11fca07e5f3db2d979e9fd640` | Visual QA evidence        | Review-only                                     |

Generation scripts are project source rather than website assets. Their hashes are `1edae147bec5413a2b38d5bf0329b500903496c348fe639057f7405cd85e06a2` (`generate-technical-previews.mjs`, 4,996 bytes) and `798d3680ec9535dc4ab9c3a75ba442ed5ca336059c0534fef297a0814fe2d204` (`validate-assets.mjs`, 1,605 bytes).

No organizational GSAP licence acceptance evidence was created or inferred in Phase 04.

## Phase 04U original Blender production

All Phase 04U model geometry, procedural materials, furnishings, landscaping, lighting, cameras, and renders were generated locally for BuildMind from the authoritative internal geometry and camera records. No external texture, HDRI, model, vegetation pack, furniture asset, font, add-on, cloud service, or AI image generator was used. Ownership remains subject to BuildMind confirming rights in the underlying procedural source. The files are illustrative marketing work, not customer-specific or construction output.

| Asset                      |     Bytes | SHA-256                                                            | Status/use                                 |
| -------------------------- | --------: | ------------------------------------------------------------------ | ------------------------------------------ |
| `build_residence.py` 1.0.2 |    20,657 | `83d707530cfe0df46b685a8d904902581bde10a1fbff35ae6608a23f3e2bef70` | Reproducible project source                |
| `BM-CV2-RES-001-v1.blend`  | 1,907,077 | `01ea9889b676a91e948863df7a84fa0825ed3390a49053873c78f4f61cf31ab9` | Versioned source model; stored outside Git |
| `R3-SHOT-05-960x540.png`   |   579,253 | `7e789a351759e14f9d3d63276fc6099595db6e9e6fd2cb3879f786b0da93a194` | Review-only exterior evaluation            |
| `R3-SHOT-08-960x540.png`   |   589,868 | `5963c29c2eb84ba13c3962d2e41e0365bbc01e0c2f5c2ce830484485842acbdb` | Review-only interior evaluation            |

No organizational GSAP licence acceptance was created or inferred in Phase 04U.
| Manrope | official Google Fonts distribution | SIL OFL 1.1 | Proposed display/navigation font | PROPOSED — do not add yet |
| Source Sans 3 | Adobe Fonts upstream | SIL OFL 1.1 | Proposed body/UI font | PROPOSED — do not add yet |
| Final residence model | Original `BM-CV2-RES-001-v1.blend` | Internal original work from authoritative BuildMind geometry | Scene continuity | TECHNICAL APPROVAL; final visual approval pending |
| Concrete/timber/glass/stone materials | Original Blender procedural nodes | Internal original work; no external maps | Material/render production | TECHNICAL APPROVAL |
| Exterior renders/media | Original local Cycles CPU evaluation renders | Derived from approved residence | Scene 05 | REVIEW-ONLY; production media pending |
| Interior renders/media | Original local Cycles CPU evaluation renders | Derived from approved residence | Scene 06 | REVIEW-ONLY; production media pending |
| HDR/environment lighting | None; procedural Blender world | Internal original work | Offline render production | TECHNICAL APPROVAL |
| Vegetation/furniture | Original lightweight procedural geometry | Internal original work; no downloaded assets | Offline render production | REVIEW-ONLY |

For each future asset record: filename, creator, source URL or contract, acquisition date, exact licence text/version, allowed uses, redistribution limits, modifications, approval owner, SHA-256, and every exported derivative.

Official licence references reviewed in Phase 02:

- GSAP Standard License: `https://gsap.com/community/standard-license/`
- Manrope OFL: `https://github.com/google/fonts/blob/main/ofl/manrope/OFL.txt`
- Source Sans upstream: `https://github.com/adobe-fonts/source-sans`

## Phase 04V provenance

V2 materials, vegetation, furniture, architectural details, procedural sky and comparison renders are original local generator output. No third-party texture, HDRI, model, furniture, vegetation, add-on, cloud renderer or AI image asset was acquired. The v2 source model SHA-256 is `cb75cb19c3205ffd76ea88ed1c333339f842e24dc2dd73ef4834ae7c7187d90a`; it remains outside Git and review-only.

# Integrated web derivatives (2026-11-27)

All `public/assets/cinematic/residence-*.png` files are derivatives of the original, procedurally
generated BM-CV2-RES-001 v2 model. They contain no downloaded third-party assets and require no
external attribution. The source model remains outside Git at
`C:\Users\kogul\Tools\BuildMind-Rendering\production\BM-CV2-RES-001\BM-CV2-RES-001-v2.blend`.

| File                           |   Bytes | SHA-256                                                            | Use                            |
| ------------------------------ | ------: | ------------------------------------------------------------------ | ------------------------------ |
| residence-exterior-desktop.png | 680,633 | `80ABB236D65BCB73C0206BE122AAA05C5A0D3B5C117C84E8E2F3862A53085B6C` | Scene 05 desktop               |
| residence-exterior-mobile.png  | 325,939 | `CC021919EC9099A96C98FAA21C629EA9D5E08A129BE48CDE797A94523883079A` | Scene 05 mobile                |
| residence-interior-journey.png | 657,739 | `381E9889F8776B72AD6E4CA27BCB4BC3AA5446ABBE093DA4676F3D635BD74A5E` | Scene 06 desktop               |
| residence-reduced-motion.png   | 185,284 | `4DCE48B4A8D8705B0C2958A91FE1A621885CEF71923E1950081C2FC4404E69D4` | Reduced-motion/mobile interior |

The source model hash remains
`CB75CB19C3205FFD76EA88ED1C333339F842E24DC2DD73EF4834AE7C7187D90A` (2,503,324 bytes).
The two additional local plates (`residence-approach.png` and `residence-interior-reveal.png`) are
original same-residence alternates and are not requested during the initial route load.

## Final static homepage photograph (2026-11-28)

| Local asset | Creator | Source | Licence | Bytes | SHA-256 | Use |
| --- | --- | --- | --- | ---: | --- | --- |
| `public/assets/marketing/buildmind-villa-pexels-28915352.jpg` | Vero Benedini | [Pexels photo 28915352](https://www.pexels.com/photo/luxury-villa-with-pool-in-tropical-setting-28915352/) | [Pexels licence](https://www.pexels.com/license/) | 819,429 | `AB3ADF6A439A80E744273747CA6EA4E0544B0E45781DA4DC0EB345B6184FB601` | Static public-homepage hero |

The integrated file is a compressed 2400-pixel web derivative of the 7952 × 5304 source image.
Pexels permits free commercial use and modification without required attribution, subject to its
licence restrictions. BuildMind nevertheless retains a visible source credit and does not imply
that the photographed residence is a BuildMind-generated or customer-specific design.
