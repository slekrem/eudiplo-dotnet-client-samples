# Deploy-only Dockerfile for running this sample's backend on a server (not needed for
# local development — dotnet run is simpler there). Frontend must already be built into
# wwwroot/ before building this image (npm run build in ../Frontend).
#
# Build context MUST be the repo root, not this directory — the backend project references
# ../../../src/Eudiplo.Client, which needs to be inside the build context:
#   docker build -f samples/Eudiplo.Client.Sample.AccessControl/Backend/Dockerfile -t gate-backend .
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
# Directory.Build.props/Directory.Packages.props/NuGet.Config live at the repo root and are
# auto-discovered by MSBuild walking up from the project file (ImplicitUsings, Nullable,
# central package versions) — without them in the build context, basic BCL types fail to
# resolve.
COPY Directory.Build.props Directory.Packages.props NuGet.Config ./
COPY src/Eudiplo.Client/ src/Eudiplo.Client/
COPY samples/Eudiplo.Client.Sample.AccessControl/Backend/ samples/Eudiplo.Client.Sample.AccessControl/Backend/
WORKDIR /src/samples/Eudiplo.Client.Sample.AccessControl/Backend
RUN dotnet publish -c Release -o /app --self-contained false

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Eudiplo.Client.Sample.AccessControl.Backend.dll"]
