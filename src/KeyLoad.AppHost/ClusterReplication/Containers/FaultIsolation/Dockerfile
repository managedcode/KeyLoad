ARG KEYLOAD_SERVER_IMAGE
FROM ${KEYLOAD_SERVER_IMAGE} AS fault-runtime
USER 0
RUN apt-get update && apt-get install -y --no-install-recommends iptables coreutils \
    && dpkg-query -W iptables coreutils > /app/keyload-fault-tools.txt \
    && /usr/sbin/iptables --version > /app/keyload-fault-iptables.txt \
    && rm -rf /var/lib/apt/lists/*
ARG KEYLOAD_FAULT_SOURCE
LABEL keyload.fault.source=${KEYLOAD_FAULT_SOURCE}
USER $APP_UID
