"""Explainable structural/lexical assessment against verified source snapshots."""
import copy,hashlib,json,re
from pathlib import Path
from ..approved_sources_step4 import APPROVED
from ..corpus import sha256
from ..index import tokenize,STOPWORDS

DEFAULT_CONFIG={'minimum_coverage':0.45,'minimum_passage_chars':80}
STATUSES={'EVIDENCE_CANDIDATE','INSUFFICIENT_EVIDENCE','AMBIGUOUS_EVIDENCE','JURISDICTION_UNVERIFIED'}

def passage_id(did,pn,start,end,text):
    value=f'{did}:{pn}:{start}:{end}:'+hashlib.sha256(text.encode()).hexdigest()
    return hashlib.sha256(value.encode()).hexdigest()

class EvidenceAssessor:
    def __init__(self,documents,config=None):
        self.config=dict(DEFAULT_CONFIG)
        if config is not None:
            if not isinstance(config,dict) or set(config)-set(DEFAULT_CONFIG):raise ValueError('Invalid evidence configuration')
            self.config.update(config)
        if type(self.config['minimum_coverage']) not in (int,float) or not 0<=self.config['minimum_coverage']<=1 or type(self.config['minimum_passage_chars']) is not int or self.config['minimum_passage_chars']<1:
            raise ValueError('Invalid evidence thresholds')
        self.records={};self.registry={};self.source_hashes={}
        for d in documents:
            r=d['record'];did=r['document_id']
            if did not in APPROVED or did in self.records or r['source']!=APPROVED[did]['source_pdf']:
                raise ValueError('Unapproved/duplicate source snapshot')
            if sha256(d['pdf_path'])!=did or sha256(d['record_path'])!=d['record_sha256']:
                raise ValueError('Source snapshot identity mismatch')
            if r!=json.loads(Path(d['record_path']).read_text(encoding='utf-8-sig')):
                raise ValueError('In-memory source differs from verified extracted record')
            self.records[did]=copy.deepcopy(r);self.registry[did]=copy.deepcopy(d['metadata']['step4_source']);self.source_hashes[did]=d['record_sha256']
        if set(self.records)!=set(APPROVED):raise ValueError('Exactly the approved source snapshots required')

    def assess(self,query,passage,requested_jurisdiction=None):
        if not isinstance(query,str):raise ValueError('Query must be text')
        if requested_jurisdiction is not None and (not isinstance(requested_jurisdiction,str) or not requested_jurisdiction.strip()):raise ValueError('Invalid requested jurisdiction')
        result={'status':'INSUFFICIENT_EVIDENCE','reasons':[],'source_identity_verified':False,'provenance_verified':False,
                'lexical_coverage':0.0,'semantic_sufficiency':'NOT_ESTABLISHED','legal_correctness':'NOT_ESTABLISHED',
                'legal_status':'not_verified','answer_generated':False,'automated_compliance_approved':False,
                'warnings':['EVIDENCE_CANDIDATE is a heuristic retrieval-quality label, not proof of entailment or legal applicability.']}
        def finish(status,*reasons):return result|{'status':status,'reasons':list(reasons)}
        if not isinstance(passage,dict):return finish('INSUFFICIENT_EVIDENCE','INVALID_PASSAGE_SCHEMA')
        did=passage.get('document_id');result['document_id']=did
        if not isinstance(did,str) or did not in self.records:return finish('INSUFFICIENT_EVIDENCE','UNAPPROVED_OR_MISSING_SOURCE')
        record=self.records[did];meta=self.registry[did]
        if passage.get('source_pdf')!=record['source']:return finish('INSUFFICIENT_EVIDENCE','SOURCE_REFERENCE_MISMATCH')
        result['source_identity_verified']=True
        for primary,alias in [('original_text_start_offset','start_char'),('original_text_end_offset','end_char'),('retrieved_passage','text')]:
            if primary in passage and alias in passage and (type(passage[primary]) is not type(passage[alias]) or passage[primary]!=passage[alias]):
                return finish('INSUFFICIENT_EVIDENCE','INCONSISTENT_PROVENANCE_ALIASES')
        pn=passage.get('pdf_page_number');start=passage.get('original_text_start_offset',passage.get('start_char'));end=passage.get('original_text_end_offset',passage.get('end_char'));text=passage.get('retrieved_passage',passage.get('text'))
        if type(pn) is not int or not 1<=pn<=record['page_count'] or type(start) is not int or type(end) is not int or not isinstance(text,str):
            return finish('INSUFFICIENT_EVIDENCE','INVALID_PAGE_OR_OFFSETS')
        original=record['pages'][pn-1]['text']
        if not 0<=start<end<=len(original) or original[start:end]!=text:return finish('INSUFFICIENT_EVIDENCE','PASSAGE_DOES_NOT_RECONSTRUCT')
        if passage.get('chunk_id')!=passage_id(did,pn,start,end,text):return finish('INSUFFICIENT_EVIDENCE','PASSAGE_ID_MISMATCH')
        result.update({'provenance_verified':True,'citation':{'document_id':did,'source_pdf':record['source'],'pdf_page_number':pn,'start_char':start,'end_char':end},
                       'jurisdiction':meta['jurisdiction'],'jurisdiction_verification_status':meta['jurisdiction_verification_status'],
                       'documented_applicability':meta['applicability'],'council_name_ambiguity':meta['council_name_ambiguity'],
                       'passage_chars':len(text),'retrieved_passage':text})
        known={m['jurisdiction'] for m in self.registry.values() if m['jurisdiction_verification_status'].startswith('document_supported')}
        if requested_jurisdiction is not None and (requested_jurisdiction not in known or meta['jurisdiction']!=requested_jurisdiction):
            return finish('JURISDICTION_UNVERIFIED','REQUESTED_JURISDICTION_NOT_SUPPORTED_BY_SOURCE')
        if passage.get('jurisdiction',meta['jurisdiction'])!=meta['jurisdiction'] or passage.get('jurisdiction_verification_status',meta['jurisdiction_verification_status'])!=meta['jurisdiction_verification_status']:
            return finish('JURISDICTION_UNVERIFIED','RESPONSE_JURISDICTION_CONTRADICTS_SOURCE_METADATA')
        query_terms=set(tokenize(query))-STOPWORDS-{'does','do','did','can','shall','should'}
        shared=sorted(query_terms&set(tokenize(text)));coverage=len(shared)/len(query_terms) if query_terms else 0.
        result.update({'query_content_terms':sorted(query_terms),'matched_terms':shared,'missing_terms':sorted(query_terms-set(shared)),'lexical_coverage':coverage})
        result['clause_heading_hints']=re.findall(r'(?m)^\s*(\d{1,3})[.)]\s',text)
        requested_refs=re.findall(r'(?i)\b(regulation|section|schedule|clause)\s+(\d+[a-z]?)\b',query)
        actual_refs={(a.casefold(),b.casefold()) for a,b in re.findall(r'(?i)\b(regulation|section|schedule|clause)\s+(\d+[a-z]?)\b',text)}
        result['explicit_reference_checks']=[{'kind':kind.casefold(),'number':number.casefold(),'literal_match':(kind.casefold(),number.casefold()) in actual_refs} for kind,number in requested_refs]
        cut_start=start>0 and original[start-1].isalnum() and bool(text) and text[0].isalnum()
        cut_end=end<len(original) and original[end].isalnum() and bool(text) and text[-1].isalnum()
        result['completeness_checks']={'starts_inside_word':cut_start,'ends_inside_word':cut_end,'paragraph_and_clause_completeness':'NOT_ESTABLISHED',
                                       'qualifier_terms':sorted(set(tokenize(text))&{'unless','except','subject','provided','not','no'})}
        result['section_relevance']='EXPLICIT_REFERENCE_LITERAL_MATCH' if requested_refs and all(c['literal_match'] for c in result['explicit_reference_checks']) else 'LEXICAL_AND_HEADING_HINTS_ONLY'
        if not query_terms or len(text.strip())<self.config['minimum_passage_chars'] or len(shared)<min(2,len(query_terms)) or coverage<self.config['minimum_coverage']:
            return finish('INSUFFICIENT_EVIDENCE','LOW_LEXICAL_COVERAGE_OR_SHORT_PASSAGE','SEMANTIC_SUPPORT_NOT_ESTABLISHED')
        ambiguous=[]
        if cut_start or cut_end:ambiguous.append('PASSAGE_CUTS_A_WORD_CONTEXT_MAY_BE_INCOMPLETE')
        if record['pages'][pn-1].get('extraction_error'):ambiguous.append('PAGE_EXTRACTION_ERROR')
        if meta['council_name_ambiguity']:ambiguous.append('COUNCIL_NAME_INCONSISTENCY_UNRESOLVED')
        if requested_refs and not all(c['literal_match'] for c in result['explicit_reference_checks']):ambiguous.append('EXPLICIT_CLAUSE_OR_SECTION_REFERENCE_UNCONFIRMED')
        if re.search(r'(?i)\b(certify|approve|authorize|enforceable)\b|legally current|currently applicable',query):ambiguous.append('LEGAL_APPLICABILITY_OR_APPROVAL_NOT_VERIFIED')
        if passage.get('legal_status','not_verified')!='not_verified' or passage.get('legal_status_verified',False) is not False or passage.get('amendments_verified',False) is not False or passage.get('automated_compliance_approved',False) is not False:
            ambiguous.append('UNSUPPORTED_LEGAL_VERIFICATION_CLAIM')
        if ambiguous:return finish('AMBIGUOUS_EVIDENCE',*ambiguous,'SEMANTIC_SUPPORT_NOT_ESTABLISHED')
        return finish('EVIDENCE_CANDIDATE','SOURCE_AND_OFFSETS_VERIFIED','LEXICAL_THRESHOLD_MET','SEMANTIC_SUPPORT_NOT_ESTABLISHED')
