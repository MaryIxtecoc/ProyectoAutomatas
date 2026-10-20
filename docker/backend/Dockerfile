# Build context: ./backend (docker-compose.yml lo apunta así)
# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY AutomatasFinal.Domain/*.csproj AutomatasFinal.Domain/
COPY AutomatasFinal.Application/*.csproj AutomatasFinal.Application/
COPY AutomatasFinal.Infrastructure/*.csproj AutomatasFinal.Infrastructure/
COPY AutomatasFinal.API/*.csproj AutomatasFinal.API/

RUN dotnet restore AutomatasFinal.API/AutomatasFinal.API.csproj

COPY . .

RUN dotnet publish AutomatasFinal.API/AutomatasFinal.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Librerías nativas que necesita OpenCvSharp (reconocimiento facial) y que la
# imagen aspnet no trae. Verificado con: ldd libOpenCvSharpExtern.so
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libgomp1 \
        libglib2.0-0 \
        libsm6 \
        libxext6 \
        libxrender1 \
        libgtk-3-0 \
        libfreetype6 \
        libharfbuzz0b \
        libdrm2 \
        libatomic1 \
        fontconfig \
        fonts-dejavu-core \
        fonts-noto-core \
        fonts-noto-cjk \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AutomatasFinal.API.dll"]
