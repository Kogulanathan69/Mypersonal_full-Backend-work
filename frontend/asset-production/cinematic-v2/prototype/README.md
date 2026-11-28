# BM-CV2-RES-001 browser-rendering feasibility prototype

This isolated prototype uses the repository's existing Three.js package, authoritative geometry JSON, camera register, and original procedural materials. It does not modify or integrate with the Angular application.

Run `node asset-production/cinematic-v2/prototype/server.mjs`, then open `http://127.0.0.1:4310/asset-production/cinematic-v2/prototype/?shot=SHOT-05&mode=material&quality=standard`.

Modes: `blueprint`, `wireframe`, `material`. Quality: `low`, `standard`, `high`. Capture with `node asset-production/cinematic-v2/prototype/capture.mjs` while the local server is running.

The prototype is a feasibility experiment. It is not a photorealistic production render, customer-specific 3D generation, or construction documentation.
