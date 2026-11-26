"""Deterministic BM25 with corpus-derived margin/identifier weights and phrases."""
from collections import Counter
import math
import re
import unicodedata
from pathlib import PureWindowsPath

# Generic English function words; no domain clauses, fixture terms or answers.
STOPWORDS = frozenset('a an and are as at be been being by for from how i in into is it its of on or that the their these this those to under was were what when where which who why with'.split())
DEFAULT_RANKING = {'identifier_weight': 0.05, 'edge_weight': 0.1,
                   'phrase_weight': 0.15, 'phrase_cap': 0.3,
                   'remove_stopwords': True, 'edge_lines': 2,
                   'edge_min_pages': 3, 'edge_page_fraction': 0.5}

def ranking_config(overrides=None):
    config = dict(DEFAULT_RANKING)
    if overrides is not None and (not isinstance(overrides, dict) or set(overrides) - set(config)):
        raise ValueError('Unknown ranking parameter')
    config.update(overrides or {})
    for name in ['identifier_weight','edge_weight','phrase_weight','phrase_cap','edge_page_fraction']:
        value=config[name]
        if type(value) not in (int,float) or not math.isfinite(value) or not 0 <= value <= 1:
            raise ValueError('Ranking weight must be finite and between zero and one')
    for name in ['edge_lines','edge_min_pages']:
        if type(config[name]) is not int or config[name] < 1:
            raise ValueError('Edge counts must be positive integers')
    if type(config['remove_stopwords']) is not bool:
        raise ValueError('remove_stopwords must be Boolean')
    return config

def edge_signature(line):
    # Ignore pagination/date digits only for repetition detection.
    return ' '.join(t for t in tokenize(line) if not t.isdigit())

def edge_ranges(record, config):
    candidates={}; counts=Counter()
    for page in record['pages']:
        lines=[];pos=0
        for line in page['text'].splitlines(keepends=True):
            if line.strip():lines.append((pos,pos+len(line),edge_signature(line)))
            pos+=len(line)
        edge_count=config['edge_lines'] if len(lines)>2*config['edge_lines'] else 1
        selected=lines[:edge_count]+lines[-edge_count:]
        selected=list(dict.fromkeys(selected))
        # Short labels and isolated numbers are not reliable boilerplate.
        selected=[row for row in selected if len(row[2])>=20]
        candidates[page['page_number']]=selected
        counts.update({row[2] for row in selected})
    minimum=max(config['edge_min_pages'],math.ceil(len(record['pages'])*config['edge_page_fraction']))
    return {pn:[(lo,hi) for lo,hi,sig in rows if counts[sig]>=minimum] for pn,rows in candidates.items()}

def identifier_terms(document):
    raw=document['metadata'].get('raw',{})
    # Derive identifiers from source metadata, never from evaluation data.
    values=[document['record']['source'],str(raw.get('gazette_number','')),str(raw.get('publication_date',''))]
    return sorted({t for value in values for t in tokenize(value) if t.isdigit()})

def tokenize(text):
    return re.findall(r'[^\W_]+', unicodedata.normalize('NFKC', text).casefold(), re.UNICODE)

def build_index(documents, size=1000, overlap=150, ranking=None):
    from .chunking import chunk_document
    config=ranking_config(ranking)
    seen_ids=set();seen_sources=set()
    for document in documents:
        did=document['record']['document_id']
        source=str(PureWindowsPath(document['record']['source'])).casefold()
        if did in seen_ids or source in seen_sources:
            raise ValueError('Duplicate document identity or source')
        seen_ids.add(did);seen_sources.add(source)
    chunks, skipped = [], []
    edges={};identifiers={}
    for document in documents:
        did=document['record']['document_id']
        edges[did]=edge_ranges(document['record'],config)
        identifiers[did]=identifier_terms(document)
        part, empty = chunk_document(document, size, overlap)
        chunks.extend(part); skipped.extend(empty)
    chunks.sort(key=lambda c: (c['document_id'], c['pdf_page_number'], c['start_char']))
    df = Counter()
    for chunk in chunks:
        tf = Counter(tokenize(chunk['text']))
        chunk['term_frequencies'] = dict(sorted(tf.items()))
        chunk['length'] = sum(tf.values())
        ranges=edges[chunk['document_id']][chunk['pdf_page_number']]
        weighted=Counter()
        for match in re.finditer(r'[^\W_]+',chunk['text'],re.UNICODE):
            lo=chunk['start_char']+match.start();hi=chunk['start_char']+match.end()
            edge=any(lo<end and hi>start for start,end in ranges)
            for token in tokenize(match.group()):
                weighted[token]+=config['edge_weight'] if edge else 1.0
        chunk['ranking_term_frequencies']=dict(sorted(weighted.items()))
        chunk['ranking_length']=sum(weighted.values())
        # Phrase tokens exclude repeated margins; original text is untouched.
        content=[];position=0
        for start,end in sorted(ranges):
            lo=max(0,start-chunk['start_char']);hi=min(len(chunk['text']),end-chunk['start_char'])
            if hi>lo:
                content.append(chunk['text'][position:lo]);position=hi
        content.append(chunk['text'][position:])
        chunk['content_bigrams']=[list(pair) for pair in sorted(set(zip(tokenize(' '.join(content)),tokenize(' '.join(content))[1:])))]
        df.update(tf.keys())
    return {'schema_version': 1, 'algorithm': 'BM25', 'k1': 1.5, 'b': .75,
            'ranking_version': 2, 'ranking_config': config, 'identifier_terms': identifiers,
            'chunk_size': size, 'overlap': overlap, 'chunks': chunks,
            'document_frequencies': dict(sorted(df.items())),
            'average_length': sum(c['length'] for c in chunks) / len(chunks) if chunks else 0,
            'skipped_pages': skipped,
            'sources': [{'document_id': d['record']['document_id'], 'source_pdf': d['record']['source'],
                         'record_path': d['record_path'], 'record_sha256': d['record_sha256'],
                         'pdf_path': d['pdf_path'], 'metadata_sha256': d['metadata_sha256'],
                         'page_count': d['record']['page_count']} for d in documents]}

