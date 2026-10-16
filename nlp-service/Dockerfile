# Servicio NLP (Stanza + spaCy + autómata de reglas) para AutomatasFinal.
# Pensado para Railway: los modelos quedan DENTRO de la imagen para que el
# servicio no los descargue cada vez que arranca.
FROM python:3.11-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    STANZA_RESOURCES_DIR=/models/stanza \
    NLP_MAX_LOADED_MODELS=2 \
    PORT=8000

WORKDIR /app

# PyTorch solo-CPU (mucho más liviano que la versión con GPU); lo usa Stanza.
RUN pip install --no-cache-dir torch --index-url https://download.pytorch.org/whl/cpu

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY descargar_modelos.py .
RUN python descargar_modelos.py

COPY app.py engines.py rules.py ./
COPY tests ./tests
#COPY evaluacion ./evaluacion

# Las pruebas del autómata se ejecutan al construir: si fallan, no se publica.
RUN python -m pytest -q tests

EXPOSE 8000
# "::" escucha en IPv4 e IPv6 (la red privada de Railway usa IPv6).
CMD ["sh", "-c", "uvicorn app:app --host 0.0.0.0 --port ${PORT}"]
