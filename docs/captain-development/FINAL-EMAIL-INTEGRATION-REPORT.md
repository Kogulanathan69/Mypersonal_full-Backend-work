# Final email integration report

| Status | Detail |
|---|---|
| **Verified-TLS delivery: COMPLETE_AND_VERIFIED locally** | |
| **External provider delivery: IMPLEMENTED_AWAITING_EXTERNAL_ACTIVATION** | needs E-3 provider and credentials, and E-4 an approved recipient. No email was sent to any real recipient. |

## The previous container failure (110/111) and its fix

- **Before:** inside containers the capture host (`mailpit`) is not loopback. The SMTP policy correctly refused non-loopback delivery without `AllowExternalDelivery` + `UseStartTls`, so container runs had no email.
- **Fix, without weakening the policy:** the container capture now behaves like a real provider.
  - Mailpit runs with `--smtp-require-starttls` and a certificate for `DNS:mailpit`, issued by the Captain server CA.
  - The API container trusts that CA through the standard OpenSSL `SSL_CERT_FILE` bundle (system CAs + Captain CA).
  - `SmtpClient` therefore uses **normal platform certificate validation**: no validation callback, no bypass.
  - The API runs with `AllowExternalDelivery=true` and `UseStartTls=true`, exactly the production rule.
- **Result:** container journey phases 2,3,4,6,8,9,10,11 gave **216/216**, including "outbox delivered the notification email to the local mail capture" (`MAILPIT_API=http://127.0.0.1:8026/api/v1`).

## Delivery policy (unchanged, unit-tested)

`SmtpDeliveryPolicyTests` 7/7:
- loopback capture allowed;
- external host refused unless `AllowExternalDelivery` **and** `UseStartTls`;
- password read from `PasswordFile` only;
- logs record failure types only.

## Transactional notifications (all via the outbox, in the same transaction as the state change)

| Notification | Verified |
|---|---|
| Password recovery email (hashed single-use token, enumeration-safe responses) | process stack (Mailpit), journey 9 / admin smoke |
| Report ready (`REPORT_READY`) | journey 9 (process and containers) |
| Architect request created / responded / feedback | journey 9 |
| Request withdrawn (`ARCHITECT_REQUEST_WITHDRAWN`) | journey 9 |
| Delivery failure handling | outbox retry with `NextAttemptAtUtc` backoff; `SENDING` leases reclaimed after 5 min; business state is never rolled back by a mail failure |
| Idempotency | per-notification idempotency keys (for example `report-ready:{reportId}`) |

## External activation steps (E-3/E-4)

1. Place the password in `~/bm-captain/secrets/smtp.password` (0600).
2. Set `SmtpHost` / `SmtpPort` / `UserName` / `PasswordFile` / `FromAddress` (a verified sender), `UseStartTls=true` and `AllowExternalDelivery=true`.
3. Run the recovery and notification journeys to the approved recipient, and record the provider message ids.
