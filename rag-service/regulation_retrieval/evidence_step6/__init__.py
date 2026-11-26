"""Opt-in evidence-quality heuristics; no semantic or legal sufficiency claim."""
from .assessor import EvidenceAssessor
from .grouping import group_evidence
__all__=['EvidenceAssessor','group_evidence']
