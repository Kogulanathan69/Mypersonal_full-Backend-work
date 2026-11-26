"""Read metadata conservatively without inferring legal currency."""
import json

def load_metadata(path, document_id, source):
    data = json.loads(path.read_text(encoding='utf-8-sig')) if path else {}
    if data and (data.get('document_id') != document_id or data.get('source') != source):
        raise ValueError('Metadata identity mismatch')
    warnings = ['Legal currency and amendments have not been independently verified by this pilot.']
    if not data:
        warnings.append('Document metadata unavailable.')
    if data.get('classification_verified') is not True:
        warnings.append('Classification unverified; inclusion relies only on the explicit corpus allowlist.')
    if data and not data.get('jurisdiction'):
        warnings.append('Jurisdiction unknown; this source cannot satisfy a jurisdiction filter.')
    # Preserve the sidecar verbatim. Classification verification alone does not
    # independently verify each metadata field or establish legal currency.
    return {'raw': data, 'jurisdiction': data.get('jurisdiction'),
            'publication_date': data.get('publication_date'),
            'metadata_provenance': str(path) if path else None,
            'metadata_verification_basis': 'existing_sidecar_assertion' if data else 'unknown',
            'legal_status_verified': False, 'warnings': warnings}
