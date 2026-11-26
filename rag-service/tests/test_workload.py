"""Server-side workload bounds (M6 H4-R1 C10): bounded concurrency, per-request budget, no unbounded pile-up."""
import threading
import time

import pytest
from fastapi.testclient import TestClient

from app.auth import InternalAuth
from app.main import create_app
from app.workload import WorkLimiter
from support import AUTH_HEADER, TEST_TOKEN
from synthetic_corpus import SyntheticEngine

SEARCH = "/internal/v1/regulations/search"


class BlockingEngine(SyntheticEngine):
    """Real synthetic engine whose search blocks until released (simulates pathological server work)."""

    def __init__(self):
        super().__init__()
        self.release = threading.Event()
        self.started = threading.Event()
        self.finished = threading.Event()

    def search(self, *args):
        self.started.set()
        try:
            self.release.wait(timeout=30)
            return super().search(*args)
        finally:
            self.finished.set()


def client(engine, limiter):
    return TestClient(create_app(engine, InternalAuth.from_tokens([TEST_TOKEN]), None, limiter), headers={AUTH_HEADER: TEST_TOKEN})


def test_overrunning_work_times_out_and_keeps_its_slot_until_it_really_finishes():
    engine = BlockingEngine()
    c = client(engine, WorkLimiter(max_concurrent=1, timeout_seconds=0.3))

    started = time.monotonic()
    first = c.post(SEARCH, json={"query": "rear space"})
    elapsed = time.monotonic() - started

    assert first.status_code == 504 and first.json() == {"code": "WORK_TIMEOUT"}
    assert elapsed < 5  # the caller is answered within the budget, not when the work ends
    assert engine.started.is_set() and not engine.finished.is_set()

    busy = c.post(SEARCH, json={"query": "rear space"})  # the stuck work still occupies the only slot
    assert busy.status_code == 503 and busy.json() == {"code": "SERVICE_BUSY"}

    engine.release.set()
    assert engine.finished.wait(timeout=10)
    for _ in range(50):  # the slot is released in the worker's finally block
        recovered = c.post(SEARCH, json={"query": "rear space"})
        if recovered.status_code != 503:
            break
        time.sleep(0.05)
    assert recovered.status_code == 200


def test_busy_rejection_is_immediate_and_never_starts_more_work():
    engine = BlockingEngine()
    limiter = WorkLimiter(max_concurrent=1, timeout_seconds=0.2)
    c = client(engine, limiter)
    c.post(SEARCH, json={"query": "rear space"})  # occupies the slot (times out, keeps running)
    calls_before = engine.started.is_set()

    started = time.monotonic()
    responses = [c.post(SEARCH, json={"query": "rear space"}).status_code for _ in range(5)]

    assert responses == [503] * 5
    assert time.monotonic() - started < 2  # no waiting in a queue
    assert calls_before
    engine.release.set()


def test_constraint_lookups_are_bounded_too():
    from test_constraints import provider

    gate = threading.Event()
    slow = provider()
    real_lookup = slow.lookup
    slow.lookup = lambda request: (gate.wait(timeout=30), real_lookup(request))[1]
    app = create_app(SyntheticEngine(), InternalAuth.from_tokens([TEST_TOKEN]), slow, WorkLimiter(1, 0.2))
    c = TestClient(app, headers={AUTH_HEADER: TEST_TOKEN})
    body = {"jurisdiction": "Synthetic North Council planning area", "kinds": ["REAR_SPACE_MIN"],
            "binding": {"project_id": "11111111-1111-4111-8111-111111111111", "project_revision": 3,
                        "land_revision_id": "22222222-2222-4222-8222-222222222222",
                        "requirement_revision_id": "33333333-3333-4333-8333-333333333333"}}

    assert c.post("/internal/v1/regulations/constraints", json=body).status_code == 504
    assert c.post("/internal/v1/regulations/constraints", json=body).status_code == 503
    gate.set()


def test_normal_work_is_unaffected():
    response = client(SyntheticEngine(), WorkLimiter()).post(SEARCH, json={"query": "rear space"})

    assert response.status_code == 200


@pytest.mark.parametrize(("variable", "value"), [
    ("BUILDMIND_RAG_MAX_CONCURRENT_WORK", "0"),
    ("BUILDMIND_RAG_MAX_CONCURRENT_WORK", "65"),
    ("BUILDMIND_RAG_MAX_CONCURRENT_WORK", "many"),
    ("BUILDMIND_RAG_WORK_TIMEOUT_SECONDS", "0"),
    ("BUILDMIND_RAG_WORK_TIMEOUT_SECONDS", "61"),
    ("BUILDMIND_RAG_WORK_TIMEOUT_SECONDS", "nan"),
])
def test_invalid_workload_configuration_fails_start_up(monkeypatch, variable, value):
    monkeypatch.setenv(variable, value)

    with pytest.raises(RuntimeError):
        WorkLimiter.from_environment()


def test_workload_configuration_from_environment(monkeypatch):
    monkeypatch.setenv("BUILDMIND_RAG_MAX_CONCURRENT_WORK", "2")
    monkeypatch.setenv("BUILDMIND_RAG_WORK_TIMEOUT_SECONDS", "5")

    limiter = WorkLimiter.from_environment()

    assert (limiter.max_concurrent, limiter.timeout_seconds) == (2, 5.0)
