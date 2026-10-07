# =============================================================================
# CustomerClient - Angular-appen byggd och serverad av nginx.
# Samma upplägg som CQRSClient: Node bygger, nginx serverar.
#
#   docker build -t localhost:5000/customerclient:1.0.0 .
# =============================================================================

# ---- Steg 1: bygg Angular ---------------------------------------------------
FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npx ng build --configuration production

# ---- Steg 2: servera med nginx ----------------------------------------------
FROM nginx:stable-alpine AS final
COPY nginx.conf /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/CustomerClient/browser /usr/share/nginx/html

# Standardvärden om containern körs utan Helm (t.ex. docker run lokalt).
ENV API_UPSTREAM=http://telemetryapi.local \
    FARO_UPSTREAM=http://faro.local

EXPOSE 80
