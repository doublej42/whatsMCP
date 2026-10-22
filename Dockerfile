# ---- Build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY WhatsBuildingMcp.sln Directory.Build.props .editorconfig ./
COPY src/WhatsBuildingMcp/WhatsBuildingMcp.csproj src/WhatsBuildingMcp/
COPY tests/WhatsBuildingMcp.Tests/WhatsBuildingMcp.Tests.csproj tests/WhatsBuildingMcp.Tests/
RUN dotnet restore WhatsBuildingMcp.sln
COPY src/WhatsBuildingMcp/ src/WhatsBuildingMcp/
RUN dotnet publish src/WhatsBuildingMcp/WhatsBuildingMcp.csproj -c Release -o /app/publish --no-restore

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
COPY --from=build /app/publish .
USER app
ENTRYPOINT ["dotnet", "WhatsBuildingMcp.dll"]
