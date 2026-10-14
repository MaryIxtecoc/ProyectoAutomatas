"""
Autómata finito de corrección gramatical (post-procesamiento).

Los modelos estadísticos (Stanza/spaCy) a veces se equivocan con palabras
ambiguas en oraciones cortas: "yo estudio" (verbo) vs "el estudio"
(sustantivo), "corre y salta", "ayer". Este módulo recorre la secuencia de
palabras YA etiquetadas con un AUTÓMATA FINITO DETERMINISTA (AFD) cuyo
estado resume el contexto inmediato a la izquierda, y aplica reglas de
corrección según (estado, palabra actual).

    Alfabeto de entrada (clase de cada palabra):
        SUJ   pronombre personal sujeto (yo, tú, él, ella, nosotros...)
        DET   artículo / determinante (el, la, los, un, una, este...)
        VERB  verbo o auxiliar
        CONJ  conjunción copulativa (y, e, o, u, ni)
        OTRO  cualquier otra palabra
        CORTE signo de puntuación fuerte (. ; : ! ?)

    Estados:
        q0      inicio / sin contexto útil
        qSUJ    se acaba de leer un pronombre sujeto
        qDET    se acaba de leer un determinante
        qVERB   se acaba de leer un verbo
        qVCONJ  se leyó VERB seguido de CONJ   (patrón "corre y _")

    Reglas (se aplican al leer la palabra actual en el estado indicado):
        R1  qSUJ   + NOUN/ADJ/PROPN con lema verbal en el diccionario -> VERB
        R2  qDET   + VERB                                            -> NOUN
        R3  qVCONJ + NOUN/ADJ/PROPN con lema verbal                 -> VERB
        R4  cualquier estado + terminación -amos/-emos/-imos con
            lema verbal en el diccionario                            -> VERB
        R5  palabras de clase cerrada (ayer, hoy, sí, no...)         -> ADV
            (nunca verbo, sustantivo ni pronombre)
        R6  pronombres sujeto (yo, tú, nosotras...)                 -> PRON

Además se corrige el lema de todo VERB con el diccionario morfológico
(volverían -> volver, aprendimos -> aprender, come -> comer).
"""

from __future__ import annotations

from dataclasses import dataclass, field

# --------------------------------------------------------------------------
# Datos por idioma (por ahora reglas para español; el AFD es genérico)
# --------------------------------------------------------------------------

SUBJECT_PRONOUNS = {
    "ES": {"yo", "tú", "él", "ella", "usted", "nosotros", "nosotras",
           "vosotros", "vosotras", "ellos", "ellas", "ustedes"},
}

DETERMINERS = {
    "ES": {"el", "la", "los", "las", "un", "una", "unos", "unas", "este",
           "esta", "estos", "estas", "ese", "esa", "esos", "esas", "aquel",
           "aquella", "aquellos", "aquellas", "mi", "mis", "tu", "tus", "su",
           "sus", "nuestro", "nuestra", "nuestros", "nuestras", "del", "al"},
}

CONJUNCTIONS = {"ES": {"y", "e", "o", "u", "ni"}}

# Palabras de clase cerrada que los modelos a veces etiquetan mal.
CLOSED_ADVERBS = {
    "ES": {"ayer", "hoy", "anoche", "anteayer", "siempre", "nunca", "jamás",
           "sí", "si", "no", "también", "tampoco", "ya", "aquí", "allí",
           "allá", "acá", "ahí", "luego", "después", "antes", "pronto",
           "tarde", "temprano", "bien", "mal", "muy", "más", "menos",
           "todavía", "aún", "quizá", "quizás", "casi", "solo", "sólo"},
}
# "tarde" y "mañana" son también sustantivos ("por la tarde"): R5 no se
# aplica si el estado es qDET.

PLURAL_1P_ENDINGS = ("amos", "emos", "imos")
STRONG_PUNCT = {".", ";", ":", "!", "?", "¡", "¿", "…"}

VERB_TAGS = {"VERB", "AUX"}
NOUNISH_TAGS = {"NOUN", "ADJ", "PROPN"}


