# Render Benchmark Register

All Phase 04U renders used Blender 4.5.17 LTS, Cycles CPU, two threads, adaptive sampling, CPU denoising, and conservative bounce limits.

| Stage | Shot | Resolution | Samples | Blender render time | Result |
| --- | --- | ---: | ---: | ---: | --- |
| R1 | SHOT-05 | 480×270 | 8 | 14.46 s process elapsed | Complete |
| R1 | SHOT-07 | 480×270 | 8 | 17.12 s process elapsed | Complete; composition defect found |
| R1b | SHOT-07 | 480×270 | 8 | 19.65 s process elapsed | Complete after bounded fix |
| R2 | SHOT-04 | 640×360 | 16 | 20.66 s | Complete |
| R2 | SHOT-05 | 640×360 | 16 | 22.45 s | Complete |
| R2 | SHOT-06 | 640×360 | 16 | 31.70 s | Complete |
| R2 | SHOT-07 | 640×360 | 16 | 45.97 s | Complete |
| R2 | SHOT-08 | 640×360 | 16 | 44.66 s | Complete |
| R3 | SHOT-05 | 960×540 | 32 | 79.77 s | Complete |
| R3 | SHOT-08 | 960×540 | 32 | 176.80 s | Complete, near bounded ceiling |

Blender reported a 96.37 MiB render peak for both R3 renders. R1 process working-set measurements were approximately 347 MiB. No Blender process remained after rendering. Temperature telemetry remained unavailable. R4 and R5 were not attempted.

## Phase 04V v2

| Stage | Shot | Settings | Time | Blender peak |
| --- | --- | --- | ---: | ---: |
| R2 | 04 | 640×360, 16 | 24.38 s | 58.98 MiB |
| R2 | 05 | 640×360, 16 | 26.31 s | 58.98 MiB |
| R2 | 08 | 640×360, 16 | 49.61 s | 58.98 MiB |
| R3 | 05 | 960×540, 32 | 94.04 s | 101.83 MiB |
| R3 | 08 | 960×540, 32 | 198.36 s | 101.83 MiB |

V2 added 14.27 s exterior and 21.56 s interior versus v1 R3. Reported peak increased about 5.46 MiB. No instability occurred.
