"""Pruebas unitarias del autómata de reglas y del diccionario (pytest)."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from rules import RuleAutomaton, Token, VerbLexicon  # noqa: E402

LEX = VerbLexicon.load("ES")


def run(pairs):
    toks = [Token(text=t, lemma=t.lower(), pos=p) for t, p in pairs]
    RuleAutomaton("ES", LEX).run(toks)
    return {t.text: (t.pos, t.lemma) for t in toks}


def test_diccionario_distingue_verbos():
    assert LEX.verb_lemma("volverían") == "volver"
    assert LEX.verb_lemma("aprendimos") == "aprender"
    assert LEX.verb_lemma("mujer") is None
    assert LEX.verb_lemma("ayer") is None


def test_R1_pronombre_sujeto_mas_palabra_es_verbo():
    r = run([("Yo", "PRON"), ("estudio", "NOUN")])
    assert r["estudio"] == ("VERB", "estudiar")


def test_R2_determinante_mas_verbo_es_sustantivo():
    r = run([("El", "DET"), ("estudio", "VERB")])
    assert r["estudio"][0] == "NOUN"


def test_R2_no_aplica_a_infinitivo():
    r = run([("Al", "ADP"), ("regresar", "VERB")])
    assert r["regresar"][0] == "VERB"


def test_R3_verbo_conjuncion_palabra_es_verbo():
    r = run([("corre", "VERB"), ("y", "CCONJ"), ("salta", "NOUN")])
    assert r["salta"] == ("VERB", "saltar")


def test_R4_primera_persona_plural():
    r = run([("bebimos", "NOUN")])
    assert r["bebimos"] == ("VERB", "beber")


def test_R5_ayer_nunca_es_verbo():
    r = run([("Ayer", "VERB"), ("comimos", "VERB")])
    assert r["Ayer"][0] == "ADV"


def test_R5_si_afirmativo_no_es_pronombre():
    r = run([("que", "SCONJ"), ("sí", "PRON")])
    assert r["sí"][0] == "ADV"


def test_la_tarde_sigue_siendo_sustantivo():
    r = run([("la", "DET"), ("tarde", "NOUN")])
    assert r["tarde"][0] == "NOUN"


def test_corrige_lema_verbal_imposible():
    toks = [Token(text="volverían", lemma="volverir", pos="VERB")]
    RuleAutomaton("ES", LEX).run(toks)
    assert toks[0].lemma == "volver"


def test_R6_pronombre_sujeto_al_inicio():
    r = run([("Nosotras", "PROPN"), ("pintamos", "VERB")])
    assert r["Nosotras"][0] == "PRON"