@dataclass
class Token:
    """Palabra ya etiquetada por el modelo."""
    text: str
    lemma: str
    pos: str                  # etiqueta universal (UPOS)
    personal: bool = False    # pronombre personal (PronType=Prs)
    is_stop: bool = False
    rule: str | None = None   # regla del autómata que la corrigió

    @property
    def lower(self) -> str:
        return self.text.lower()


@dataclass
class RuleAutomaton:
    language: str
    lexicon: "VerbLexicon | None" = None
    corrections: list[dict] = field(default_factory=list)

    # ---- Clasificación de la entrada (alfabeto del AFD) -------------------
    def symbol(self, tok: Token) -> str:
        w = tok.lower
        if w in STRONG_PUNCT:
            return "CORTE"
        if w in SUBJECT_PRONOUNS.get(self.language, set()):
            return "SUJ"
        if w in DETERMINERS.get(self.language, set()) and tok.pos in {"DET", "ADP", "PRON"}:
            return "DET"
        if w in CONJUNCTIONS.get(self.language, set()):
            return "CONJ"
        if tok.pos in VERB_TAGS:
            return "VERB"
        return "OTRO"

    # ---- Función de transición δ(estado, símbolo) -------------------------
    TRANSITIONS = {
        "q0":     {"SUJ": "qSUJ", "DET": "qDET", "VERB": "qVERB", "CONJ": "q0",     "OTRO": "q0", "CORTE": "q0"},
        "qSUJ":   {"SUJ": "qSUJ", "DET": "qDET", "VERB": "qVERB", "CONJ": "q0",     "OTRO": "q0", "CORTE": "q0"},
        "qDET":   {"SUJ": "qSUJ", "DET": "qDET", "VERB": "qVERB", "CONJ": "q0",     "OTRO": "q0", "CORTE": "q0"},
        "qVERB":  {"SUJ": "qSUJ", "DET": "qDET", "VERB": "qVERB", "CONJ": "qVCONJ", "OTRO": "q0", "CORTE": "q0"},
        "qVCONJ": {"SUJ": "qSUJ", "DET": "qDET", "VERB": "qVERB", "CONJ": "q0",     "OTRO": "q0", "CORTE": "q0"},
    }

    def verb_lemma(self, tok: Token) -> str | None:
        return self.lexicon.verb_lemma(tok.lower) if self.lexicon else None

    def _fix(self, tok: Token, pos: str, lemma: str, rule: str) -> None:
        self.corrections.append({"word": tok.text, "from": tok.pos, "to": pos, "rule": rule})
        tok.pos, tok.lemma, tok.rule = pos, lemma, rule

    # ---- Ejecución ---------------------------------------------------------
    def run(self, tokens: list[Token]) -> list[Token]:
        if self.language not in SUBJECT_PRONOUNS:
            # Sin reglas para este idioma: solo se corrige el lema verbal.
            for tok in tokens:
                self._normalize_verb_lemma(tok)
            return tokens

        state = "q0"
        closed = CLOSED_ADVERBS.get(self.language, set())

        for tok in tokens:
            w = tok.lower
            vlemma = self.verb_lemma(tok)

            # R6: los pronombres sujeto son clase cerrada: siempre pronombre
            # personal ("Nosotras" al inicio de oración no es nombre propio).
            if w in SUBJECT_PRONOUNS.get(self.language, set()) and (tok.pos != "PRON" or not tok.personal):
                if tok.pos != "PRON":
                    self.corrections.append({"word": tok.text, "from": tok.pos, "to": "PRON", "rule": "R6"})
                tok.pos, tok.personal, tok.lemma, tok.rule = "PRON", True, w, tok.rule or "R6"
            # R5: clase cerrada (excepto "la tarde", "la mañana": estado qDET)
            if w in closed and state != "qDET" and tok.pos in VERB_TAGS | NOUNISH_TAGS | {"PRON"}:
                self._fix(tok, "ADV", w, "R5")
            # R1: pronombre sujeto + palabra con lema verbal -> verbo
            elif state == "qSUJ" and tok.pos in NOUNISH_TAGS and vlemma:
                self._fix(tok, "VERB", vlemma, "R1")
            # R2: determinante + "verbo" -> sustantivo ("el estudio")
            elif (state == "qDET" and tok.pos == "VERB"
                  and not (self.lexicon and self.lexicon.is_verb_lemma(w))):
                # (no aplica a infinitivos: "al regresar", "el comer")
                self._fix(tok, "NOUN", noun_singular(w), "R2")
            # R3: "corre y salta": verbo + conjunción + palabra verbal -> verbo
            elif state == "qVCONJ" and tok.pos in NOUNISH_TAGS and vlemma:
                self._fix(tok, "VERB", vlemma, "R3")
            # R4: primera persona plural (-amos/-emos/-imos)
            elif (tok.pos in NOUNISH_TAGS and w.endswith(PLURAL_1P_ENDINGS)
                  and vlemma and state != "qDET"):
                self._fix(tok, "VERB", vlemma, "R4")

            self._normalize_verb_lemma(tok)
            state = self.TRANSITIONS[state][self.symbol(tok)]

        return tokens

    def _normalize_verb_lemma(self, tok: Token) -> None:
        """Corrige lemas verbales imposibles con el diccionario morfológico."""
        if tok.pos not in VERB_TAGS or not self.lexicon:
            return
        good = self.lexicon.verb_lemma(tok.lower)
        if good and not self.lexicon.is_verb_lemma(tok.lemma.lower()):
            tok.lemma = good


