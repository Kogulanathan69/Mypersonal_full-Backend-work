import json
from pathlib import Path

import pytest

from app.plan_editor.schemas import PlanGeometry


@pytest.fixture
def geometry_dict() -> dict[str, object]:
    path = Path(__file__).parents[3] / "docs" / "proposals" / "m3-plan-editor" / "PLAN-GEOMETRY-SAMPLE.json"
    return json.loads(path.read_text(encoding="utf-8-sig"))


@pytest.fixture
def geometry(geometry_dict: dict[str, object]) -> PlanGeometry:
    return PlanGeometry.model_validate(geometry_dict)
