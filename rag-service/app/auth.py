"""Internal service authentication (G11 MVP, lead-approved 2026-11-19): per-service shared secret.

- The C# API sends the secret in `X-BuildMind-Internal-Token` (never `Authorization`: user credentials are never
  forwarded to internal services).
- Up to two accepted tokens allow rotation without downtime.
- Comparison is constant-time (`hmac.compare_digest`).
- FAIL CLOSED: authentication is required unless explicitly disabled for local development
  (`BUILDMIND_RAG_AUTH=disabled`). Required with no token configured means every protected endpoint answers 503;
  it never falls back to open access.
- Tokens come only from the environment and are never logged or echoed.
"""
import hmac
import os
from dataclasses import dataclass

AUTH_HEADER = "X-BuildMind-Internal-Token"
TOKENS_VARIABLE = "BUILDMIND_RAG_INTERNAL_TOKENS"
MODE_VARIABLE = "BUILDMIND_RAG_AUTH"
ENVIRONMENT_VARIABLE = "BUILDMIND_RAG_ENVIRONMENT"
MIN_TOKEN_LENGTH = 32
MAX_TOKEN_LENGTH = 256
MAX_TOKENS = 2


def _valid(token: str) -> bool:
    return MIN_TOKEN_LENGTH <= len(token) <= MAX_TOKEN_LENGTH and all(" " < c < "\x7f" for c in token)


@dataclass(frozen=True)
class InternalAuth:
    tokens: tuple[bytes, ...]
    required: bool = True

    @staticmethod
    def from_tokens(tokens, required: bool = True) -> "InternalAuth":
        values = [t for t in tokens]
        if len(values) > MAX_TOKENS or not all(isinstance(t, str) and _valid(t) for t in values):
            # Never include the configured values in the message.
            raise ValueError(f"Internal tokens must be 1-{MAX_TOKENS} values of {MIN_TOKEN_LENGTH}-{MAX_TOKEN_LENGTH} printable characters")
        return InternalAuth(tuple(t.encode("ascii") for t in values), required)

    @staticmethod
    def from_environment() -> "InternalAuth":
        mode = os.environ.get(MODE_VARIABLE, "required").strip().lower()
        raw = os.environ.get(TOKENS_VARIABLE, "")
        tokens = [t.strip() for t in raw.split(",") if t.strip()]
        if mode == "disabled" and os.environ.get(ENVIRONMENT_VARIABLE, "").strip().lower() != "development":
            # A single setting must never be able to switch authentication off in a deployed service.
            raise RuntimeError(f"{MODE_VARIABLE}=disabled requires {ENVIRONMENT_VARIABLE}=development")
        return InternalAuth.from_tokens(tokens, required=(mode != "disabled"))

    def check(self, presented: str | None) -> str | None:
        """Returns None when the request may proceed, otherwise the error code."""
        if not self.required:
            return None
        if not self.tokens:
            return "AUTH_NOT_CONFIGURED"
        candidate = (presented or "").encode("utf-8", errors="replace")
        matched = False
        for token in self.tokens:  # compare against every token (no early exit)
            matched |= hmac.compare_digest(candidate, token)
        return None if matched else "UNAUTHENTICATED"


REQUIRED_WITHOUT_TOKENS = InternalAuth((), True)
