"""Research-only grouping, preserving rather than harmonizing source provisions."""
from collections import defaultdict

def group_evidence(assessor,query,passages,requested_document_ids=None,requested_jurisdiction=None,conflict_reported=False,max_per_source=3):
    if not isinstance(passages,list) or type(conflict_reported) is not bool or type(max_per_source) is not int or max_per_source<1:raise ValueError('Invalid grouping arguments')
    if requested_document_ids is not None and (not isinstance(requested_document_ids,list) or not all(isinstance(v,str) for v in requested_document_ids)):raise ValueError('Invalid explicit source request')
    requested=set(requested_document_ids or []);unknown=requested-set(assessor.records)
    groups=defaultdict(list);assessments=[];kept_spans=defaultdict(list);dropped=[]
    for position,passage in enumerate(passages):
        a=assessor.assess(query,passage,requested_jurisdiction);a['input_position']=position;assessments.append(a)
        if not a['provenance_verified']:continue
        if a['status'] not in {'EVIDENCE_CANDIDATE','AMBIGUOUS_EVIDENCE'}:
            dropped.append({'input_position':position,'reason':'NOT_A_QUALIFYING_CANDIDATE_OR_AMBIGUOUS_PASSAGE'});continue
        c=a['citation'];key=(c['document_id'],c['pdf_page_number']);start=c['start_char'];end=c['end_char']
        duplicate=any(max(0,min(end,hi)-max(start,lo))/min(end-start,hi-lo)>=.8 for lo,hi in kept_spans[key])
        if duplicate or len(groups[c['document_id']])>=max_per_source:
            dropped.append({'input_position':position,'reason':'DUPLICATE_OR_HIGH_OVERLAP' if duplicate else 'SOURCE_PASSAGE_CAP'});continue
        kept_spans[key].append((start,end));groups[c['document_id']].append(a)
    represented={did for did,items in groups.items() if any(a['status'] in {'EVIDENCE_CANDIDATE','AMBIGUOUS_EVIDENCE'} for a in items)}
    provenance_sources={a['document_id'] for a in assessments if a['provenance_verified']}
    candidates={did for did,items in groups.items() if any(a['status']=='EVIDENCE_CANDIDATE' for a in items)}
    known_jurisdictions={m['jurisdiction'] for m in assessor.registry.values()}
    reasons=[]
    if requested_jurisdiction is not None and requested_jurisdiction not in known_jurisdictions:
        status='JURISDICTION_UNVERIFIED';reasons.append('NO_DOCUMENT_SUPPORTED_MAPPING_FOR_REQUESTED_JURISDICTION')
    elif unknown:
        status='INSUFFICIENT_EVIDENCE';reasons.append('REQUESTED_SOURCE_OUTSIDE_APPROVED_CORPUS')
    elif requested and requested-represented:
        status='INSUFFICIENT_EVIDENCE';reasons.append('INCOMPLETE_REQUESTED_SOURCE_COVERAGE')
    elif conflict_reported:
        status='AMBIGUOUS_EVIDENCE';reasons.append('REPORTED_SOURCE_CONFLICT_REQUIRES_REVIEW_NOT_SCORE_RESOLUTION')
    elif any(a['status']=='JURISDICTION_UNVERIFIED' for a in assessments):
        status='JURISDICTION_UNVERIFIED';reasons.append('PASSAGE_JURISDICTION_NOT_VERIFIED_FOR_REQUEST')
    elif any(a['status']=='AMBIGUOUS_EVIDENCE' for items in groups.values() for a in items):
        status='AMBIGUOUS_EVIDENCE';reasons.append('AT_LEAST_ONE_SOURCE_OR_PASSAGE_REQUIRES_REVIEW')
    elif candidates:
        status='EVIDENCE_CANDIDATE';reasons.append('HEURISTIC_CANDIDATES_PRESENT_NOT_SEMANTIC_CERTIFICATION')
    else:
        status='INSUFFICIENT_EVIDENCE';reasons.append('NO_QUALIFYING_CANDIDATE')
    return {'status':status,'reasons':reasons,'query':query,'groups':dict(sorted(groups.items())),
            'assessments':assessments,'dropped_passages':dropped,'requested_document_ids':sorted(requested),
            'represented_source_ids':sorted(represented),'candidate_source_ids':sorted(candidates),
            'provenance_source_ids':sorted(provenance_sources),
            'requested_sources_physically_present':not bool(requested-provenance_sources) if requested else None,
            'missing_requested_sources':sorted(requested-represented),'unknown_requested_sources':sorted(unknown),
            'requested_sources_represented':not bool(requested-represented) if requested else None,
            'requested_sources_all_candidate':not bool(requested-candidates) if requested else None,
            'legal_hierarchy_inferred':False,'provisions_combined':False,'answer_generated':False,
            'semantic_sufficiency':'NOT_ESTABLISHED','automated_compliance_approved':False,
            'conflict_reported':conflict_reported,
            'warnings':['Sources remain separate; different scopes and possible conflicts are not resolved by rank scores.','Source representation and lexical candidates do not establish complete semantic evidence.']}
