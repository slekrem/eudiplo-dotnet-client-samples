# Deploy-only Dockerfile for running this sample's backend on a server (not needed for
# local development — dotnet run is simpler there). Frontend must already be built into
# wwwroot/ before building this image (npm run build in ../Frontend).
#
# Build context MUST be the repo root, not this directory — Directory.Build.props/
# Directory.Packages.props/NuGet.Config live there and are auto-discovered by MSBuild
# walking up from the project file (ImplicitUsings, Nullable, central package versions,
# the nuget.org-only package source); without them in the build context, the restore
# either fails to resolve Eudiplo.Client's version or basic BCL types fail to resolve:
#   docker build -f src/Eudiplo.Client.Sample.AccessControl/Backend/Dockerfile -t gate-backend .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props NuGet.Config ./
COPY src/Eudiplo.Client.Sample.AccessControl/Backend/ Eudiplo.Client.Sample.AccessControl/Backend/
WORKDIR /src/Eudiplo.Client.Sample.AccessControl/Backend
RUN dotnet publish -c Release -o /app --self-contained false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Eudiplo.Client.Sample.AccessControl.Backend.dll"]
