"""
Servicio de análisis lingüístico para AutomatasFinal (Léxica).

Recibe un texto y su idioma y devuelve, palabra por palabra, su categoría
(pronombre, sustantivo, verbo, nombre propio), su forma raíz (lema) y las
entidades nombradas (personas, lugares, organizaciones).

Arquitectura en capas:
  1. IA ligera: Stanza (categorías y raíces); spaCy como respaldo.
  2. Diccionario morfológico: corrige raíces verbales (volverían -> volver).
  3. Autómata finito de reglas (rules.py): corrige ambigüedades por contexto.
  4. Entidades: spaCy (Stanza en árabe) + unificación de nombres.

El backend .NET lo llama por HTTP. Si este servicio no responde, el backend
usa su analizador propio (TextAnalyzer.cs): nunca se queda sin análisis.
"""

from __future__ import annotations

import os
import threading
import time
from collections import OrderedDict

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field
from fastapi.middleware.cors import CORSMiddleware

import engines
from rules import CLOSED_ADVERBS, RuleAutomaton, Token, VerbLexicon

ENABLED = {
    x.strip().upper()
    for x in os.getenv("NLP_LANGUAGES", "ES,EN,RU,ZH,AR").split(",")
    if x.strip()
}
MAX_LOADED = int(os.getenv("NLP_MAX_LOADED_MODELS", "2"))
CHUNK_CHARS = int(os.getenv("NLP_CHUNK_CHARS", "20000"))
MAX_TEXT_CHARS = int(os.getenv("NLP_MAX_TEXT_CHARS", "6000000"))
# Permite apagar el autómata de reglas para comparar resultados (evaluar.py).
RULES_ENABLED = os.getenv("NLP_RULES", "1") != "0"

app = FastAPI(title="AutomatasFinal NLP", version="2.0.0")

@app.api_route("/", methods=["GET", "HEAD"])
def read_root():
    return {"status": "ok"}

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

_resources: "OrderedDict[str, engines.Resources]" = OrderedDict()
_lexicons: dict[str, VerbLexicon | None] = {}
_lock = threading.Lock()


class AnalyzeRequest(BaseModel):
    text: str = Field(..., description="Texto a analizar")
    language: str = Field(..., description="ES, EN, RU, ZH o AR")


# --------------------------------------------------------------------------
# Carga perezosa con límite de modelos en memoria
# --------------------------------------------------------------------------

def get_resources(language: str) -> engines.Resources:
    with _lock:
        if language in _resources:
            _resources.move_to_end(language)
            return _resources[language]
        while len(_resources) >= MAX_LOADED:
            _resources.popitem(last=False)
        res = engines.load_resources(language)
        _resources[language] = res
        return res


def get_lexicon(language: str) -> VerbLexicon | None:
    if language not in _lexicons:
        _lexicons[language] = VerbLexicon.load(language)
    return _lexicons[language]


# --------------------------------------------------------------------------
# Utilidades
# --------------------------------------------------------------------------

def chunks(text: str, size: int):
    """Corta el texto en bloques por saltos de línea para no partir palabras."""
    start, n = 0, len(text)
    while start < n:
        end = min(start + size, n)
        if end < n:
            cut = text.rfind("\n", start, end)
            if cut <= start:
                cut = text.rfind(" ", start, end)
            if cut > start:
                end = cut + 1
        yield text[start:end]
        start = end


def is_word(text: str) -> bool:
    return any(ch.isalpha() for ch in text)


def unify_person_names(names: list[str]) -> list[str]:
    """'María' y 'María López' son la misma persona: se deja el nombre completo."""
    result = []
    for name in names:
        parts = name.split()
        contained = len(parts) == 1 and any(
            other != name and name in other.split() for other in names
        )
        if not contained and name not in result:
            result.append(name)
    return result


# Pronombres personales del chino. El modelo chino no distingue el tipo de
# pronombre, así que en chino solo se cuentan los de esta lista
# (这 "este" o 这里 "aquí" son demostrativos, no personales).
ZH_PERSONAL_PRONOUNS = {
    "我", "你", "您", "他", "她", "它",
    "我们", "咱们", "你们", "您们", "他们", "她们", "它们",
    "自己",
}


