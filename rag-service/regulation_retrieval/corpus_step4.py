"""Strict STEP 4 schema and read-only loading; no discovery or legal inference."""
import json
import re
from pathlib import Path
from .approved_sources_step4 import APPROVED,GAZETTE
from .corpus import inside,sha256,validate_record
from .metadata import load_metadata

REQUIRED={'document_id','source_pdf','record','record_sha256','page_count','jurisdiction','jurisdiction_verification_status','council_name_ambiguity','publication_date','publication_date_verified','legal_status','amendments_verified','classification_status','extraction_warnings','source_approval_status','applicability','metadata_evidence'}

def validate_manifest(data):
    if not isinstance(data,dict) or set(data)!={'schema_version','scope','documents'} or data['schema_version']!=2 or type(data['schema_version']) is not int:
        raise ValueError('Unsupported STEP 4 manifest schema')
    entries=data['documents']
    if not isinstance(entries,list) or len(entries)!=3:
        raise ValueError('Exactly three approved sources required')
    seen=set()
    for e in entries:
        if not isinstance(e,dict) or set(e)!=REQUIRED:
            raise ValueError('Invalid STEP 4 source fields')
        did=e['document_id']
        if not isinstance(did,str) or did not in APPROVED or did in seen:
            raise ValueError('Unapproved or duplicate identity')
        seen.add(did)
        for key,value in APPROVED[did].items():
            if e[key]!=value or type(e[key]) is not type(value):
                raise ValueError('Approved identity/scope mismatch: '+key)
        if e['record']!='output/documents/'+did[:24]+'.json':
            raise ValueError('Record-path substitution rejected')
        if not isinstance(e['record_sha256'],str) or not re.fullmatch('[a-f0-9]{64}',e['record_sha256']):
            raise ValueError('Invalid extracted-record hash')
        if e['legal_status']!='not_verified' or e['amendments_verified'] is not False or e['source_approval_status']!='explicit_user_approved':
            raise ValueError('Unsafe legal or approval metadata')
        if type(e['publication_date_verified']) is not bool or e['publication_date_verified']!=(did==GAZETTE):
            raise ValueError('Unsupported publication-date verification claim')
        if e['publication_date']!=('2021-07-08' if did==GAZETTE else None):
            raise ValueError('Unsupported publication date')
        if e['classification_status']!=('existing_verified_classification' if did==GAZETTE else 'document_content_supported_not_legal_verification'):
            raise ValueError('Invalid classification status')
        if not isinstance(e['applicability'],str) or not e['applicability'].strip():
            raise ValueError('Missing applicability')
        if not isinstance(e['extraction_warnings'],list) or not all(isinstance(w,str) for w in e['extraction_warnings']):
            raise ValueError('Invalid warnings')
        if not isinstance(e['metadata_evidence'],list) or not e['metadata_evidence']:
            raise ValueError('Missing source evidence')
        for proof in e['metadata_evidence']:
            if not isinstance(proof,dict) or set(proof)!={'pdf_page_number','passage'} or type(proof['pdf_page_number']) is not int or not 1<=proof['pdf_page_number']<=e['page_count'] or not isinstance(proof['passage'],str) or not proof['passage'].strip():
                raise ValueError('Invalid metadata evidence')
    return entries

def load_step4_corpus(root,manifest_path):
    root=Path(root).resolve();manifest=json.loads(Path(manifest_path).read_text(encoding='utf-8-sig'))
    entries=validate_manifest(manifest);docs=[]
    for e in entries:
        pdf=inside(root/'input_pdfs',e['source_pdf']);record_path=inside(root,e['record'])
        if sha256(pdf)!=e['document_id'] or sha256(record_path)!=e['record_sha256']:
            raise ValueError('Original PDF or extracted-record identity mismatch')
        record=validate_record(json.loads(record_path.read_text(encoding='utf-8-sig')),e)
        if record['page_count']!=e['page_count']:
            raise ValueError('Approved page count mismatch')
        for proof in e['metadata_evidence']:
            if proof['passage'] not in record['pages'][proof['pdf_page_number']-1]['text']:
                raise ValueError('Metadata evidence does not reconstruct')
        sidecar=root/'output/regulatory_metadata/uda_gazette_2235_54_2021.json' if e['document_id']==GAZETTE else None
        meta=load_metadata(sidecar,e['document_id'],e['source_pdf'])
        if e['document_id']==GAZETTE and e['applicability']!=meta['raw'].get('applicability'):
            raise ValueError('Gazette applicability differs from preserved source metadata')
        meta.update({'jurisdiction':e['jurisdiction'],'publication_date':e['publication_date'],
                     'metadata_verification_basis':'approved_manifest_and_document_passages',
                     'step4_source':dict(e)})
        meta['warnings']+=e['extraction_warnings']
        meta['warnings']+=['Retrieval relevance is not parcel-level legal applicability.','Automated compliance approval is disabled.']
        if e['council_name_ambiguity']:
            meta['warnings'].append('Source uses both Urban Council and Municipal Council labels for Maharagama; inconsistency unresolved.')
        docs.append({'record':record,'record_path':str(record_path),'record_sha256':sha256(record_path),'pdf_path':str(pdf),
                     'metadata':meta,'metadata_sha256':sha256(sidecar) if sidecar else None})
    return docs

def build_step4_index(documents):
    from .index import build_index
    if len(documents)!=3 or {d['record']['document_id'] for d in documents}!=set(APPROVED):
        raise ValueError('STEP 4 index requires exactly the approved three sources')
    index=build_index(documents)
    index['corpus_profile']='step4_approved_three_documents'
    index['step4_contract_version']=1
    index['source_registry']={d['record']['document_id']:d['metadata']['step4_source'] for d in documents}
    return index
