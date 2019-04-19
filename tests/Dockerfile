FROM mcr.microsoft.com/dotnet/core/sdk:2.1.505-alpine3.7 AS builder
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT false
RUN apk add --no-cache icu-libs
ENV LC_ALL en_US.UTF-8
ENV LANG en_US.UTF-8
COPY . .
ENTRYPOINT ["dotnet", "test"]
