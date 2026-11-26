> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# BE Second Brain – Question & Answer Register

### BE-Q-001: When may initial rules checks run?

**Answer:** After survey upload, confirmed location and submitted house requirements.

**Knowledge status:** Confirmed Requirement

**Source:** [[RULES-ENGINE]] and [[FUNCTIONAL-REQUIREMENTS]] where relevant.

**Implementation evidence:** Not verified against repository.

### BE-Q-002: Can upload run compliance analysis?

**Answer:** No; upload is followed by Mapbox location then requirements.

**Knowledge status:** Confirmed Requirement

**Source:** [[SURVEY-PIPELINE]] and [[FUNCTIONAL-REQUIREMENTS]] where relevant.

**Implementation evidence:** Not verified against repository.

### BE-Q-003: Are API URLs already documented?

**Answer:** No; proposed API capabilities exist but exact routes/DTOs have not been verified.

**Knowledge status:** Unknown / Gap

**Source:** [[API-BOUNDARIES]] and [[FUNCTIONAL-REQUIREMENTS]] where relevant.

**Implementation evidence:** Not verified against repository.

### BE-Q-004: Is Python FastAPI approved?

**Answer:** No. It is recommended, pending approval.

**Knowledge status:** Recommended / Pending

**Source:** [[AI-PLAN-GENERATION]] and [[FUNCTIONAL-REQUIREMENTS]] where relevant.

**Implementation evidence:** Not verified against repository.

### BE-Q-005: Can LLM output establish building-rule compliance?

**Answer:** No. Compliance needs verifiable source-based deterministic checks and review where necessary.

**Knowledge status:** Proposed Safety Principle

**Source:** [[RULES-ENGINE]] and [[FUNCTIONAL-REQUIREMENTS]] where relevant.

**Implementation evidence:** Not verified against repository.

### BE-Q-006: Is Mapbox legally authoritative zoning data?

**Answer:** No; independent verified geographic/regulatory source is required.

**Knowledge status:** Knowledge Limitation

**Source:** [[JURISDICTION]] and [[FUNCTIONAL-REQUIREMENTS]] where relevant.

**Implementation evidence:** Not verified against repository.

### BE-Q-007: Which SQL Server tables exist?

**Answer:** Unknown; entity list is only proposed; no schema or migrations inspected.

**Knowledge status:** Unknown / Gap

**Source:** [[DATA-MODEL]] and [[FUNCTIONAL-REQUIREMENTS]] where relevant.

**Implementation evidence:** Not verified against repository.

### BE-Q-008: What protects paid downloads?

**Answer:** Backend verifies ownership, finalization and eligible subscription before export.

**Knowledge status:** Confirmed Rule + Proposed Design

**Source:** [[PAYMENT-DOWNLOAD]] and [[FUNCTIONAL-REQUIREMENTS]] where relevant.

**Implementation evidence:** Not verified against repository.

### BE-Q-009: What is shared between AI/manual plans?

**Answer:** A common structured geometry representation is recommended, not yet approved.

**Knowledge status:** Proposed Design

**Source:** [[FLOOR-PLAN-GEOMETRY]] and [[FUNCTIONAL-REQUIREMENTS]] where relevant.

**Implementation evidence:** Not verified against repository.

### BE-Q-010: Which Sri Lankan locality is covered?

**Answer:** Unknown; initial authority/jurisdiction and exact numeric rules need authoritative confirmation.

**Knowledge status:** Unknown / Gap

**Source:** [[RULES-ENGINE]] and [[FUNCTIONAL-REQUIREMENTS]] where relevant.

**Implementation evidence:** Not verified against repository.
