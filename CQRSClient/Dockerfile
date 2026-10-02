# =============================================================================
# CQRSClient - Angular-appen byggd och serverad av nginx.
#
# Steg 1 (build): Node bygger Angular till statiska filer (HTML/JS/CSS).
# Steg 2 (final): bara de statiska filerna + nginx hamnar i imagen.
#                 Ingen Node, inga node_modules - liten och snabb image.
#
#   docker build -t localhost:5000/cqrsclient:1.0.0 .
# =============================================================================

# ---- Steg 1: bygg Angular ---------------------------------------------------
# Angular 22 kräver Node ^22.22.3, ^24.15.0 eller >=26.
FROM node:24-alpine AS build
WORKDIR /app

# Kopiera bara paketfilerna först: så länge de inte ändras återanvänder Docker
# lagret med installerade paket och bygget går mycket snabbare.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npx ng build --configuration production

# ---- Steg 2: servera med nginx ----------------------------------------------
FROM nginx:stable-alpine AS final

# Som MALL: nginx-imagen kör envsubst på /etc/nginx/templates/*.template vid
# start och skriver resultatet till /etc/nginx/conf.d/. Där fylls
# ${API_UPSTREAM} och ${API_KEY} i från miljövariablerna som Helm sätter.
COPY nginx.conf /etc/nginx/templates/default.conf.template

# Angulars application-builder lägger webbfilerna i dist/<projektnamn>/browser
COPY --from=build /app/dist/CQRSClient/browser /usr/share/nginx/html

# Standardvärden om containern körs utan Helm (t.ex. docker run lokalt).
ENV API_UPSTREAM=http://cqrsapi.local \
    API_KEY=

EXPOSE 80