def noun_singular(word: str) -> str:
    """Singular aproximado para sustantivos corregidos por R2."""
    if len(word) > 4 and word.endswith("es") and word[-3] not in "aeiouáéíóú":
        return word[:-2]
    if len(word) > 3 and word.endswith("s"):
        return word[:-1]
    return word


class VerbLexicon:
    """
    Diccionario morfológico forma -> lema (spacy-lookups-data, ~490 mil
    formas del español). Se usa SOLO para verbos: algunas formas son
    ambiguas ("fotografías" -> "fotografiar"), así que nunca se usa para
    cambiar sustantivos.
    """

    INFINITIVE_ENDINGS = {
        "ES": ("ar", "er", "ir", "ír"),
    }

    def __init__(self, language: str, table):
        # table: spacy.lookups.Table (claves con hash) o dict normal
        self.language = language
        self.table = table
        endings = self.INFINITIVE_ENDINGS.get(language, ())
        not_verbs = CLOSED_ADVERBS.get(language, set())
        candidates = {
            v for v in table.values()
            if isinstance(v, str) and endings and v.endswith(endings) and v not in not_verbs
        }
        # Un lema es verbo solo si el diccionario también conoce su gerundio
        # (caminar -> caminando, comer -> comiendo). Así se descartan
        # sustantivos que terminan igual que un infinitivo: mujer, lugar, mar.
        self.verb_lemmas = {v for v in candidates if self._has_gerund(v)}

    def _has_gerund(self, lemma: str) -> bool:
        stem = lemma[:-2]
        if lemma.endswith("ar"):
            options = (stem + "ando",)
        else:
            options = (stem + "iendo", stem + "yendo", lemma[:-1] + "ndo")
        return any(self.table.get(g) == lemma for g in options)

    def is_verb_lemma(self, lemma: str) -> bool:
        return lemma in self.verb_lemmas

    def verb_lemma(self, form: str) -> str | None:
        lemma = self.table.get(form)
        if lemma and lemma in self.verb_lemmas:
            return lemma
        if form in self.verb_lemmas:      # ya es infinitivo
            return form
        return None

    @classmethod
    def load(cls, language: str) -> "VerbLexicon | None":
        if language not in cls.INFINITIVE_ENDINGS:
            return None
        try:
            from spacy.lookups import load_lookups

            table = load_lookups(language.lower(), ["lemma_lookup"]).get_table("lemma_lookup")
            return cls(language, table)
        except Exception:
            return None
