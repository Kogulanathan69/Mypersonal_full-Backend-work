# Real email integration report

**Verdict: IMPLEMENTED_AWAITING_EXTERNAL_ACTIVATION.**
- External delivery was **not** performed: there is no authorized SMTP provider, credentials, sender domain or authorized recipient (inputs I-2 and I-3).
- Delivery through the local capture (Mailpit) is verified. Per the order, a local capture is **not** external delivery.

## What is implemented

| Area | Detail |
|---|---|
| Sender | `SmtpEmailSender` (`ISystemEmailSender`, `IPasswordResetEmailSender`), configured under `CaptainIntegration:Email:` (`SmtpHost`, `SmtpPort`, `FromAddress`, `UserName`, `PasswordFile`, `UseStartTls`, `AllowExternalDelivery`) |
| Delivery policy | **Loopback** host → allowed (local capture). Non-loopback host → allowed only when `AllowExternalDelivery=true` **and** `UseStartTls=true`. Plaintext external SMTP is refused (the sender reports not configured). |
| TLS | STARTTLS with platform certificate validation. No certificate-validation override exists for SMTP: the only override in the codebase is the strict internal mTLS validator. |
| Credentials | SMTP password read from a file (`PasswordFile`), never from configuration values or source; never logged |
| Logging | failure type only (`Email delivery failed ({Type})`); no recipients, subjects or bodies |
| Timeout | 15 s per send |
| Dispatch | Transactional outbox: notifications and their email jobs are written in the same transaction as the business state change. The hosted outbox worker delivers them with retry; delivery failure never rolls back business state. |
| Password recovery | reset email through the same sender (enumeration-safe responses) |

## Notification coverage

| Event | Kind | Recipient | Verified |
|---|---|---|---|
| Architect review requested | (request notification) | architect | journey phase 9 (process and container) |
| Architect accepted / responded / feedback / revision request | `ACCEPTED`, `ARCHITECT_RESPONSE`, `ARCHITECT_FEEDBACK`, `REVISION_REQUEST` | customer / architect | journey phase 9 |
| **Consent withdrawn** (new) | `ARCHITECT_REQUEST_WITHDRAWN` | architect | container journey run B: PASS |
| **Report ready** (new) | `REPORT_READY` | customer | container journey run B: PASS |
| Password reset | email only | account holder | journey phase 9 (Mailpit) |

Generation completion has **no** notification. Generation is synchronous: the request returns its three alternatives or a truthful failure directly, so a notification would duplicate the response.

## Evidence

- `SmtpDeliveryPolicyTests` 7/7:
  - loopback IPv4/`localhost` allowed;
  - external host refused when not allowed, when allowed without STARTTLS, and when STARTTLS is false;
  - external host accepted with both flags;
  - a missing sender address is not configured.
- Process stack (Mailpit on loopback): the outbox delivered the notification email (journey phase 9, earlier runs) and the password-reset email.
- Container stack: notifications were created (110/111). Email is **not** wired there, because the capture host is not loopback and the policy correctly refuses it (see DOCKER-CONTAINER-VERIFICATION).

## To activate external delivery (operator actions, I-2/I-3)

1. An authorized SMTP relay with STARTTLS and a sender domain with SPF, DKIM and DMARC.
2. The password supplied as a secret file (`PasswordFile`) through the deployment secret store, not in chat or source.
3. Set `AllowExternalDelivery=true` and `UseStartTls=true`.
4. Send to authorized test recipients first, and record message ids and provider logs as evidence.

Not done: bounce or complaint handling, unsubscribe preferences, and templated HTML emails (plain text only).
