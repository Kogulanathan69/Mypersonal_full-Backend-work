# Local LLM runtime for the verification stack: llama.cpp release b12237 (archive SHA-256 verified when downloaded;
# see REAL-LLM-INTEGRATION-REPORT). Build context: the extracted release directory (~/bm-captain/tools/llama-b12237),
# not the repository. The model is mounted read-only at run time, never baked into the image.
FROM ubuntu:24.04
RUN apt-get update && apt-get install -y --no-install-recommends libgomp1 ca-certificates curl \
 && rm -rf /var/lib/apt/lists/* && useradd --uid 10003 --create-home --shell /usr/sbin/nologin bmllm
COPY . /opt/llama
ENV LD_LIBRARY_PATH=/opt/llama
USER bmllm
EXPOSE 8093
ENTRYPOINT ["/opt/llama/llama-server"]