def rank(index, query, k, filters=None, ranking=None):
    if type(k) is not int or not 1 <= k <= 100:
        raise ValueError('k must be an integer from 1 to 100')
    if not isinstance(query, str):
        raise ValueError('Query must be a string')
    allowed = {'document_id', 'source_pdf', 'jurisdiction', 'publication_date'}
    if filters is not None and not isinstance(filters, dict):
        raise ValueError('Filters must be an object')
    filters = filters or {}
    if set(filters) - allowed or any(not isinstance(v, str) or not v for v in filters.values()):
        raise ValueError('Unsupported filter or value')
    chunks = [c for c in index['chunks'] if all((c.get(key) if key in {'document_id','source_pdf'} else c['metadata'].get(key)) == value for key,value in filters.items())]
    if ranking is not None:
        if not isinstance(ranking,dict):raise ValueError('Ranking overrides must be an object')
        if index.get('ranking_version')!=2:raise ValueError('Ranking overrides require a version-2 index')
        build_only={'edge_weight','edge_lines','edge_min_pages','edge_page_fraction'}
        if any(name in ranking and ranking[name]!=index['ranking_config'][name] for name in build_only):
            raise ValueError('Edge suppression changes require a rebuilt index')
    config=ranking_config(index.get('ranking_config',{}) | (ranking or {})) if index.get('ranking_version')==2 else None
    query_tokens=tokenize(query)
    terms = set(query_tokens)
    if config and config['remove_stopwords']:
        terms-=STOPWORDS
    if not terms or not chunks:
        return []
    # Restrict the candidate corpus before computing ranking statistics.
    tf_key='ranking_term_frequencies' if config else 'term_frequencies'
    length_key='ranking_length' if config else 'length'
    df = Counter(t for c in chunks for t in c[tf_key] if c[tf_key][t]>0)
    n = len(chunks); avg = sum(c[length_key] for c in chunks) / n
    if not avg:
        return []
    results = []
    for c in chunks:
        score = 0.
        for term in sorted(terms):
            tf = c[tf_key].get(term, 0)
            if tf:
                idf = math.log(1 + (n - df[term] + .5) / (df[term] + .5))
                weight=1.0
                if config:
                    ids=set(index['identifier_terms'].get(c['document_id'],[]))
                    if term in ids and terms-ids:
                        weight=config['identifier_weight']
                score += weight * idf * tf * (index['k1'] + 1) / (tf + index['k1'] * (1 - index['b'] + index['b'] * c[length_key]/avg))
        if config and score>0:
            pairs={pair for pair in zip(query_tokens,query_tokens[1:]) if set(pair)<=terms and not set(pair)&set(index['identifier_terms'].get(c['document_id'],[]))}
            present={tuple(pair) for pair in c['content_bigrams']}
            bonus=sum(math.log(1+(n-df[a]+.5)/(df[a]+.5))+math.log(1+(n-df[b]+.5)/(df[b]+.5)) for a,b in pairs&present)*config['phrase_weight']
            score+=min(bonus,score*config['phrase_cap'])
        if score > 0:
            results.append((score,c))
    results.sort(key=lambda row: (-row[0], row[1]['chunk_id']))
    return results[:k]
