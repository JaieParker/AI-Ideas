# Plan-13 / BR-SKILL-008 amended — Docker is an accepted non-.NET
# dependency only when the project is in Sidecar:Mode = "container".
# This image bundles BOTH the helpers sidecar and the Go OTel
# collector binary so the container can serve the full /demo otel
# experience end-to-end (sidecar inside a container can't spawn host
# processes).
#
# Per BR-HELPERS-002 (amended): inside the container the sidecar
# binds 0.0.0.0:5050 because port mapping requires it. The host's
# loopback contract (http://127.0.0.1:5050) is preserved by
# `docker run -p 127.0.0.1:5050:5050 ...`.

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the .NET project file first to maximise restore cache hits.
COPY src/HelpersSidecar/*.csproj ./HelpersSidecar/
RUN dotnet restore ./HelpersSidecar/HelpersSidecar.csproj

# Copy the rest of the .NET sources and the Go collector binary
# that the sidecar will spawn at runtime.
COPY src/HelpersSidecar/ ./HelpersSidecar/
COPY config.yaml /app/config.yaml
COPY dist/linux-amd64/claude-otel-collector /app/dist/linux-amd64/claude-otel-collector

RUN dotnet publish ./HelpersSidecar/HelpersSidecar.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Bind 0.0.0.0 inside the container — port mapping requires it
# (BR-HELPERS-002 amended). Host loopback contract preserved via
# `docker run -p 127.0.0.1:5050:5050`.
ENV ASPNETCORE_URLS=http://0.0.0.0:5050
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish /app
COPY --from=build /app/config.yaml /app/config.yaml
COPY --from=build /app/dist/linux-amd64/claude-otel-collector /app/dist/linux-amd64/claude-otel-collector
RUN chmod +x /app/dist/linux-amd64/claude-otel-collector

# `persistent-enrichments.json` is mounted by the host at run time
# (`-v $(pwd)/persistent-enrichments.json:/app/persistent-enrichments.json`)
# so BR-ENRICH-009's "file is the source of truth" invariant
# survives container restart.

EXPOSE 5050
ENTRYPOINT ["dotnet", "/app/HelpersSidecar.dll"]
