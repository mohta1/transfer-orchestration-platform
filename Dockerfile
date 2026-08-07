FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY ["Directory.Build.props", "."]

COPY ["src/TransferOrchestration.Api/TransferOrchestration.Api.csproj", "src/TransferOrchestration.Api/"]

COPY ["src/BuildingBlocks/TransferOrchestration.BuildingBlocks/TransferOrchestration.BuildingBlocks.csproj", "src/BuildingBlocks/TransferOrchestration.BuildingBlocks/"]

COPY ["src/Modules/TransferManagement/TransferOrchestration.TransferManagement.csproj", "src/Modules/TransferManagement/"]
COPY ["src/Modules/AccountBalance/TransferOrchestration.AccountBalance.csproj", "src/Modules/AccountBalance/"]
COPY ["src/Modules/PaymentNetwork/TransferOrchestration.PaymentNetwork.csproj", "src/Modules/PaymentNetwork/"]
COPY ["src/Modules/Reconciliation/TransferOrchestration.Reconciliation.csproj", "src/Modules/Reconciliation/"]
COPY ["src/Modules/Notification/TransferOrchestration.Notification.csproj", "src/Modules/Notification/"]
COPY ["src/Modules/AuditOperations/TransferOrchestration.AuditOperations.csproj", "src/Modules/AuditOperations/"]

RUN dotnet restore "src/TransferOrchestration.Api/TransferOrchestration.Api.csproj"

COPY src/ src/

WORKDIR "/src/src/TransferOrchestration.Api"

RUN dotnet publish \
    "TransferOrchestration.Api.csproj" \
    -c Release \
    --no-restore \
    -o /app/publish \
    /p:UseAppHost=false


FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "TransferOrchestration.Api.dll"]
