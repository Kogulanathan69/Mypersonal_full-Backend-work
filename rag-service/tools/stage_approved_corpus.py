"""Stages the approved STEP 4 regulation corpus into a corpus root that the unchanged loaders resolve on any OS.

Why: the approved-source pins (`approved_sources_step4.APPROVED[...]['source_pdf']`) are Windows-style relative paths
(`Other_Documents\\x.pdf`). The verbatim loader resolves `root/'input_pdfs'/source_pdf`; on POSIX the backslash is an
ordinary filename character, so a Windows tree does not resolve. This tool does NOT relax identity checks: it

1. validates the source manifest with the verbatim `validate_manifest` (pins, scope, approval metadata);
2. locates each PDF in the source tree by interpreting the pin as a Windows path, and refuses unless its SHA-256
   equals the pinned document id;
3. writes it to `dest/input_pdfs/<pin exactly as pinned>`, i.e. the path the loader itself will compute;
4. copies each extracted record, refusing unless its SHA-256 equals the manifest `record_sha256`;
5. copies the Gazette metadata sidecar, the manifest and the index, records every SHA-256 in
   `dest/STAGING-RECEIPT.json`, and makes staged files read-only;
6. loads the staged root with the verbatim `load_step4_corpus` and the evidence assessor as a final check.

Usage: python -B tools/stage_approved_corpus.py --source <corpus root> --manifest <rel> --index <rel> --dest <dir>
Never stages into the repository. Existing staged files are reused only when byte-identical (hash-checked).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path, PureWindowsPath

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from regulation_retrieval.approved_sources_step4 import APPROVED, GAZETTE  # noqa: E402
from regulation_retrieval.corpus_step4 import load_step4_corpus, validate_manifest  # noqa: E402

GAZETTE_SIDECAR = "output/regulatory_metadata/uda_gazette_2235_54_2021.json"
REPO_ROOT = Path(__file__).resolve().parents[2]


class StagingError(RuntimeError):
    pass


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def _copy_verified(source: Path, target: Path, expected: str | None) -> str:
    if not source.is_file():
        raise StagingError(f"missing source file: {source.name}")
    actual = sha256(source)
    if expected is not None and actual != expected:
        raise StagingError(f"identity mismatch for {source.name}: refusing to stage")
    if target.exists():
        if sha256(target) != actual:
            raise StagingError(f"staged file differs from source: {target.name}; refusing to overwrite")
        return actual
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = target.with_name(target.name + ".partial")
    shutil.copyfile(source, temporary)
    if sha256(temporary) != actual:
        temporary.unlink()
        raise StagingError(f"copy verification failed: {source.name}")
    os.replace(temporary, target)
    target.chmod(0o444)
    return actual


def stage(source: Path, manifest_rel: str, index_rel: str, dest: Path) -> dict:
    source, dest = source.resolve(), dest.resolve()
    if dest == REPO_ROOT or REPO_ROOT in dest.parents:
        raise StagingError("refusing to stage the corpus inside the repository")
    manifest_path = source / PureWindowsPath(manifest_rel).as_posix()
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    entries = validate_manifest(manifest)  # verbatim pin/scope/approval validation
    dest.mkdir(parents=True, exist_ok=True)
    dest.chmod(0o700)
    files = []
    for entry in entries:
        pin = entry["source_pdf"]
        if APPROVED[entry["document_id"]]["source_pdf"] != pin:
            raise StagingError("manifest pin differs from the approved source pin")
        located = source / "input_pdfs" / Path(*PureWindowsPath(pin).parts)
        staged = dest / "input_pdfs" / pin          # exactly what the loader computes on this OS
        files.append({"role": "approved_pdf", "document_id": entry["document_id"], "pin": pin,
                      "sha256": _copy_verified(located, staged, entry["document_id"])})
        files.append({"role": "extracted_record", "document_id": entry["document_id"], "path": entry["record"],
                      "sha256": _copy_verified(source / entry["record"], dest / entry["record"], entry["record_sha256"])})
    files.append({"role": "gazette_metadata", "document_id": GAZETTE, "path": GAZETTE_SIDECAR,
                  "sha256": _copy_verified(source / GAZETTE_SIDECAR, dest / GAZETTE_SIDECAR, None)})
    staged_manifest = dest / "approved_corpus_manifest.json"
    staged_index = dest / "multi_document_index.json"
    files.append({"role": "manifest", "sha256": _copy_verified(manifest_path, staged_manifest, None)})
    files.append({"role": "index", "sha256": _copy_verified(source / PureWindowsPath(index_rel).as_posix(), staged_index, None)})

    documents = load_step4_corpus(dest, staged_manifest)    # verbatim loader: every identity check runs again
    from regulation_retrieval.evidence_step6.assessor import EvidenceAssessor
    EvidenceAssessor(documents)                               # verbatim snapshot identity checks
    receipt = {
        "schemaVersion": "captain-corpus-staging/1",
        "stagedAtUtc": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "documents": sorted(e["document_id"] for e in entries),
        "files": files,
        "loaderVerified": True,
        "note": "Identity pins, manifest and records are byte-identical to the approved source; only the location changed.",
    }
    receipt_path = dest / "STAGING-RECEIPT.json"
    if receipt_path.exists():
        receipt_path.chmod(0o600)
    receipt_path.write_text(json.dumps(receipt, indent=2), encoding="utf-8")
    receipt_path.chmod(0o444)
    return receipt


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--source", required=True)
    parser.add_argument("--manifest", default="output/regulation_retrieval_step4/approved_corpus_manifest.json")
    parser.add_argument("--index", default="output/regulation_retrieval_step4/multi_document_index.json")
    parser.add_argument("--dest", required=True)
    args = parser.parse_args(argv)
    try:
        receipt = stage(Path(args.source), args.manifest, args.index, Path(args.dest))
    except (StagingError, ValueError, OSError) as error:
        print(f"STAGING REFUSED: {error}", file=sys.stderr)
        return 2
    print(f"staged {len(receipt['documents'])} approved documents; loader verified; receipt written")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
