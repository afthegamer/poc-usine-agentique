FROM mcr.microsoft.com/playwright:v1.63.0-noble

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NPM_CONFIG_UPDATE_NOTIFIER=false \
    NPM_CONFIG_FUND=false \
    NPM_CONFIG_AUDIT=false \
    NPM_CONFIG_PREFIX=/home/usine/.npm-global \
    PIP_BREAK_SYSTEM_PACKAGES=1 \
    PATH=/home/usine/.npm-global/bin:/home/usine/.dotnet/tools:/usr/share/dotnet:$PATH

RUN apt-get update \
 && apt-get install -y --no-install-recommends git curl ca-certificates python3 python3-venv python3-pip procps zip unzip sqlite3 \
 && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
 && rm /tmp/dotnet-install.sh \
 && dotnet --version

COPY runner/ /opt/acceptance-runner/
RUN cd /opt/acceptance-runner && npm install --no-audit --no-fund && npx playwright --version \
 && node echantillons.mjs /opt/echantillons

RUN useradd --create-home --uid 1500 --shell /bin/bash usine \
 && mkdir -p /workspace /acceptance \
 && chown usine:usine /workspace /acceptance

USER usine
RUN git config --global user.name "Usine agentique" \
 && git config --global user.email "usine@local" \
 && git config --global init.defaultBranch main \
 && git config --global --add safe.directory '*' \
 && mkdir -p /home/usine/.npm-global

WORKDIR /workspace
CMD ["sleep", "infinity"]