class Collector:
    def __init__(self, language: str):
        self.language = language
        self.words: list[str] = []
        self.stop_count = 0
        self.pronouns: list[str] = []
        self.nouns: list[str] = []
        self.verbs: list[str] = []
        self.noun_count = 0
        self.verb_count = 0
        self.entities: dict[str, list[dict]] = {}
        self.corrections: list[dict] = []

    @staticmethod
    def _add(target: list[str], value: str) -> None:
        if value and value not in target:
            target.append(value)

    def tokens(self, toks: list[Token]) -> None:
        closed = CLOSED_ADVERBS.get(self.language, set())
        for t in toks:
            if not is_word(t.text):
                continue
            lower = t.lower
            self.words.append(lower)
            if t.is_stop:
                self.stop_count += 1
            lemma = (t.lemma or t.text).strip().lower()
            # Una letra suelta ("á", "é", "l") no es sustantivo ni verbo,
            # salvo en chino, donde un solo carácter sí puede ser palabra.
            too_short = self.language != "ZH" and len(lemma) < 2
            # Ruso: una letra suelta ("г", "б") no es palabra aunque el modelo
            # la lea como abreviatura ("г" -> "год").
            if self.language == "RU" and len(t.text.strip()) < 2:
                too_short = True
            if t.pos == "PRON" and t.personal and lower not in closed and (
                    self.language != "ZH" or lower in ZH_PERSONAL_PRONOUNS):
                self._add(self.pronouns, lower)
            elif too_short:
                continue
            elif t.pos == "NOUN":
                self.noun_count += 1
                if is_word(lemma):
                    self._add(self.nouns, lemma)
            elif t.pos in ("VERB", "AUX"):
                self.verb_count += 1
                if is_word(lemma):
                    self._add(self.verbs, lemma)

    def entity(self, text: str, label: str) -> None:
        kind = engines.ENTITY_MAP.get(label.upper())
        text = " ".join(text.split())
        if not kind or not is_word(text):
            return
        bucket = self.entities.setdefault(kind, [])
        if any(e["canonical"].lower() == text.lower() for e in bucket):
            return
        bucket.append({"text": text, "canonical": text, "language": self.language})

    def finish_entities(self) -> None:
        persons = self.entities.get("PERSON")
        if persons:
            keep = set(unify_person_names([p["canonical"] for p in persons]))
            self.entities["PERSON"] = [p for p in persons if p["canonical"] in keep]

    def result(self, res: engines.Resources, elapsed: float) -> dict:
        self.finish_entities()
        persons = [e["canonical"] for e in self.entities.get("PERSON", [])]
        model = (engines.STANZA_CONFIG[self.language][0]
                 if res.pos_engine == "stanza"
                 else engines.SPACY_MODELS.get(self.language, ""))
        return {
            "engine": res.pos_engine,
            "model": model,
            "language": self.language,
            "elapsedMs": int(elapsed * 1000),
            "words": self.words,
            "totalWordCount": len(self.words),
            "stopWordCount": self.stop_count,
            "personalPronouns": sorted(self.pronouns),
            "personNames": sorted(persons),
            "rootNouns": sorted(self.nouns),
            "rootVerbs": sorted(self.verbs),
            "nounCount": self.noun_count,
            "verbCount": self.verb_count,
            "entities": self.entities,
            "ruleCorrections": len(self.corrections),
            "corrections": self.corrections[:200],
        }


def run_analysis(text: str, language: str) -> dict:
    started = time.perf_counter()
    res = get_resources(language)
    lexicon = get_lexicon(language)
    col = Collector(language)

    for part in chunks(text, CHUNK_CHARS):
        spacy_doc = res.spacy(part) if res.spacy is not None else None

        # 1) Categorías y raíces
        if res.stanza is not None:
            stanza_doc = res.stanza(part)
            sentences = [
                [Token(text=w.text, lemma=w.lemma or w.text, pos=w.upos or "X",
                       personal=engines.stanza_is_personal(w.feats),
                       is_stop=w.text.lower() in res.stop_words)
                 for w in s.words]
                for s in stanza_doc.sentences
            ]
        else:
            stanza_doc = None
            sentences = engines.spacy_sentence_tokens(spacy_doc)

        # 2) y 3) Diccionario + autómata de reglas
        for sentence in sentences:
            if RULES_ENABLED:
                automaton = RuleAutomaton(language, lexicon)
                automaton.run(sentence)
                col.corrections.extend(automaton.corrections)
            col.tokens(sentence)

        # 4) Entidades
        if spacy_doc is not None:
            for text_ent, label in engines.spacy_entities(spacy_doc):
                col.entity(text_ent, label)
        elif stanza_doc is not None:
            for ent in getattr(stanza_doc, "ents", None) or []:
                col.entity(ent.text, ent.type)

    return col.result(res, time.perf_counter() - started)


# --------------------------------------------------------------------------
# Endpoints
# --------------------------------------------------------------------------

@app.get("/health")
def health() -> dict:
    return {
        "status": "ok",
        "posEngine": engines.POS_ENGINE,
        "languages": sorted(ENABLED),
        "loaded": {k: v.pos_engine for k, v in _resources.items()},
    }


@app.post("/analyze")
def analyze(request: AnalyzeRequest) -> dict:
    language = (request.language or "").strip().upper()
    if language not in engines.SUPPORTED:
        raise HTTPException(400, "Idioma no soportado. Use ES, EN, RU, ZH o AR.")
    if language not in ENABLED:
        raise HTTPException(503, f"El idioma {language} está deshabilitado en este servidor.")
    if len(request.text) > MAX_TEXT_CHARS:
        raise HTTPException(413, "El texto es demasiado grande.")

    try:
        return run_analysis(request.text, language)
    except RuntimeError as exc:
        raise HTTPException(503, str(exc))
