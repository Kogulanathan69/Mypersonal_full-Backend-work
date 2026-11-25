# rag-service (M7 regulation evidence retrieval). Non-root, read-only root filesystem at run time (see compose).
# The approved corpus is NOT in the image: the staged corpus (tools: rag-service/tools/stage_approved_corpus.py) is
# mounted read-only at /corpus and re-verified by the unchanged loaders at start-up. Versions are pinned by the
# constraints file frozen from the verified local environment. Build context: repository root.
FROM python:3.13-slim
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/* \
 && useradd --uid 10002 --create-home --shell /usr/sbin/nologin bmrag
WORKDIR /srv
COPY rag-service/requirements.txt deploy/captain/rag.constraints.txt ./
RUN pip install --no-cache-dir -r requirements.txt -c rag.constraints.txt
COPY rag-service/app ./app
COPY rag-service/regulation_retrieval ./regulation_retrieval
COPY rag-service/contract ./contract
USER bmrag
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 \
    BUILDMIND_RAG_CORPUS_ROOT=/corpus \
    BUILDMIND_RAG_MANIFEST=/corpus/approved_corpus_manifest.json \
    BUILDMIND_RAG_INDEX=/corpus/multi_document_index.json
EXPOSE 8092
CMD ["python", "-B", "-m", "uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8092", "--no-server-header", "--no-access-log"]
