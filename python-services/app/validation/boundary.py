"""Provisional M6 JSON and public error boundary helpers.

Call the JSON preflight before validation or fingerprinting. Production
transport adoption, payload-size policy and cross-language duplicate handling
require M4 agreement. Use the public formatter at the exception boundary
instead of exposing Pydantic errors or arbitrary exception messages.
"""

import json
from typing import TypeVar

from pydantic import ValidationError

from app.geometry.footprint.contracts import ContractModel

Contract = TypeVar("Contract", bound=ContractModel)


class ContractJsonError(ValueError):
    """A safe local JSON boundary rejection."""


def _unique_members(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for name, value in pairs:
        if name in result:
            raise ContractJsonError("Duplicate JSON property.")
        result[name] = value
    return result


def _invalid_constant(_token: str) -> None:
    raise ContractJsonError("Invalid JSON number.")


def parse_contract_json(model: type[Contract], payload: str | bytes | bytearray) -> Contract:
    """Reject ambiguous JSON, then apply the model's original JSON-mode validation."""
    try:
        json.loads(
            payload, object_pairs_hook=_unique_members, parse_constant=_invalid_constant,
            parse_int=lambda _: None, parse_float=lambda _: None,
        )
    except ContractJsonError:
        raise
    except (ValueError, RecursionError):
        raise ContractJsonError("Invalid contract JSON.") from None
    return model.model_validate_json(payload)


_PUBLIC_ERRORS = {
    "missing": ("REQUIRED_PROPERTY_MISSING", "A required property is missing."),
    "extra_forbidden": ("PROPERTY_NOT_ALLOWED", "A submitted property is not allowed."),
    "literal_error": ("VALUE_NOT_SUPPORTED", "A submitted value is not supported."),
    "json_invalid": ("JSON_INVALID", "The request is not valid contract JSON."),
}
_INVALID_VALUE = ("CONTRACT_VALUE_INVALID", "A submitted value violates the contract.")
_MAX_PUBLIC_ERRORS = 20


def format_public_validation_errors(error: Exception) -> list[dict[str, str]]:
    """Emit at most 20 fixed messages without input, paths or validator context."""
    if isinstance(error, ValidationError):
        issues = error.errors(include_input=False, include_context=False, include_url=False)
        messages = [
            _PUBLIC_ERRORS.get(issue["type"], _INVALID_VALUE)
            for issue in issues[:_MAX_PUBLIC_ERRORS]
        ]
    elif isinstance(error, ContractJsonError):
        messages = [_PUBLIC_ERRORS["json_invalid"]]
    else:
        messages = [_INVALID_VALUE]
    return [{"code": code, "message": message} for code, message in messages]
