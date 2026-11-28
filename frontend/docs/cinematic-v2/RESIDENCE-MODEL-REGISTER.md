# BuildMind Cinematic V2 — Residence Model Register

| ID | Item | Version | Source/format | Status |
| --- | --- | --- | --- | --- |
| BM-CV2-RES-001 | Authoritative geometry | 1 | Original BuildMind procedural data / JSON | Complete |
| BM-CV2-CAM-001 | Camera register | 1 | Original JSON | Complete; nine cameras registered in Blender |
| BM-CV2-TECH-PLAN | Plan registration preview | 1 | Generated SVG | Complete; technical evidence |
| BM-CV2-TECH-MASS | Massing registration preview | 1 | Generated SVG | Complete; technical evidence |
| BM-CV2-MODEL-HQ | Residence source model | v1 | Blender 4.5.17 LTS | Model complete; final visual refinement pending |
| BM-CV2-WEB | Optimized web derivative | — | GLB optional | Not started; Phase 05 decision |
| BM-CV2-RENDERS | Exterior/interior masters | R3 evaluation | PNG | Review-only; final production pending |

## BM-CV2-RES-001-v1

| Field | Value |
| --- | --- |
| Blender file | `C:\Users\kogul\Tools\BuildMind-Rendering\production\BM-CV2-RES-001\BM-CV2-RES-001-v1.blend` |
| Size | 1,907,077 bytes |
| SHA-256 | `01ea9889b676a91e948863df7a84fa0825ed3390a49053873c78f4f61cf31ab9` |
| Generator | `build_residence.py` 1.0.2 |
| Blender | 4.5.17 LTS |
| Objects | 219 |
| Mesh objects | 205 |
| Polygons | 1,394 |
| Materials | 12 |
| Cameras | 9 |
| Lights | 5 |

Collections cover site/parcel, ground and upper floors, walls/slabs, roofs/overhangs, doors/windows, glazing, timber screens, furnishings, pool/landscape, materials, lights, and registered cameras.

Continuity evidence: the model preserves the 16×9 metre footprint, six parcel vertices, seven recorded ground-floor zones, fourteen wall runs, their recorded openings, both authoritative roof slabs, eighteen upper louvres, pool bounds, six tree anchors, five path stones, and the reserved stair volume. Exterior R2/R3 renders show continuous roof plates, opening placement, timber screening, pool, and entry path. Interior R2/R3 renders retain the living-to-verandah glazing relationship.

Known limitation: furnishing, planting, and material detail are intentionally lightweight and illustrative. The model is not construction documentation and should not be presented as customer-specific output.

## BM-CV2-RES-001-v2

`C:\Users\kogul\Tools\BuildMind-Rendering\production\BM-CV2-RES-001\BM-CV2-RES-001-v2.blend` is 2,503,324 bytes, SHA-256 `cb75cb19c3205ffd76ea88ed1c333339f842e24dc2dd73ef4834ae7c7187d90a`. Generator 1.1.1 creates 274 objects, 260 meshes, 5,176 polygons, 12 materials, nine cameras and five lights. V1 remains unchanged at SHA-256 `01ea9889b676a91e948863df7a84fa0825ed3390a49053873c78f4f61cf31ab9`.
