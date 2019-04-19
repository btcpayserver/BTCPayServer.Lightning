FROM mcr.microsoft.com/dotnet/core/sdk:2.1.505-alpine3.7 AS builder

ENV IN_DOCKER_CONTAINER true
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT false
RUN apk add --no-cache icu-libs
ENV LC_ALL en_US.UTF-8
ENV LANG en_US.UTF-8
COPY . .
RUN ["chmod", "+x", "./tests/docker-entrypoint.sh"]
ENTRYPOINT ["./tests/docker-entrypoint.sh"]
