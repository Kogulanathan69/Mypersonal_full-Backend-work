"""Shared strict-model helpers for the provisional M3 editor contract."""

from pydantic import BaseModel, ConfigDict


class StrictModel(BaseModel):
    """Reject undeclared input so contract drift cannot pass silently."""

    model_config = ConfigDict(extra="forbid")
