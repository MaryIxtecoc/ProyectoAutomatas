"""
Motores lingüísticos.

- Categorías y raíces (POS + lema): Stanza (red neuronal de Stanford,
  entrenada con Universal Dependencies). Si el modelo de Stanza no está
  instalado o falla al cargar, se usa spaCy automáticamente.
- Entidades (personas, lugares, organizaciones): spaCy para ES/EN/RU/ZH;
  Stanza para árabe (spaCy no tiene modelo árabe).
"""

from __future__ import annotations

import os
from dataclasses import dataclass
from typing import Any

from rules import Token

SPACY_MODELS = {
    "ES": os.getenv("NLP_MODEL_ES", "es_core_news_md"),
    "EN": "en_core_web_sm",
    "RU": "ru_core_news_sm",
    "ZH": "zh_core_web_sm",
}

# Procesadores de Stanza por idioma (mwt = separar "del" -> "de el").
STANZA_CONFIG = {
    "ES": ("es", "tokenize,mwt,pos,lemma"),
    "EN": ("en", "tokenize,mwt,pos,lemma"),
    "RU": ("ru", "tokenize,pos,lemma"),
    "ZH": ("zh-hans", "tokenize,pos,lemma"),
    "AR": ("ar", "tokenize,mwt,pos,lemma,ner"),
}

SUPPORTED = set(STANZA_CONFIG)

# "stanza" (por defecto) o "spacy" para categorías y raíces.
POS_ENGINE = os.getenv("NLP_POS_ENGINE", "stanza").lower()

# Paquete de Stanza: "default_fast" (sin modelo de caracteres, mucho más
# liviano en disco y memoria) o "default" (algo más preciso y más pesado).
STANZA_PACKAGE = os.getenv("NLP_STANZA_PACKAGE", "default_fast")

ENTITY_MAP = {
    "PER": "PERSON", "PERSON": "PERSON",
    "LOC": "LOCATION", "GPE": "LOCATION", "FAC": "LOCATION",
    "ORG": "ORGANIZATION", "NORP": "ORGANIZATION",
    "PRODUCT": "PRODUCT",
}


@dataclass
class Resources:
    language: str
    spacy: Any | None
    stanza: Any | None
    stop_words: set[str]

    @property
    def pos_engine(self) -> str:
        return "stanza" if self.stanza is not None else "spacy"


def load_resources(language: str) -> Resources:
    spacy_nlp = _load_spacy(language)
    stanza_nlp = _load_stanza(language) if (POS_ENGINE == "stanza" or language == "AR") else None

    if spacy_nlp is None and stanza_nlp is None:
        raise RuntimeError(f"No hay modelos instalados para {language}.")

    stop_words: set[str] = set()
    if spacy_nlp is not None:
        stop_words = set(spacy_nlp.Defaults.stop_words)

    return Resources(language, spacy_nlp, stanza_nlp, stop_words)


def _load_spacy(language: str):
    if language not in SPACY_MODELS:
        return None
    try:
        import spacy

        # El parser sintáctico no se usa: se excluye para ahorrar memoria.
        nlp = spacy.load(SPACY_MODELS[language], exclude=["parser"])
        nlp.max_length = 10_000_000
        return nlp
    except Exception:
        return None


def _load_stanza(language: str):
    code, processors = STANZA_CONFIG[language]
    try:
        import stanza

        reduced = ",".join(p for p in processors.split(",") if p not in ("mwt", "ner"))
        # Se prueba el paquete configurado y, si no está instalado, el normal;
        # si un idioma no tiene 'mwt' o 'ner', se reintenta sin ellos.
        for package in dict.fromkeys([STANZA_PACKAGE, "default"]):
            for procs in (processors, reduced):
                try:
                    return stanza.Pipeline(code, processors=procs, package=package,
                                           verbose=False, use_gpu=False,
                                           download_method=None)
                except Exception:
                    continue
        return None
    except Exception:
        return None


# --------------------------------------------------------------------------
# Adaptadores: convierten la salida de cada librería a listas de Token
# --------------------------------------------------------------------------

def stanza_is_personal(feats: str | None) -> bool:
    """Pronombre personal: PronType=Prs y que no sea posesivo (my, her, su)."""
    feats = feats or ""
    if "Poss=Yes" in feats:
        return False
    return "PronType=Prs" in feats or "PronType" not in feats


def spacy_sentence_tokens(doc) -> list[list[Token]]:
    sentences: list[list[Token]] = []
    current: list[Token] = []
    for t in doc:
        if t.is_space:
            continue
        prontype = t.morph.get("PronType")
        possessive = "Yes" in t.morph.get("Poss")
        current.append(Token(
            text=t.text,
            lemma=t.lemma_ or t.text,
            pos="PUNCT" if t.is_punct else t.pos_,
            personal=((not prontype) or ("Prs" in prontype)) and not possessive,
            is_stop=t.is_stop,
        ))
        if t.is_punct and t.text in {".", "!", "?", "…", ";"}:
            sentences.append(current)
            current = []
    if current:
        sentences.append(current)
    return sentences


def clean_entity_span(ent) -> str:
    """
    Los modelos pequeños a veces marcan como entidad palabras comunes
    ("Ayer", "CASA") o se llevan el verbo de al lado ("Juan camina").
    Se recorta la entidad a sus nombres propios (PROPN) y se descarta si no
    tiene ninguno.
    """
    tokens = [t for t in ent if not t.is_punct and not t.is_space]
    while tokens and tokens[0].pos_ != "PROPN":
        tokens.pop(0)
    while tokens and tokens[-1].pos_ != "PROPN":
        tokens.pop()
    if not tokens:
        return ""
    return ent.doc[tokens[0].i: tokens[-1].i + 1].text


def spacy_entities(doc) -> list[tuple[str, str]]:
    out = []
    for ent in doc.ents:
        text = clean_entity_span(ent)
        if text:
            out.append((text, ent.label_))
    return out
