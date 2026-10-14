# Servicio NLP: Stanza + spaCy + autómata de reglas

Servicio en Python que hace el análisis lingüístico de Léxica: categoría de
cada palabra (pronombre, sustantivo, verbo, nombre propio), su forma raíz
(lema) y las entidades (personas, lugares, organizaciones).

## Arquitectura (4 capas)

```
Texto
  │
  ▼
① IA ligera (Stanza; si no está instalado, spaCy)  → categoría + raíz
  ▼
② Diccionario morfológico (~490 mil formas)        → corrige raíces verbales
  ▼                                                  (volverían → volver)
③ Autómata finito de reglas (rules.py)             → corrige por contexto
  ▼                                                  (yo + estudio → verbo)
④ Entidades (spaCy; Stanza en árabe)               → une María = María López
  ▼
Resultado  (si el servicio no responde, el backend usa TextAnalyzer.cs)
```

| Idioma | Categorías y raíces | Entidades |
|---|---|---|
| Español | Stanza (es) → respaldo spaCy es_core_news_md | spaCy |
| Inglés | Stanza (en) → respaldo spaCy en_core_web_sm | spaCy |
| Ruso | Stanza (ru) → respaldo spaCy ru_core_news_sm | spaCy |
| Chino | Stanza (zh-hans) → respaldo spaCy zh_core_web_sm | spaCy |
| Árabe | Stanza (ar) | Stanza |

## El autómata de reglas (`rules.py`)

AFD con estados `q0, qSUJ, qDET, qVERB, qVCONJ` que recorre las palabras ya
etiquetadas y corrige:

| Regla | Patrón | Ejemplo |
|---|---|---|
| R1 | pronombre sujeto + palabra con lema verbal → VERBO | *yo **estudio*** |
| R2 | determinante + "verbo" (no infinitivo) → SUSTANTIVO | *el **estudio*** |
| R3 | verbo + conjunción + palabra verbal → VERBO | *corre y **salta*** |
| R4 | -amos/-emos/-imos con lema verbal → VERBO | ***bebimos*** |
| R5 | clase cerrada (ayer, hoy, sí…) → ADVERBIO | ***ayer***, ***sí*** |
| R6 | pronombres sujeto → PRONOMBRE | ***Nosotras*** al inicio |

## Precisión medida (`evaluacion/evaluar.py`)

Con 33 oraciones etiquetadas a mano (25 en español, 8 en inglés), motor spaCy:

| | Sin reglas | Con reglas |
|---|---|---|
| Verbos (español) F1 | 87,6 % | **98,9 %** |
| Sustantivos (español) F1 | 92,4 % | **95,7 %** |
| **Global F1** | 93,6 % | **98,0 %** |

```bash
python evaluacion/evaluar.py            # con reglas
NLP_RULES=0 python evaluacion/evaluar.py   # sin reglas (Linux/Mac)
python -m pytest -q tests               # pruebas unitarias del autómata
```

## Instalar y ejecutar en Windows

1. Python 3.11 instalado.
2. Doble clic en `instalar.bat` (una vez; descarga los modelos).
3. `iniciar-todo.bat` lo levanta en `http://localhost:8000`
   (documentación interactiva en `http://localhost:8000/docs`).

## Variables de entorno

| Variable | Defecto | Uso |
|---|---|---|
| `NLP_POS_ENGINE` | `stanza` | `stanza` o `spacy` para categorías y raíces |
| `NLP_STANZA_PACKAGE` | `default_fast` | `default_fast` (liviano) o `default` (más preciso, más pesado) |
| `NLP_LANGUAGES` | `ES,EN,RU,ZH,AR` | Idiomas habilitados |
| `NLP_MAX_LOADED_MODELS` | `2` | Idiomas cargados en memoria a la vez |
| `NLP_RULES` | `1` | `0` apaga el autómata (solo para comparar) |
| `PORT` | `8000` | Puerto (Railway lo asigna) |

## Despliegue en Railway

Ver `../docs/DESPLIEGUE_RAILWAY.md`.
