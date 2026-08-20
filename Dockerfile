# Multi-stage: SDK to build, a plain runtime (not chiseled, not alpine) to run. Arch.Sql and
# Arch.Cli both set InvariantGlobalization=false (Microsoft.Data.SqlClient requires ICU and
# throws at CONNECTION time, not at build time, under invariant mode — see the comment on
# Arch.Sql.csproj) — an ICU-less base would pass every build and test here and then break
# `arch connect` in production.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Arch.Cli/Arch.Cli.csproj -c Release -o /app \
    --no-self-contained -p:CI=true

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
# git: GitHistory.cs and GitRemote.cs shell out to `git log` / `git remote` for the Evolution,
# Hotspots and source-link features. Without it those pages degrade silently to "unavailable" —
# no error, just missing data — which is a quiet quality regression across the whole estate,
# not something CI would ever flag.
RUN apt-get update \
 && apt-get install -y --no-install-recommends git ca-certificates \
 && rm -rf /var/lib/apt/lists/*
# Arch is read-only by design (CLAUDE.md, Conventions) — run as a non-root user so that is
# enforced by the OS, not only by the code.
RUN useradd --create-home --uid 10001 arch
USER arch
WORKDIR /work
COPY --from=build /app /opt/arch
# The portable CI core, at a fixed path so a CI template can call it without vendoring a copy
# of it into every consumer repo (templates/arch.gitlab-ci.yml runs /opt/arch/arch-ci.sh).
# Shipping it in the image is what keeps the script and the binary it drives on one version.
COPY templates/arch-ci.sh /opt/arch/arch-ci.sh
ENV PATH="/opt/arch:${PATH}"
ENTRYPOINT ["/opt/arch/arch"]
