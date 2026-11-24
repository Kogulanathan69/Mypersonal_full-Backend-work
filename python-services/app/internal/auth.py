"""Internal service authentication for python-services (G11 pattern, adopted from rag-service/app/auth.py).

Same lead-approved per-service shared-secret design as rag-service (doc 31): the C# API sends the secret in
`X-BuildMind-Internal-Token`; up to two tokens allow rotation; comparison is constant-time; FAIL CLOSED (no token
configured => protected routes answer 503, never open access). Authentication can be disabled only when the service
environment is explicitly `development`. The services are deployed separately, so this small module is a per-service
copy rather than a shared import.
"""
import hmac
import os
from dataclasses import dataclass

AUTH_HEADER = "X-BuildMind-Internal-Token"
TOKENS_VARIABLE = "BUILDMIND_PY_INTERNAL_TOKENS"
MODE_VARIABLE = "BUILDMIND_PY_AUTH"
ENVIRONMENT_VARIABLE = "BUILDMIND_PY_ENVIRONMENT"
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
        values = list(tokens)
        if len(values) > MAX_TOKENS or not all(isinstance(t, str) and _valid(t) for t in values):
            raise ValueError(
                f"Internal tokens must be 1-{MAX_TOKENS} values of {MIN_TOKEN_LENGTH}-{MAX_TOKEN_LENGTH} printable characters"
            )
        return InternalAuth(tuple(t.encode("ascii") for t in values), required)

    @staticmethod
    def from_environment() -> "InternalAuth":
        mode = os.environ.get(MODE_VARIABLE, "required").strip().lower()
        tokens = [t.strip() for t in os.environ.get(TOKENS_VARIABLE, "").split(",") if t.strip()]
        if mode == "disabled" and os.environ.get(ENVIRONMENT_VARIABLE, "").strip().lower() != "development":
            raise RuntimeError(f"{MODE_VARIABLE}=disabled requires {ENVIRONMENT_VARIABLE}=development")
        return InternalAuth.from_tokens(tokens, required=(mode != "disabled"))

    def check(self, presented: str | None) -> str | None:
        """None when the request may proceed, otherwise the error code."""
        if not self.required:
            return None
        if not self.tokens:
            return "AUTH_NOT_CONFIGURED"
        candidate = (presented or "").encode("utf-8", errors="replace")
        matched = False
        for token in self.tokens:  # no early exit
            matched |= hmac.compare_digest(candidate, token)
        return None if matched else "UNAUTHENTICATED"


REQUIRED_WITHOUT_TOKENS = InternalAuth((), True)
