> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# BuildMind — Project Overview

- **Project:** BuildMind
- **Document ID:** BM-SB-001
- **Version:** 1.1
- **Status:** Draft; not approved or implementation-verified
- **Prepared:** 2026-11-14

## Purpose and vision
BuildMind is an AI-assisted residential floor planning platform. The longer-term vision is to automate parts of an architect's design work; the current MVP is intentionally limited to **2D residential floor plans** because of time constraints. It does **not** replace professional design, engineering certification, or authority approval.

## Problem being addressed
Initial residential planning can be costly and slow; users may lack clarity about site restrictions or the layout options that meet their needs. BuildMind intends to let users explore and refine alternatives while making supported constraints visible.

## Confirmed MVP process
1. User uploads a land survey plan.
2. User identifies/confirms property location through **Mapbox**.
3. User supplies house requirements.
4. **Only now** the system analyses relevant Sri Lankan building rules using available site/location/requirement data.
5. User chooses **AI generation (three alternatives)** or **manual drawing**.
6. AI path: validate generated designs, select one, optionally edit, revalidate. Manual path: draw, validate, correct, confirm.
7. User confirms final 2D plan; subscription/payment is required to download.
8. AI chatbot assists with queries during the journey.

## Technology
**Confirmed:** Angular; C#/.NET; MSSQL; Mapbox.

**Recommended, unconfirmed:** ASP.NET Core Web API, Mapbox GL JS, OpenAI API, Python FastAPI, Konva.js, PayHere, Azure Blob Storage.

## Expected outcome
A usable web app for site-aware, requirements-based, AI-assisted or manual 2D residential floor planning with scoped checks, edits, subscription gating and file download.

## Open decisions
Target authority/jurisdictions, survey formats/extraction, exact house inputs, AI planning method, drawing geometry model, regulation catalogue, payment plans, download formats, hosting and security review.

## Related documents
[Scope](PROJECT-SCOPE.md) · [Requirements](FUNCTIONAL-REQUIREMENTS.md) · [User flow](../02-User-Experience/USER-FLOW.md)
