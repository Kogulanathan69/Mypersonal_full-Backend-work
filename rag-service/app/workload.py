"""Server-side workload bounds (M6 H4-R1 C10).

Engine work runs on a dedicated, bounded thread pool:
- at most ``max_concurrent`` units of work exist at once; a request that finds no free slot is refused immediately
  with 503 ``SERVICE_BUSY`` (no queue grows without bound);
- each request waits at most ``timeout_seconds`` for its result and otherwise receives 504 ``WORK_TIMEOUT``;
- Python threads cannot be killed, so a slot is released only when its work has REALLY finished. Work that
  overruns its budget therefore keeps occupying capacity, and new requests are refused instead of piling up more
  CPU work behind it.

Configuration: ``BUILDMIND_RAG_MAX_CONCURRENT_WORK`` (1-64, default 4) and ``BUILDMIND_RAG_WORK_TIMEOUT_SECONDS``
(1-60, default 10). Invalid values fail start-up.
"""
import asyncio
import os
import threading
from concurrent.futures import ThreadPoolExecutor
from typing import Any, Callable

from .strict_json import RequestRejected

CONCURRENCY_VARIABLE = "BUILDMIND_RAG_MAX_CONCURRENT_WORK"
TIMEOUT_VARIABLE = "BUILDMIND_RAG_WORK_TIMEOUT_SECONDS"
DEFAULT_CONCURRENCY = 4
DEFAULT_TIMEOUT_SECONDS = 10.0


class WorkLimiter:
    def __init__(self, max_concurrent: int = DEFAULT_CONCURRENCY, timeout_seconds: float = DEFAULT_TIMEOUT_SECONDS):
        if not 1 <= max_concurrent <= 64:
            raise ValueError("max_concurrent must be between 1 and 64")
        if not 0 < timeout_seconds <= 60:
            raise ValueError("timeout_seconds must be greater than 0 and at most 60")
        self.max_concurrent = max_concurrent
        self.timeout_seconds = timeout_seconds
        self._slots = threading.BoundedSemaphore(max_concurrent)
        self._executor = ThreadPoolExecutor(max_workers=max_concurrent, thread_name_prefix="rag-work")

    @staticmethod
    def from_environment() -> "WorkLimiter":
        return WorkLimiter(
            _read(CONCURRENCY_VARIABLE, DEFAULT_CONCURRENCY, int, 1, 64),
            _read(TIMEOUT_VARIABLE, DEFAULT_TIMEOUT_SECONDS, float, 1, 60),
        )

    async def run(self, work: Callable[..., Any], *args: Any) -> Any:
        if not self._slots.acquire(blocking=False):
            raise RequestRejected(503, "SERVICE_BUSY")

        def bounded() -> Any:
            try:
                return work(*args)
            finally:
                self._slots.release()  # only when the work has actually finished

        try:
            future = asyncio.get_running_loop().run_in_executor(self._executor, bounded)
        except BaseException:
            self._slots.release()
            raise
        try:
            # shield: a timeout must not detach the slot accounting from the still-running thread
            return await asyncio.wait_for(asyncio.shield(future), timeout=self.timeout_seconds)
        except asyncio.TimeoutError:
            raise RequestRejected(504, "WORK_TIMEOUT") from None


def _read(variable: str, default, parse, low, high):
    raw = os.environ.get(variable, "").strip()
    if not raw:
        return default
    try:
        value = parse(raw)
    except ValueError:
        raise RuntimeError(f"{variable} must be a number between {low} and {high}") from None
    if not low <= value <= high:
        raise RuntimeError(f"{variable} must be between {low} and {high}")
    return value
