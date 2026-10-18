# Multi-stage build: API (dotnet publish) + Mini App (vite) → two final targets.
#   docker build --target api -t carousel-api .
#   docker build --target web -t carousel-web .
# Or via compose profile: docker compose --profile full up -d --build

# ---------- Stage 1: backend publish ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ ./src/
RUN dotnet publish src/Carousel.Api/Carousel.Api.csproj -c Release -o /out --nologo

# ---------- Stage 2: Mini App build ----------
# Official images via mirror.gcr.io: Docker Hub (docker.io) is unreachable from this
# build network; override with --build-arg if you have direct Hub access.
FROM mirror.gcr.io/library/node:20 AS web-build
WORKDIR /web
ARG VITE_API_BASE_URL=http://localhost:8081
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}
COPY src/frontends/mini-app/package.json src/frontends/mini-app/package-lock.json ./
RUN npm ci
COPY src/frontends/mini-app/ .
RUN npm run build

# ---------- Stage 3a: API runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
# SkiaSharp native lib needs fontconfig on Debian-based runtime images.
RUN apt-get update \
    && apt-get install -y --no-install-recommends fontconfig \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=api-build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Carousel.Api.dll"]

# ---------- Stage 3b: web runtime (nginx: static + /api proxy) ----------
FROM mirror.gcr.io/library/nginx:1.27-alpine AS web
COPY docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=web-build /web/dist /usr/share/nginx/html
EXPOSE 80