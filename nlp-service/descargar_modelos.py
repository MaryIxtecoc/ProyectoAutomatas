"""Descarga los modelos que usa el servicio. Se ejecuta una sola vez."""
import os
import subprocess
import sys

LANGS = {x.strip().upper() for x in os.getenv("NLP_LANGUAGES", "ES,EN,RU,ZH,AR").split(",")}

SPACY = {
    "ES": os.getenv("NLP_MODEL_ES", "es_core_news_md"),
    "EN": "en_core_web_sm",
    "RU": "ru_core_news_sm",
    "ZH": "zh_core_web_sm",
}

STANZA = {
    "ES": ("es", "tokenize,mwt,pos,lemma"),
    "EN": ("en", "tokenize,mwt,pos,lemma"),
    "RU": ("ru", "tokenize,pos,lemma"),
    "ZH": ("zh-hans", "tokenize,pos,lemma"),
    "AR": ("ar", "tokenize,mwt,pos,lemma,ner"),
}

errores = []

for lang, model in SPACY.items():
    if lang in LANGS:
        print(f"[spaCy] Descargando {model}...")
        if subprocess.call([sys.executable, "-m", "spacy", "download", model]) != 0:
            errores.append(model)

if os.getenv("NLP_POS_ENGINE", "stanza").lower() == "stanza" or "AR" in LANGS:
    import stanza

    for lang, (code, processors) in STANZA.items():
        if lang not in LANGS:
            continue
        if lang != "AR" and os.getenv("NLP_POS_ENGINE", "stanza").lower() != "stanza":
            continue
        package = os.getenv("NLP_STANZA_PACKAGE", "default_fast")
        reduced = ",".join(p for p in processors.split(",") if p not in ("mwt", "ner"))
        print(f"[Stanza] Descargando {code} ({processors}, paquete {package})...")
        ok = False
        for pkg in dict.fromkeys([package, "default"]):
            for procs in (processors, reduced):
                try:
                    stanza.download(code, processors=procs, package=pkg, verbose=False)
                    ok = True
                    break
                except Exception as exc:  # paquete o procesador no disponible
                    print(f"  aviso ({pkg}, {procs}): {exc}")
            if ok:
                break
        if not ok:
            errores.append(f"stanza-{code}")

if errores:
    print("\nNo se pudieron descargar:", errores)
    print("El servicio usará spaCy (o el analizador del backend) para esos idiomas.")
else:
    print("\nModelos listos.")
