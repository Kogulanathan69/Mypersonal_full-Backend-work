"""Strict read-only loader for manifest-approved documents."""
import hashlib
import json
import re
from pathlib import Path
from .metadata import load_metadata

def sha256(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()

def inside(root, relative):
    path = (root / relative).resolve()
    if not path.is_relative_to(root.resolve()):
        raise ValueError('Path escapes approved root')
    return path

def validate_record(data, approved):
    if not isinstance(data, dict):
        raise ValueError('Record must be an object')
    did = data.get('document_id')
    if not isinstance(did, str) or not re.fullmatch(r'[a-f0-9]{64}', did):
        raise ValueError('Invalid document ID')
    if did != approved['document_id'] or data.get('source') != approved['source_pdf']:
        raise ValueError('Record identity mismatch')
    pages = data.get('pages')
    if not isinstance(pages, list) or type(data.get('page_count')) is not int or len(pages) != data['page_count']:
        raise ValueError('Invalid page count')
    for expected, page in enumerate(pages, 1):
        if not isinstance(page, dict) or type(page.get('page_number')) is not int or page['page_number'] != expected:
            raise ValueError('Pages must retain contiguous one-based PDF positions')
        if not isinstance(page.get('text'), str):
            raise ValueError('Page text must be a string')
        if 'extraction_error' not in page or page['extraction_error'] is not None and not isinstance(page['extraction_error'], str):
            raise ValueError('Invalid extraction error')
    return data

def load_corpus(root, manifest_path):
    root = Path(root).resolve()
    manifest_path = Path(manifest_path)
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    approved = manifest.get('documents')
    if manifest.get('schema_version') != 1 or not isinstance(approved, list) or not approved:
        raise ValueError('Invalid corpus manifest')
    docs, seen, seen_sources = [], set(), set()
    for entry in approved:
        if not isinstance(entry,dict) or not isinstance(entry.get('source_pdf'),str):
            raise ValueError('Invalid manifest document')
        normalized_source=entry['source_pdf'].replace('\\','/').casefold()
        if normalized_source in seen_sources:
            raise ValueError('Duplicate approved source PDF')
        seen_sources.add(normalized_source)
        if entry['document_id'] in seen:
            raise ValueError('Duplicate approved document ID')
        seen.add(entry['document_id'])
        record_path = inside(root, entry['record'])
        pdf_path = inside(root / 'input_pdfs', entry['source_pdf'])
        if pdf_path.suffix.lower() != '.pdf' or sha256(pdf_path) != entry['document_id']:
            raise ValueError('Original PDF hash does not match document identity')
        data = validate_record(json.loads(record_path.read_text(encoding='utf-8-sig')), entry)
        meta_path = inside(root, entry['metadata']) if entry.get('metadata') else None
        docs.append({'record': data, 'record_path': str(record_path),
                     'record_sha256': sha256(record_path), 'pdf_path': str(pdf_path),
                     'metadata': load_metadata(meta_path, data['document_id'], data['source']),
                     'metadata_sha256': sha256(meta_path) if meta_path else None})
    return docs
