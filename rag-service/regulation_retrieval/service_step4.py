"""Jurisdiction-scoped research retrieval. No legal answers or decisions."""
from .index import rank

def search_step4(index,query,k=5,filters=None,ranking=None):
    # Validate before even returning an insufficient-metadata response.
    if not isinstance(query,str) or type(k) is not int or not 1<=k<=100:
        raise ValueError('Invalid query or K')
    if filters is not None and not isinstance(filters,dict):raise ValueError('Filters must be an object')
    filters=dict(filters or {})
    if set(filters)-{'document_id','jurisdiction','source','source_pdf','publication_date'} or any(not isinstance(v,str) or not v.strip() for v in filters.values()):
        raise ValueError('Unsupported or invalid filter')
    if 'source' in filters:
        if 'source_pdf' in filters and filters['source_pdf']!=filters['source']:raise ValueError('Conflicting source filters')
        filters['source_pdf']=filters.pop('source')
    registry=index['source_registry']
    mode='jurisdiction_scoped_research' if 'jurisdiction' in filters else 'source_scoped_research' if filters else 'unfiltered_research'
    warnings=['Current legal applicability and subsequent amendments are not verified.',
              'Relevance does not establish parcel applicability or authorize construction.']
    base={'query':query,'results':[],'answer_generated':False,'automated_compliance_approved':False,
          'retrieval_mode':mode,'legal_status':'not_verified','source_warnings':warnings}
    # No automatic aliases, geographic inference or locality substitution.
    if 'jurisdiction' in filters and not any(e['jurisdiction']==filters['jurisdiction'] and e['jurisdiction_verification_status'].startswith('document_supported') for e in registry.values()):
        return base|{'status':'insufficient_jurisdiction_evidence'}
    selected={did:e for did,e in registry.items() if all((did if key=='document_id' else e.get(key))==value and (key!='publication_date' or e['publication_date_verified']) for key,value in filters.items())}
    if not selected:
        return base|{'status':'insufficient_jurisdiction_evidence' if 'jurisdiction' in filters else 'insufficient_source_evidence'}
    if 'jurisdiction' in filters:
        warnings.append('Exact documented scope filter; broader Gazette provisions may also require separate review. No conclusion of applicability or irrelevance is made.')
    hits=rank(index,query,k,filters,ranking)
    results=[]
    for position,(score,c) in enumerate(hits,1):
        e=registry[c['document_id']];meta=c['metadata']
        source_warnings=list(dict.fromkeys(meta['warnings']+c['extraction_limitations']+warnings))
        results.append({key:c[key] for key in ['chunk_id','document_id','source_pdf','pdf_page_number','text','start_char','end_char','extraction_error','regulation_number_hints']}|{
            'query':query,'rank':position,'score':score,'retrieved_passage':c['text'],
            'original_text_start_offset':c['start_char'],'original_text_end_offset':c['end_char'],
            'jurisdiction':e['jurisdiction'],'jurisdiction_verification_status':e['jurisdiction_verification_status'],
            'council_name_ambiguity':e['council_name_ambiguity'],'legal_status':'not_verified','legal_status_verified':False,
            'amendments_verified':False,'publication_date':e['publication_date'],'publication_date_verified':e['publication_date_verified'],
            'source_warnings':source_warnings,'metadata_warnings':source_warnings,
            'applicability':e['applicability'],'retrieval_mode':mode,'automated_compliance_approved':False})
    if len({r['document_id'] for r in results})>1 or any(r['council_name_ambiguity'] for r in results):
        warnings.append('UNRESOLVED_APPLICABILITY_OR_SOURCE_CONFLICT: differing source scopes or source ambiguity require review; BM25 does not identify or resolve legal conflicts.')
    return base|{'status':'retrieval_results' if results else 'no_matching_passages','results':results}
