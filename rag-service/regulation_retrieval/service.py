"""Provenance-bearing retrieval, without answer generation."""
import json
from pathlib import Path
from .index import rank

class RegulationRetrievalService:
    def __init__(self, index, ranking=None):
        self.index = json.loads(Path(index).read_text(encoding='utf-8')) if isinstance(index,(str,Path)) else index
        self.ranking=ranking
        if self.index.get('schema_version') != 1 or self.index.get('algorithm') != 'BM25':
            raise ValueError('Unsupported index')

    def search(self, query, k=5, filters=None):
        if self.index.get('corpus_profile')=='step4_approved_three_documents':
            from .service_step4 import search_step4
            return search_step4(self.index,query,k,filters,self.ranking)
        hits = rank(self.index, query, k, filters, self.ranking)
        results = []
        for position,(score,chunk) in enumerate(hits,1):
            meta = chunk['metadata']
            results.append({key: chunk[key] for key in ['chunk_id','document_id','source_pdf','pdf_page_number','start_char','end_char','text','regulation_number_hints','extraction_error']} | {
                'rank': position, 'score': score, 'jurisdiction': meta['jurisdiction'],
                'publication_date': meta['publication_date'], 'legal_status_verified': False,
                'metadata_verification_basis': meta['metadata_verification_basis'],
                'metadata_warnings': meta['warnings'] + chunk['extraction_limitations']})
        return {'query': query, 'status': 'results_found' if results else 'no_matching_passages',
                'results': results, 'answer_generated': False, 'legal_status_verified': False}
