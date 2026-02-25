FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://*:8080

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS copy 
WORKDIR /app
COPY [ "Directory.Build.props", "Directory.Packages.props", "./"]
COPY [ "nuget.config", "./"]
COPY [ "Share.Common.Workflow.sln", "./"]
ADD ["src", "./src"]

FROM copy AS build
WORKDIR /app

ARG GITLAB_PACKAGE_REGISTRY_USERNAME
ARG GITLAB_PACKAGE_REGISTRY_PASSWORD
ARG RUNNER_HTTP_PROXY
ARG RUNNER_HTTPS_PROXY
ARG RUNNER_NO_PROXY

ENV GITLAB_PACKAGE_REGISTRY_USERNAME=$GITLAB_PACKAGE_REGISTRY_USERNAME
ENV GITLAB_PACKAGE_REGISTRY_PASSWORD=$GITLAB_PACKAGE_REGISTRY_PASSWORD
ENV MSBuildEnableWorkloadResolver=false
ENV HTTP_PROXY=$RUNNER_HTTP_PROXY
ENV HTTPS_PROXY=$RUNNER_HTTPS_PROXY
ENV NO_PROXY=$RUNNER_NO_PROXY

RUN dotnet nuget disable source local
RUN dotnet nuget update source gitlab --username "$GITLAB_PACKAGE_REGISTRY_USERNAME" --password "$GITLAB_PACKAGE_REGISTRY_PASSWORD" --store-password-in-clear-text
RUN dotnet restore "src/EFCoreMigrations/Share.Common.Workflow.EFCoreMigrations.Dev/Share.Common.Workflow.EFCoreMigrations.Dev.csproj"
RUN dotnet build "src/EFCoreMigrations/Share.Common.Workflow.EFCoreMigrations.Dev/Share.Common.Workflow.EFCoreMigrations.Dev.csproj" -r linux-x64 -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "src/EFCoreMigrations/Share.Common.Workflow.EFCoreMigrations.Dev/Share.Common.Workflow.EFCoreMigrations.Dev.csproj" -r linux-x64 --no-self-contained -c Release -o /app/publish /p:UseAppHost=false /p:PublishReadyToRun=true

FROM base AS final
RUN apk add --no-cache tzdata
ENV TZ=Asia/Singapore
RUN cp /usr/share/zoneinfo/$TZ /etc/localtime \
    && echo $TZ > /etc/timezone

COPY --from=publish /app/publish .
RUN addgroup -g 2000 hsa \
    && adduser \
    -u 1000 \
    -D \
    -H \
    -G hsa \
    hsa

RUN chown hsa:hsa /app
USER hsa:hsa

ENTRYPOINT ["dotnet", "Share.Common.Workflow.EFCoreMigrations.Dev.dll"]
