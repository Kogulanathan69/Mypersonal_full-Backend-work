"""Internal token loading from a container secret file (fail closed)."""
import pytest

from app.internal.auth import TOKENS_VARIABLE, InternalAuth

TOKEN = "file-token-" + "a" * 40


def test_tokens_load_from_secret_file(tmp_path, monkeypatch):
    secret = tmp_path / "token"
    secret.write_text(TOKEN + "\n", encoding="ascii")
    monkeypatch.delenv(TOKENS_VARIABLE, raising=False)
    monkeypatch.setenv(TOKENS_VARIABLE + "_FILE", str(secret))
    auth = InternalAuth.from_environment()
    assert auth.check(TOKEN) is None and auth.check("wrong") == "UNAUTHENTICATED"


def test_missing_secret_file_fails_closed(tmp_path, monkeypatch):
    monkeypatch.delenv(TOKENS_VARIABLE, raising=False)
    monkeypatch.setenv(TOKENS_VARIABLE + "_FILE", str(tmp_path / "absent"))
    assert InternalAuth.from_environment().check(TOKEN) == "AUTH_NOT_CONFIGURED"


def test_both_sources_is_a_configuration_error(tmp_path, monkeypatch):
    secret = tmp_path / "token"
    secret.write_text(TOKEN, encoding="ascii")
    monkeypatch.setenv(TOKENS_VARIABLE, TOKEN)
    monkeypatch.setenv(TOKENS_VARIABLE + "_FILE", str(secret))
    with pytest.raises(RuntimeError):
        InternalAuth.from_environment()
