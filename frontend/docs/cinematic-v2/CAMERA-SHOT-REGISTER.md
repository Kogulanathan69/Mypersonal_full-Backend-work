# BuildMind Cinematic V2 — Camera Shot Register

All transforms use metres and geometry `BM-CV2-RES-001`. Machine-readable values are in `asset-production/cinematic-v2/cameras.json`.

| Shot | Purpose | Position → target | Lens | Master | Light | Responsive strategy |
| --- | --- | --- | ---: | --- | --- | --- |
| 01 | Survey registration | `[-6,35,35] → [-13,0,3]` | 38 mm | 1920×1080 | Technical neutral | Parcel inside central 80% |
| 02 | Blueprint registration | `[1,33,7] → [0,0,0]` | 45 mm | 1920×1080 | Technical neutral | Keep dimensions clear |
| 03 | Wireframe registration | `[25,15,24] → [0,2.2,0]` | 38 mm | 1920×1080 | Technical neutral | Preserve roof and ground contact |
| 04 | Material threshold | `[-22,9.5,22] → [0,2.6,0]` | 35 mm | 1920×1080 | Late afternoon | Exact locked A/B registration |
| 05 | Exterior hero | `[-13,4.6,21] → [0.5,2.8,0]` | 32 mm | 2400×1350 | Late afternoon/blue hour | Protect verandah and upper screen in 4:5 |
| 06 | Exterior approach | `[2.5,1.65,13] → [2.5,1.8,3.2]` | 35 mm | 1920×1080 | Late afternoon | Path and entrance survive portrait crop |
| 07 | Interior reveal | `[-4.8,1.5,-2.6] → [-4.2,1.45,2.2]` | 26 mm | 1920×1080 | Warm interior/daylight | Glazing and verandah show continuity |
| 08 | Interior journey | `[-6.2,1.5,-2.7] → [-3.8,1.45,2.0]` | 26 mm | 1920×1080 | Warm interior/blue hour | Slow forward move; mobile uses still |
| 09 | Editorial transition | `[-10,3.4,8] → [-3,2.5,1]` | 50 mm | 1920×1080 | Soft detail | Roof or timber line bridges to ivory |

Final view framing, lens equivalence, camera clipping, mobile crops, and matched-pixel registration remain unverified until the model is rendered in Blender. No unrelated building may replace any shot.

## Phase 04U render verification

All nine camera records were imported with the explicit source-to-Blender coordinate conversion and preserved focal lengths. SHOT-04, SHOT-05, SHOT-06, SHOT-07, and SHOT-08 were rendered from their registered transforms. SHOT-04 and SHOT-05 establish a coherent exterior silhouette; SHOT-06 preserves the entrance and stepping-stone approach but is a deliberately tight composition; SHOT-07/08 remain inside the living zone and look through the recorded verandah glazing line.

SHOT-04 uses the same single registered camera object for material and future technical passes, but an exact pixel-difference comparison against the earlier Three.js capture was not performed. Exact pixel registration is therefore not claimed. SHOT-01/02/03/09 were imported and counted but not rendered in the Phase 04U ladder. Responsive/mobile crops remain pending because the visual-quality gate did not pass.

## Phase 04V registration measurement

Camera records remain unchanged. Four manually measured SHOT-04 silhouette landmarks produce approximately 88.8 px RMS deviation at 640×360 versus the scaled Three.js capture, or 0.121 of the image diagonal (±3 px landmark uncertainty). The mismatch is dominated by framing/FOV. No exact-pixel claim or derived camera was made.
