# Architect directory and portal contract (Captain-local; G09 portal scope unresolved → deny by default)

## Directory (admin)

| Route | Rule |
|---|---|
| `POST /admin/architects {userId, displayName, registrationNumber?}` | The user must hold the ARCHITECT role. The profile starts UNVERIFIED and unlisted. |
| `POST /admin/architects/{recipientId}/verification {status, reference, expectedRowVersion}` | `VERIFIED` requires an evidence `reference`. It is an **admin assertion of the cited evidence; BuildMind does not certify professional registration**. Non-VERIFIED status unlists the profile. |
| `PUT /admin/architects/{recipientId}/listing {isListed, acceptingRequests, expectedRowVersion}` | Listing requires VERIFIED. |

## Customer side

| Route | Behaviour |
|---|---|
| `GET /architects` | Listed + accepting + verified + active entries only: `recipientId`, `displayName`, `verificationStatus` (no contact data). |
| `POST /projects/{id}/architect-requests` | Existing M5 flow (explicit consent, policy version, Ready report). The recipient must be a synthetic file entry or an eligible directory entry. The architect is notified in the same transaction. |
| `GET /projects/{id}/architect-requests/{requestId}/conversation` | Response + feedback (owner only). |
| Withdraw (existing M5 transition) | Immediately removes the architect's access. |

## Portal (ARCHITECT role + own directory profile)

| Route | Behaviour |
|---|---|
| `GET /architect/dashboard`, `GET /architect/requests`, `GET /architect/requests/{id}` | Only requests addressed to the caller's profile that are still Pending (not withdrawn). |
| `GET /architect/requests/{id}/report` | Streams **only** the consented, Ready, integrity-verified report; audited `ARCHITECT_REPORT_ACCESS`. |
| `POST /architect/requests/{id}/respond {decision: ACCEPTED\|DECLINED, note?}` | Once only (409 after). Stored in `architect_request_responses`; the M5 request state is unchanged. Customer notified. |
| `POST /architect/requests/{id}/feedback {kind: FEEDBACK\|REVISION_REQUEST, body}` | Only after ACCEPTED. Customer notified. |

There is no access to projects, plans, surveys or any other document.

## Delivery

In-app notifications plus a transactional email **outbox**:
- delivery is at-least-once, deduplicated by key;
- failures back off and are retried up to 5 times before dead-lettering;
- stuck `SENDING` messages are recovered.

Email is delivered only to the local Mailpit capture unless `CaptainIntegration:Email:AllowExternalDelivery` is set, which requires approval. **No document is ever attached to an email.**

## Evidence

Journey phase 9: directory rules, request, PDF access, accept, double-response 409, feedback ordering, customer conversation, notifications, Mailpit delivery, withdrawal revoking access.

## Open decisions

Architect company accounts, eligibility rules, retention and sharing channels (G09/G12).
