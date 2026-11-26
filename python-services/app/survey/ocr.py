"""Local OCR for scanned survey documents (Tesseract CLI; PDFs rasterised with Poppler's pdftoppm).

Processing is entirely local: no external provider. Untrusted documents are handled in a sandbox:

* refuses to run as root;
* each tool runs as a separate process in its own session with a scrubbed environment, a private 0700 temporary
  working directory (always removed), and POSIX resource limits (address space, CPU seconds, output file size, open
  files, no core dumps);
* image dimensions and pixel counts are checked from the file header BEFORE any decoding (decompression-bomb guard);
* PDF rasterisation is capped in pages, output scale and total rendered bytes;
* optional network isolation: with ``BUILDMIND_OCR_NETWORK_ISOLATION=1`` each tool runs inside a new, empty network
  namespace (``unshare --net --map-root-user``); if that is unavailable OCR refuses to run (fail closed).

OCR text is evidence for human review only; callers must never treat OCR-derived values as verified.
"""
import os
import shutil
import struct
import subprocess
import tempfile

from app.survey.page_text import PageText

MAX_PAGES = 10
DPI = 200
MAX_RENDER_SIDE_PX = 3508  # A4 long side at 300 dpi
PAGE_TIMEOUT_SECONDS = 60
MAX_IMAGE_SIDE_PX = 12000
MAX_IMAGE_PIXELS = 50_000_000
MAX_RENDERED_BYTES = 200 * 1024 * 1024
ADDRESS_SPACE_BYTES = 1536 * 1024 * 1024
OUTPUT_FILE_BYTES = 256 * 1024 * 1024
REQUESTED_LANGUAGES = ("eng", "sin", "tam")


class OcrUnavailable(RuntimeError):
    """Tesseract or pdftoppm is not installed, or the sandbox cannot be established."""


class OcrFailed(RuntimeError):
    """A fixed-message OCR failure (never contains document content)."""


def available() -> bool:
    return shutil.which("tesseract") is not None and shutil.which("pdftoppm") is not None


def _limits(cpu_seconds: int):
    def apply():  # runs in the child between fork and exec
        import resource
        resource.setrlimit(resource.RLIMIT_AS, (ADDRESS_SPACE_BYTES, ADDRESS_SPACE_BYTES))
        resource.setrlimit(resource.RLIMIT_CPU, (cpu_seconds, cpu_seconds))
        resource.setrlimit(resource.RLIMIT_FSIZE, (OUTPUT_FILE_BYTES, OUTPUT_FILE_BYTES))
        resource.setrlimit(resource.RLIMIT_NOFILE, (64, 64))
        resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    return apply


def _network_isolated() -> bool:
    return os.environ.get("BUILDMIND_OCR_NETWORK_ISOLATION") == "1"


def sandboxed_run(argv: list[str], cwd: str, timeout: int) -> subprocess.CompletedProcess:
    """Runs one tool under the sandbox. Raises OcrUnavailable when the sandbox cannot be established."""
    if hasattr(os, "geteuid") and os.geteuid() == 0:
        raise OcrUnavailable("OCR refuses to run as root.")
    if _network_isolated():
        if shutil.which("unshare") is None:
            raise OcrUnavailable("Network isolation requested but unavailable.")
        argv = ["unshare", "--net", "--map-root-user", "--", *argv]
    env = {"PATH": "/usr/bin:/bin", "OMP_THREAD_LIMIT": "1", "LC_ALL": "C.UTF-8", "HOME": cwd, "TMPDIR": cwd}
    if os.environ.get("TESSDATA_PREFIX"):
        env["TESSDATA_PREFIX"] = os.environ["TESSDATA_PREFIX"]
    result = subprocess.run(argv, capture_output=True, timeout=timeout, check=False, cwd=cwd, env=env,
                            start_new_session=True, preexec_fn=_limits(timeout + 5), stdin=subprocess.DEVNULL)
    if _network_isolated() and result.returncode != 0 and b"unshare" in result.stderr[:200]:
        raise OcrUnavailable("Network isolation could not be established.")
    return result


_languages_cache: str | None = None


