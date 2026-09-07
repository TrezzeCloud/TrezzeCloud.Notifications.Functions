FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY ["src/TrezzeCloud.Contracts/TrezzeCloud.Contracts.csproj", "src/TrezzeCloud.Contracts/"]
COPY ["src/TrezzeCloud.Notifications.Functions/TrezzeCloud.Notifications.Functions.csproj", "src/TrezzeCloud.Notifications.Functions/"]

RUN dotnet restore "src/TrezzeCloud.Notifications.Functions/TrezzeCloud.Notifications.Functions.csproj"

COPY . .

WORKDIR "/src/src/TrezzeCloud.Notifications.Functions"

RUN dotnet publish "TrezzeCloud.Notifications.Functions.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0

ENV AzureWebJobsScriptRoot=/home/site/wwwroot
ENV AzureFunctionsJobHost__Logging__Console__IsEnabled=true

COPY --from=build /app/publish /home/site/wwwroot