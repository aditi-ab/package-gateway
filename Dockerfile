FROM node:24-alpine AS web
WORKDIR /src/PackageGateway
COPY Packages/Aditify/package.json Packages/Aditify/yarn.lock Packages/Aditify/
COPY Packages/Aditify/Ui/package.json Packages/Aditify/Ui/package.json
COPY Packages/Aditify/Identity/package.json Packages/Aditify/Identity/package.json
COPY package.json yarn.lock ./
COPY src/PackageGateway.Admin/package.json src/PackageGateway.Admin/package.json
COPY documentation/package.json documentation/package.json
RUN corepack enable \
    && yarn --cwd Packages/Aditify install --frozen-lockfile \
    && yarn install --frozen-lockfile
COPY Packages/Aditify/Ui Packages/Aditify/Ui
COPY Packages/Aditify/Identity Packages/Aditify/Identity
COPY src/PackageGateway.Admin src/PackageGateway.Admin
COPY documentation documentation
RUN yarn build

FROM mcr.microsoft.com/dotnet/sdk:10.0.301 AS build
WORKDIR /src/PackageGateway
COPY . .
ARG APP_VERSION=1.0.0
ARG APP_ASSEMBLY_VERSION=1.0.0.0
RUN dotnet restore SecurePackageGateway.slnx \
    && dotnet publish src/PackageGateway.Api/PackageGateway.csproj \
        -c Release \
        --no-restore \
        -o /app \
        /p:UseAppHost=false \
        /p:Version=${APP_VERSION} \
        /p:AssemblyVersion=${APP_ASSEMBLY_VERSION} \
        /p:FileVersion=${APP_ASSEMBLY_VERSION} \
        /p:InformationalVersion=${APP_VERSION} \
    && trusted_roots="/usr/share/dotnet/sdk/$(dotnet --version)/trustedroots" \
    && test -s "${trusted_roots}/codesignctl.pem" \
    && test -s "${trusted_roots}/timestampctl.pem" \
    && cp -R "${trusted_roots}" /app/trustedroots

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG APP_VERSION=1.0.0
LABEL org.opencontainers.image.version=${APP_VERSION}
USER root
RUN mkdir -p /data /tmp/packagegateway && chown -R app:app /data /tmp/packagegateway
WORKDIR /app
COPY --from=build --chown=app:app /app .
COPY --from=web --chown=app:app /src/PackageGateway/src/PackageGateway.Admin/dist ./wwwroot/admin
COPY --from=web --chown=app:app /src/PackageGateway/documentation/.vitepress/dist ./wwwroot/docs
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    Database__Provider=Sqlite \
    Database__ConnectionString="Data Source=/data/packagegateway.db" \
    BlobStorage__Path=/data/blobs \
    Authentication__DataProtectionKeysPath=/data/dataprotection-keys \
    TMPDIR=/tmp/packagegateway
EXPOSE 8080
ENTRYPOINT ["dotnet", "PackageGateway.dll"]