def languages() -> str:
    """The requested OCR languages that are actually installed (English is required)."""
    global _languages_cache
    if _languages_cache is None:
        try:
            listed = subprocess.run(["tesseract", "--list-langs"], capture_output=True, timeout=15, check=False,
                                    env={"PATH": "/usr/bin:/bin"}, stdin=subprocess.DEVNULL)
            installed = set(listed.stdout.decode("utf-8", errors="replace").split())
        except (OSError, subprocess.TimeoutExpired):
            installed = set()
        chosen = [lang for lang in REQUESTED_LANGUAGES if lang in installed]
        if "eng" not in chosen:
            raise OcrUnavailable("English OCR data is not installed.")
        _languages_cache = "+".join(chosen)
    return _languages_cache


def image_dimensions(content: bytes, media_type: str) -> tuple[int, int]:
    """Width and height from the PNG IHDR or the first JPEG SOF marker, without decoding pixel data."""
    if media_type == "image/png":
        if len(content) < 24 or content[:8] != b"\x89PNG\r\n\x1a\n" or content[12:16] != b"IHDR":
            raise OcrFailed("Image header is invalid.")
        return struct.unpack(">II", content[16:24])
    if media_type == "image/jpeg":
        if content[:2] != b"\xff\xd8":
            raise OcrFailed("Image header is invalid.")
        i = 2
        while i + 9 < len(content):
            if content[i] != 0xFF:
                raise OcrFailed("Image header is invalid.")
            marker = content[i + 1]
            if marker in (0xD8, 0x01) or 0xD0 <= marker <= 0xD7:
                i += 2
                continue
            length = struct.unpack(">H", content[i + 2:i + 4])[0]
            if 0xC0 <= marker <= 0xCF and marker not in (0xC4, 0xC8, 0xCC):
                height, width = struct.unpack(">HH", content[i + 5:i + 9])
                return width, height
            i += 2 + length
        raise OcrFailed("Image header is invalid.")
    raise OcrFailed("Unsupported media type for OCR.")


def check_image_limits(content: bytes, media_type: str) -> None:
    width, height = image_dimensions(content, media_type)
    if width == 0 or height == 0 or max(width, height) > MAX_IMAGE_SIDE_PX or width * height > MAX_IMAGE_PIXELS:
        raise OcrFailed("Image dimensions exceed the processing limits.")


def _tesseract(image_path: str, work: str) -> str:
    try:
        result = sandboxed_run(["tesseract", image_path, "stdout", "-l", languages(), "--psm", "3"], work, PAGE_TIMEOUT_SECONDS)
    except subprocess.TimeoutExpired:
        raise OcrFailed("OCR timed out for a page.") from None
    if result.returncode != 0:
        raise OcrFailed("OCR could not read a page.")
    return result.stdout.decode("utf-8", errors="replace")


def ocr_pages(content: bytes, media_type: str) -> tuple[PageText, ...]:
    if not available():
        raise OcrUnavailable("Local OCR is not installed.")
    with tempfile.TemporaryDirectory(prefix="bm-ocr-") as work:
        os.chmod(work, 0o700)
        if media_type in ("image/png", "image/jpeg"):
            check_image_limits(content, media_type)
            source = os.path.join(work, "page.png" if media_type == "image/png" else "page.jpg")
            with open(source, "wb") as handle:
                handle.write(content)
            return (PageText(page_number=1, text=_tesseract(source, work)),)
        if media_type != "application/pdf":
            raise OcrFailed("Unsupported media type for OCR.")
        pdf = os.path.join(work, "document.pdf")
        with open(pdf, "wb") as handle:
            handle.write(content)
        try:
            rendered = sandboxed_run(
                ["pdftoppm", "-r", str(DPI), "-scale-to", str(MAX_RENDER_SIDE_PX), "-f", "1", "-l", str(MAX_PAGES), "-png", pdf,
                 os.path.join(work, "page")], work, PAGE_TIMEOUT_SECONDS * 2)
        except subprocess.TimeoutExpired:
            raise OcrFailed("PDF rasterisation timed out.") from None
        if rendered.returncode != 0:
            raise OcrFailed("PDF could not be rasterised.")
        images = sorted(name for name in os.listdir(work) if name.startswith("page") and name.endswith(".png"))[:MAX_PAGES]
        if sum(os.path.getsize(os.path.join(work, name)) for name in images) > MAX_RENDERED_BYTES:
            raise OcrFailed("Rendered PDF exceeds the processing limits.")
        return tuple(PageText(page_number=index + 1, text=_tesseract(os.path.join(work, name), work))
                     for index, name in enumerate(images))
