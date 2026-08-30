# Image autonome : compile SMBeagle depuis les sources puis l'installe dans
# une image runtime-deps minimale (aucun binaire pré-construit requis).
#   docker build -t smbeagle .
#   docker run --rm -v "$PWD:/data" smbeagle --local-path /data -c /data/scan.csv
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG TARGETARCH=amd64
WORKDIR /src
COPY SMBeagle.csproj .
RUN case "$TARGETARCH" in arm64) echo linux-arm64 ;; *) echo linux-x64 ;; esac > /rid \
    && dotnet restore SMBeagle.csproj -r "$(cat /rid)"
COPY . .
RUN dotnet publish SMBeagle.csproj -c Release --self-contained -r "$(cat /rid)" \
    -o /out -p:PublishSingleFile=true -p:PublishTrimmed=false -p:InvariantGlobalization=true \
    -p:DebugType=None -p:DebugSymbols=false

FROM mcr.microsoft.com/dotnet/runtime-deps:9.0
COPY --from=build /out/SMBeagle /bin/smbeagle
ENTRYPOINT ["smbeagle"]
