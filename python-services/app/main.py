"""BuildMind private Python services.

`/health` is the unauthenticated liveness probe. Every `/internal/*` route requires the internal shared-secret token
(app/internal/auth.py; fail closed). Interactive docs and the OpenAPI document are disabled. Error bodies carry fixed
codes only: never exception text, request values, paths or tokens.
"""
import logging

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
from pydantic import ValidationError

from app.internal.auth import AUTH_HEADER, REQUIRED_WITHOUT_TOKENS, InternalAuth
from app.internal.generation import GenerationRunRequest, run_generation
from app.internal.land import DependencyUnavailable as LandDependencyUnavailable
from app.internal.land import LandValidationRequest, validate_land
from app.internal.plan_validation import DependencyUnavailable as PlanDependencyUnavailable
from app.internal.plan_validation import PlanValidationRequest, validate_plan
from app.validation.boundary import ContractJsonError, parse_contract_json

MAX_BODY_BYTES = 2 * 1024 * 1024
logger = logging.getLogger("buildmind-python")


def _problem(status: int, code: str) -> JSONResponse:
    return JSONResponse(status_code=status, content={"code": code})


def create_app(auth: InternalAuth = REQUIRED_WITHOUT_TOKENS) -> FastAPI:
    application = FastAPI(title="BuildMind Python Services", version="0.2.0",
                          docs_url=None, redoc_url=None, openapi_url=None)

    @application.middleware("http")
    async def guard(request: Request, call_next):
        if request.url.path != "/health":
            presented = request.headers.getlist(AUTH_HEADER)
            failure = "UNAUTHENTICATED" if len(presented) > 1 else auth.check(presented[0] if presented else None)
            if failure == "AUTH_NOT_CONFIGURED":
                return _problem(503, failure)
            if failure is not None:
                return _problem(401, failure)
        length = request.headers.get("content-length")
        if length is not None and (not length.isdigit() or int(length) > MAX_BODY_BYTES):
            return _problem(413, "PAYLOAD_TOO_LARGE")
        return await call_next(request)

    async def body(request: Request, model):
        raw = await request.body()
        if len(raw) > MAX_BODY_BYTES:
            raise _Rejected(413, "PAYLOAD_TOO_LARGE")
        if request.headers.get("content-type", "").split(";")[0].strip().lower() != "application/json":
            raise _Rejected(415, "UNSUPPORTED_MEDIA_TYPE")
        try:
            return parse_contract_json(model, raw)
        except (ContractJsonError, ValidationError, ValueError):
            raise _Rejected(422, "VALIDATION_FAILED") from None

    @application.exception_handler(_Rejected)
    async def rejected(_: Request, exc: _Rejected) -> JSONResponse:
        return _problem(exc.status, exc.code)

    async def dependency_unavailable(_: Request, __: Exception) -> JSONResponse:
        return _problem(503, "DEPENDENCY_UNAVAILABLE")

    for dependency_error in (LandDependencyUnavailable, PlanDependencyUnavailable, ImportError):
        application.add_exception_handler(dependency_error, dependency_unavailable)

    @application.exception_handler(Exception)
    async def unexpected(_: Request, exc: Exception) -> JSONResponse:
        logger.error("Unhandled %s in python-services", type(exc).__name__)
        return _problem(500, "INTERNAL_ERROR")

    @application.get("/health")
    def health() -> dict[str, str]:
        return {"service": "buildmind-python", "status": "ok"}

    @application.post("/internal/v1/land/validate")
    async def land_validate(request: Request):
        return validate_land(await body(request, LandValidationRequest))

    @application.post("/internal/v1/generation/run")
    async def generation_run(request: Request):
        return run_generation(await body(request, GenerationRunRequest))

    @application.post("/internal/v1/plans/validate")
    async def plan_validate(request: Request):
        return validate_plan(await body(request, PlanValidationRequest))

    return application


class _Rejected(Exception):
    def __init__(self, status: int, code: str):
        super().__init__(code)
        self.status = status
        self.code = code


app = create_app(InternalAuth.from_environment())
