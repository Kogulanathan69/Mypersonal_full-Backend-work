"""Deterministic page-local chunks with reconstructible Unicode offsets."""
import hashlib
import re

def chunk_document(document, size=1000, overlap=150):
    if type(size) is not int or size < 200 or type(overlap) is not int or not 0 <= overlap < size // 2:
        raise ValueError('Require size >= 200 and 0 <= overlap < size/2')
    record = document['record']
    chunks, skipped = [], []
    for page in record['pages']:
        text = page['text']
        if not text.strip():
            skipped.append({'document_id': record['document_id'], 'page_number': page['page_number'],
                            'reason': 'empty_text', 'extraction_error': page['extraction_error']})
            continue
        # Integer headings are hints only; never claim reliable clause parsing.
        headings = [(m.start(), m.group(1)) for m in re.finditer(r'(?m)^\s*(\d{1,3})\.\s', text)]
        start = 0
        while start < len(text):
            end = min(start + size, len(text))
            if end < len(text):
                boundaries = [m.end() for m in re.finditer(r'\n\s*\n|[.;]\s+|\n', text[start:end]) if m.end() >= size // 2]
                if boundaries:
                    end = start + boundaries[-1]
            passage = text[start:end]
            if passage.strip():
                hints = [number for pos, number in headings if start <= pos < end]
                prior = [number for pos, number in headings if pos <= start]
                if prior and prior[-1] not in hints:
                    hints.insert(0, prior[-1])
                identity = f"{record['document_id']}:{page['page_number']}:{start}:{end}:" + hashlib.sha256(passage.encode()).hexdigest()
                warnings = ['PDF extraction may lose table structure and reading order.', 'Regulation numbers are unverified heading hints.']
                if page['extraction_error']:
                    warnings.append('Page has extraction error: ' + page['extraction_error'])
                chunks.append({'chunk_id': hashlib.sha256(identity.encode()).hexdigest(),
                    'document_id': record['document_id'], 'source_pdf': record['source'],
                    'pdf_page_number': page['page_number'], 'start_char': start, 'end_char': end,
                    'text': passage, 'regulation_number_hints': hints,
                    'extraction_error': page['extraction_error'], 'extraction_limitations': warnings,
                    'metadata': document['metadata']})
            if end == len(text):
                break
            start = max(start + 1, end - overlap)
    return chunks, skipped
