"""
Evaluación de precisión del analizador contra un conjunto de oraciones
etiquetadas a mano (gold.json). Mide precisión, exhaustividad (recall) y F1
para verbos, sustantivos, pronombres y personas.

Uso (desde la carpeta nlp-service, con el entorno virtual activo):
    python evaluacion/evaluar.py              # con el autómata de reglas
    set NLP_RULES=0 && python evaluacion/evaluar.py   (Windows: sin reglas)

Sirve como evidencia de pruebas de caja negra para el informe.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

import app  # noqa: E402

FIELDS = {
    "verbs": "rootVerbs",
    "nouns": "rootNouns",
    "pronouns": "personalPronouns",
    "persons": "personNames",
}


def norm(values):
    return {v.lower() for v in values}


def main():
    gold = json.load(open(os.path.join(HERE, "gold.json"), encoding="utf-8"))
    totals = {}
    errores = []
    motores = set()

    for item in gold:
        result = app.run_analysis(item["text"], item["lang"])
        motores.add(f'{item["lang"]}={result["engine"]}')
        for field, key in FIELDS.items():
            expected, got = norm(item[field]), norm(result[key])
            stats = totals.setdefault((item["lang"], field), [0, 0, 0])
            stats[0] += len(expected & got)        # aciertos
            stats[1] += len(got - expected)        # sobran
            stats[2] += len(expected - got)        # faltan
            if expected != got:
                errores.append((item["lang"], field, item["text"][:50],
                                sorted(expected - got), sorted(got - expected)))

    print(f"Motores: {', '.join(sorted(motores))} | Reglas: {'sí' if app.RULES_ENABLED else 'no'}\n")
    print(f"{'Idioma':<7}{'Campo':<10}{'Precisión':>10}{'Recall':>9}{'F1':>8}")
    tp_all = fp_all = fn_all = 0
    for (lang, field), (tp, fp, fn) in sorted(totals.items()):
        p = tp / (tp + fp) if tp + fp else 1.0
        r = tp / (tp + fn) if tp + fn else 1.0
        f1 = 2 * p * r / (p + r) if p + r else 0.0
        tp_all, fp_all, fn_all = tp_all + tp, fp_all + fp, fn_all + fn
        print(f"{lang:<7}{field:<10}{p:>10.1%}{r:>9.1%}{f1:>8.1%}")
    p = tp_all / (tp_all + fp_all)
    r = tp_all / (tp_all + fn_all)
    print(f"\nGLOBAL   precisión {p:.1%}  recall {r:.1%}  F1 {2*p*r/(p+r):.1%}")

    if errores:
        print("\nDiferencias (faltan / sobran):")
        for lang, field, text, missing, extra in errores:
            print(f"  [{lang}] {field:<9} {text!r}: faltan {missing} sobran {extra}")


if __name__ == "__main__":
    main()
