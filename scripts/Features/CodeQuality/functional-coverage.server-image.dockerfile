FROM {{SOURCE_BOUND_ASPNET_IMAGE}}
WORKDIR /app
COPY server/ ./
COPY tool/ /opt/keyload-coverage/tool/
COPY license/ /opt/keyload-coverage/license/
COPY identity/ /opt/keyload-coverage/identity/
COPY settings.xml /opt/keyload-coverage/settings.xml
COPY server-wrapper.sh /usr/local/bin/keyload-coverage-server
COPY server-lifecycle.sh /usr/local/lib/keyload-coverage-server-lifecycle.sh
COPY server-target.sh /usr/local/bin/keyload-coverage-server-target
ENV ASPNETCORE_HTTP_PORTS=8080 \
    KeyLoad__DataDirectory=/data \
    DOTNET_COVERAGE_TELEMETRY_OPTOUT=1 \
    DOTNET_COVERAGE_NOLOGO=1
RUN mkdir -p /data /coverage && \
    chown "$APP_UID:$APP_UID" /data /coverage && \
    chmod 700 /data /coverage
USER $APP_UID
EXPOSE 8080 11111
ENTRYPOINT ["/usr/local/bin/keyload-coverage-server"]
