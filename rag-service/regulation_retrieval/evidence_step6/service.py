"""Opt-in orchestration: unchanged STEP 4 retrieval followed by assessment."""
from ..service import RegulationRetrievalService
from .assessor import EvidenceAssessor
from .grouping import group_evidence

class EvidenceValidationService:
    def __init__(self,index,documents,config=None):
        self.retriever=RegulationRetrievalService(index)
        if self.retriever.index.get('corpus_profile')!='step4_approved_three_documents':raise ValueError('STEP 4 approved index required')
        self.assessor=EvidenceAssessor(documents,config)

    def search(self,query,k=5,filters=None,requested_document_ids=None,conflict_reported=False):
        response=self.retriever.search(query,k,filters)
        jurisdiction=(filters or {}).get('jurisdiction')
        requested=requested_document_ids
        if requested is None and filters and 'document_id' in filters:requested=[filters['document_id']]
        evidence=group_evidence(self.assessor,query,response['results'],requested,jurisdiction,conflict_reported)
        return {'retrieval':response,'evidence':evidence,'answer_generated':False,'automated_compliance_approved':False}
