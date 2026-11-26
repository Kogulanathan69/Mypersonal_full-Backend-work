# python-services (geometry/generation/validation/survey extraction + local OCR). Non-root; run with a read-only root
# filesystem and a tmpfs /tmp (see compose). No network egress is needed. NOT deployed.
# Build context: repository root.  docker build -f deploy/captain/python.Dockerfile -t buildmind-python:local .
FROM python:3.13-slim
RUN apt-get update \
 && apt-get install -y --no-install-recommends tesseract-ocr tesseract-ocr-eng tesseract-ocr-sin tesseract-ocr-tam poppler-utils curl \
 && rm -rf /var/lib/apt/lists/* \
 && useradd --uid 10001 --create-home --shell /usr/sbin/nologin bmpy
WORKDIR /srv
COPY python-services/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY python-services/app ./app
USER bmpy
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
EXPOSE 8091
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD ["curl", "-fsS", "http://127.0.0.1:8091/health"]
CMD ["python", "-m", "uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8091", "--no-server-header"]
