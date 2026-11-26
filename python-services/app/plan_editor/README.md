# BuildMind - M3 Python Plan Editor

Task: BE-M3-D01
Owner: Member 3 (revised backend assignment)
Status: PROVISIONAL IMPLEMENTATION - LOCALLY TESTED

## Responsibilities

- Candidate geometry transformations.
- Room addition and removal.
- Room movement and resizing.
- Wall adjustments.
- Door and window operations.

## Folder Structure

- schemas: Provisional editor data models.
- operations: Independent geometry transformations.

## Safety Principles

- Never mutate the original plan silently.
- Operate on a candidate copy.
- Preserve stable geometry identifiers.
- Delegate authoritative validation to M6.
- Reject invalid candidates without persistence.

## Restrictions

- No duplicate authoritative validator.
- No direct MySQL persistence.
- No shared application bootstrap changes.
- No production route registration before approval.

Canonical schema and integration approval are pending.

Current implementation provides strict draft schemas and pure deep-copy
candidate operations. Shared-wall neighbor adjustment rejects conservatively
until the topology policy is approved. See the M3 progress record for executed
test evidence and limitations.
