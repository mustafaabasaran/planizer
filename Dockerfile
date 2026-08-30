# syntax=docker/dockerfile:1

# Native AOT build; the final image carries a single native binary on a chiseled base
# (no shell, no package manager, non-root). ICU is needed because the CLI does not opt
# into InvariantGlobalization.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
RUN apt-get update \
    && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY . .
ARG TARGETARCH
RUN case "$TARGETARCH" in \
      amd64) RID=linux-x64 ;; \
      arm64) RID=linux-arm64 ;; \
      *) echo "unsupported architecture: $TARGETARCH" >&2; exit 1 ;; \
    esac \
    && dotnet publish src/Planizer.Cli -c Release -r "$RID" /p:PublishAot=true -o /out

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled-extra
COPY --from=build /out/Planizer.Cli /usr/local/bin/planizer
# Mount the migrations to analyze here: docker run -v "$PWD:/work" ghcr.io/mustafaabasaran/planizer analyze .
WORKDIR /work
ENTRYPOINT ["/usr/local/bin/planizer"]
CMD ["--help"]
