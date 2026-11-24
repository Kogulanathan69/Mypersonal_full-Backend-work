"""OCR sandbox guards that do not need Tesseract: header-based dimension limits, root refusal, resource limits."""
import struct
import subprocess
import sys
import tempfile

import pytest

from app.survey import ocr


def png_header(width: int, height: int) -> bytes:
    return b"\x89PNG\r\n\x1a\n" + struct.pack(">I", 13) + b"IHDR" + struct.pack(">II", width, height) + b"\x08\x02\x00\x00\x00" + b"\x00" * 4


def jpeg_header(width: int, height: int) -> bytes:
    app0 = b"\xff\xe0" + struct.pack(">H", 16) + b"JFIF\x00\x01\x01\x00\x00\x01\x00\x01\x00\x00"
    sof = b"\xff\xc0" + struct.pack(">HBHHB", 11, 8, height, width, 1) + b"\x01\x11\x00"
    return b"\xff\xd8" + app0 + sof + b"\xff\xd9"


def test_png_and_jpeg_dimensions_are_read_from_headers_only():
    assert ocr.image_dimensions(png_header(800, 600), "image/png") == (800, 600)
    assert ocr.image_dimensions(jpeg_header(1024, 768), "image/jpeg") == (1024, 768)


@pytest.mark.parametrize("content,media", [
    (png_header(100_000, 100_000), "image/png"),        # decompression bomb by declared size
    (png_header(9000, 9000), "image/png"),              # 81 MP > 50 MP
    (jpeg_header(13000, 10), "image/jpeg"),             # side limit
    (png_header(0, 10), "image/png"),
])
def test_oversized_or_degenerate_images_are_rejected_before_decoding(content, media):
    with pytest.raises(ocr.OcrFailed) as error:
        ocr.check_image_limits(content, media)
    assert "limits" in str(error.value)


@pytest.mark.parametrize("content,media", [(b"GIF89a....", "image/png"), (b"\xff\xd8\x00\x00", "image/jpeg"), (b"", "image/png")])
def test_malformed_headers_fail_with_fixed_messages(content, media):
    with pytest.raises(ocr.OcrFailed) as error:
        ocr.check_image_limits(content, media)
    assert str(error.value) == "Image header is invalid."


def test_refuses_to_run_as_root(monkeypatch):
    monkeypatch.setattr(ocr.os, "geteuid", lambda: 0, raising=False)
    with pytest.raises(ocr.OcrUnavailable):
        ocr.sandboxed_run(["true"], tempfile.gettempdir(), 5)


@pytest.mark.skipif(sys.platform == "win32", reason="POSIX resource limits")
def test_children_run_with_resource_limits_scrubbed_env_and_new_session():
    probe = ("import os,resource;print(resource.getrlimit(resource.RLIMIT_AS)[0],resource.getrlimit(resource.RLIMIT_NOFILE)[0],"
             "resource.getrlimit(resource.RLIMIT_CORE)[0],os.getsid(0)==os.getpid(),sorted(os.environ))")
    with tempfile.TemporaryDirectory() as work:
        result = ocr.sandboxed_run([sys.executable, "-I", "-c", probe], work, 10)
    fields = result.stdout.decode().split(" ", 4)
    assert int(fields[0]) == ocr.ADDRESS_SPACE_BYTES and int(fields[1]) == 64 and int(fields[2]) == 0
    assert fields[3] == "True"
    assert "HTTP_PROXY" not in fields[4] and "BUILDMIND_PY_INTERNAL_TOKENS" not in fields[4]


@pytest.mark.skipif(sys.platform == "win32", reason="POSIX")
def test_timeouts_are_enforced():
    with tempfile.TemporaryDirectory() as work, pytest.raises(subprocess.TimeoutExpired):
        ocr.sandboxed_run([sys.executable, "-c", "import time; time.sleep(30)"], work, 1)
