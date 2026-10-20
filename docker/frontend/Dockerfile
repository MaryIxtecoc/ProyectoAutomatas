# Build context: ./frontend (docker-compose.yml lo apunta así)
# syntax=docker/dockerfile:1

FROM node:22-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm install

COPY . .
RUN npm run build

FROM node:22-alpine AS runtime
WORKDIR /app

COPY --from=build /app/.output ./.output

ENV NITRO_PORT=3000
ENV NITRO_HOST=0.0.0.0
EXPOSE 3000

CMD ["node", ".output/server/index.mjs"]
