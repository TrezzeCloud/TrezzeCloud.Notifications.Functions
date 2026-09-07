# TrezzeCloud.Notifications.Functions

Repositório próprio da solução serverless de notificações da Fase 3. Substitui o host legado `TrezzeCloud.NotificationsAPI`. O código usa Azure Functions v4, .NET 10 isolated worker e RabbitMQTrigger. O envio de e-mails é simulado no logger; não há envio SMTP real.

Endereço previsto: [TrezzeCloud/TrezzeCloud.Notifications.Functions](https://github.com/TrezzeCloud/TrezzeCloud.Notifications.Functions). O repositório é inicialmente local; criar/publicar o remoto é uma etapa separada.

## Estrutura

```text
src/
  TrezzeCloud.Notifications.Functions/
    Functions/
    Serialization/
    Properties/
    Program.cs
    host.json
    local.settings.example.json
    TrezzeCloud.Notifications.Functions.csproj
  TrezzeCloud.Contracts/
    Events/
    TrezzeCloud.Contracts.csproj
tests/
  TrezzeCloud.Notifications.UnitTests/
    Fixtures/payment-processed.masstransit.json
    NotificationFunctionTests.cs
    TrezzeCloud.Notifications.UnitTests.csproj
infra/azure/
  main.bicep
  main.parameters.example.bicepparam
Dockerfile
.dockerignore
.gitignore
TrezzeCloud.Notifications.Functions.slnx
```

## Arquitetura serverless

UsersAPI publica `UserCreatedEvent`. CatalogAPI inicia a compra, PaymentsAPI processa o pagamento e publica `PaymentProcessedEvent`. As Functions recebem esses envelopes nas filas de notificações, independentemente das filas usadas pela CatalogAPI para atualizar a biblioteca.

| Function | Fila | Resultado |
|---|---|---|
| `UserCreatedNotification` | `notifications-user-created` | Simula boas-vindas com nome/e-mail |
| `PaymentProcessedNotification` | `notifications-payment-processed` | Simula confirmação somente se `Status == "Approved"` |

Não há endpoint HTTP de negócio. Os triggers usam `ConnectionStringSetting = "RabbitMqConnection"`. Configure os exchanges fanout `TrezzeCloud.Contracts.Events:UserCreatedEvent` e `TrezzeCloud.Contracts.Events:PaymentProcessedEvent`, vinculados às filas respectivas no vhost configurado. A topologia local está em `TrezzeCloud.Orchestration/k8s/rabbitmq/definitions.json`; RabbitMQTrigger não cria os bindings dos publishers MassTransit.

### Envelope MassTransit

```json
{
  "messageType": ["urn:message:TrezzeCloud.Contracts.Events:PaymentProcessedEvent"],
  "message": {
    "orderId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    "userId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
    "gameId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
    "price": "10.00",
    "status": "Approved",
    "processedAt": "2026-09-05T16:47:27.5395534Z"
  }
}
```

O desserializador extrai `message`, ignora metadados e aceita propriedades camelCase/PascalCase. `JsonNumberHandling.AllowReadingFromString` permite `price: 10.00` e `price: "10.00"`, sem conversões manuais ou dependência da cultura. O evento de usuário contém `userId`, `name`, `email` e `createdAt`.

JSON inválido, envelope ausente e campos obrigatórios inválidos resultam em aviso e retorno normal, sem notificação. Isso consome a mensagem inválida; não há fila de erro/retry próprio para esses casos. O contrato é escolhido pela fila/Function, não por `messageType`. O payload bruto não é registrado.

## Configuração

| Configuração | Uso |
|---|---|
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `AzureWebJobsStorage` | Storage usado pelo host; Azurite no desenvolvimento ou Storage real no Azure |
| `RabbitMqConnection` | URI AMQP(S) com credenciais, endpoint e vhost corretos |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Opcional; omitida por padrão |

`local.settings.example.json` contém apenas exemplos. Copie para `local.settings.json` e preencha os valores locais. O arquivo real, arquivos `.env`, parâmetros locais e artefatos de validação são ignorados pelo Git e excluídos do contexto Docker.

## Execução local com Core Tools

Requer SDK .NET 10, Azure Functions Core Tools v4 compatível com esse worker, RabbitMQ com filas/bindings e Azurite ou Azure Storage acessível.

Na raiz do repositório:

```powershell
dotnet restore TrezzeCloud.Notifications.Functions.slnx
dotnet build TrezzeCloud.Notifications.Functions.slnx --no-restore
dotnet test TrezzeCloud.Notifications.Functions.slnx --no-build --no-restore
Copy-Item src/TrezzeCloud.Notifications.Functions/local.settings.example.json src/TrezzeCloud.Notifications.Functions/local.settings.json
# Preencha o arquivo local e inicie RabbitMQ/Azurite antes do host.
Set-Location src/TrezzeCloud.Notifications.Functions
func start
```

O host deve descobrir `UserCreatedNotification` e `PaymentProcessedNotification`. `dotnet run` sozinho não substitui o host do Core Tools para ativar os triggers. Configure o vhost e o usuário do RabbitMQ antes de publicar eventos.

## Docker

```powershell
docker build -t trezzecloud-notifications-functions:local .
```

O Dockerfile usa a imagem oficial Azure Functions .NET 10 isolated e copia apenas os artefatos publicados. Para executar, exporte as variáveis `RabbitMqConnection` e `AzureWebJobsStorage` no shell a partir de configuração local segura; seus endpoints precisam ser acessíveis do container:

```powershell
docker run --rm --name notifications-functions --network <rede-local> `
  -e FUNCTIONS_WORKER_RUNTIME=dotnet-isolated `
  -e RabbitMqConnection -e AzureWebJobsStorage `
  trezzecloud-notifications-functions:local
```

Nenhuma porta HTTP é necessária para os triggers. `localhost` dentro do container aponta para ele próprio: use nomes da rede Docker ou `host.docker.internal` quando adequado. `UseDevelopmentStorage=true` presume Azurite em loopback; para Azurite em outro container use os endpoints explícitos na configuração local. Não embuta connection strings no Dockerfile.

A orquestração completa existente em `../TrezzeCloud.Orchestration` usa este repositório como contexto de build. Não rode simultaneamente outro host de notificações nas mesmas filas, pois eles disputam mensagens.

## Testes

A solução contém 33 casos unitários: os dois eventos, payloads inválidos, campos obrigatórios, preços em número/string, culturas pt-BR/en-US e status Approved/não aprovado. O fixture em `tests/.../Fixtures` reproduz o formato real publicado pelo MassTransit. Ele é copiado para a saída de testes, sem dependência de outro repositório.

Os testes usam logger em memória e não precisam de RabbitMQ/Azure. A descoberta e a execução dos triggers precisam de teste separado com o host, RabbitMQ e Storage. Execução local em container valida a integração funcional, mas não reproduz escala gerenciada, cold starts, identidade Azure, RBAC, VNet, disponibilidade ou cobrança do serviço. Consumo único observado em um teste não é garantia de entrega exatamente uma vez; não há armazenamento de idempotência implementado.

## Implantação com Bicep

`infra/azure/main.bicep` cria no Resource Group escolhido:

- Storage Account StorageV2/Standard_LRS, HTTPS/TLS 1.2 e sem blobs públicos;
- plano Linux Elastic Premium (EP1, EP2 ou EP3), com limite parametrizado de workers;
- Function App Linux com imagem de container parametrizada e identidade gerenciada SystemAssigned;
- app settings do worker, Storage e RabbitMQ;
- integração opcional com subnet existente;
- parâmetro seguro opcional para Application Insights existente, desativado por padrão. Nenhum recurso ou stack de observabilidade é criado.

O plano Premium foi escolhido por compatibilidade com RabbitMQTrigger. Veja [RabbitMQ bindings](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-rabbitmq). A configuração de container segue a [documentação de infraestrutura de Functions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-infrastructure-as-code?tabs=vs-code).

### Parâmetros

| Parâmetro | Descrição |
|---|---|
| `functionAppName`, `storageAccountName`, `planName` | Nomes; Function/Storage devem ser globalmente únicos |
| `location`, `environment` | Região e tag de ambiente |
| `planSku`, `maximumElasticWorkerCount` | Plano/capacidade |
| `containerImage` | Imagem publicada, preferencialmente fixada por digest |
| `rabbitMqConnection` | Parâmetro `@secure()` obrigatório, fornecido pelo ambiente/secret store |
| `integrationSubnetResourceId` | Subnet delegada existente para saída privada, opcional |
| `useManagedIdentityForAcr` | Habilita pull do ACR com identidade da Function; requer AcrPull |
| `applicationInsightsConnectionString` | Parâmetro `@secure()` opcional; vazio desabilita o exporter existente |

Pré-requisitos: assinatura Azure, Azure CLI/Bicep atuais, Resource Group, quota/região compatíveis e imagem já publicada em registry acessível. O Bicep não compila nem publica a imagem e não provisiona RabbitMQ, rede, registry ou Key Vault.

```powershell
# Validação local do template, sem implantação:
az bicep build --file infra/azure/main.bicep
Copy-Item infra/azure/main.parameters.example.bicepparam infra/azure/main.local.bicepparam
# Ajuste nomes, região, imagem e rede no arquivo local.
# Forneça RABBITMQ_CONNECTION pelo secret store do CI ou sessão segura.
# Exemplo de entrada oculta em PowerShell:
$secret = Read-Host 'RabbitMQ connection ou referência Key Vault' -AsSecureString
$env:RABBITMQ_CONNECTION = [System.Net.NetworkCredential]::new('', $secret).Password
az login
az account set --subscription <subscription-id>
az group create --name <resource-group> --location <regiao>
az deployment group validate --resource-group <resource-group> --parameters infra/azure/main.local.bicepparam
az deployment group what-if --resource-group <resource-group> --parameters infra/azure/main.local.bicepparam
# Execute somente quando o resultado e o custo estiverem aprovados:
az deployment group create --resource-group <resource-group> --parameters infra/azure/main.local.bicepparam
Remove-Item Env:RABBITMQ_CONNECTION
```

O exemplo `.bicepparam` lê `RABBITMQ_CONNECTION` em tempo de execução, sem valor sensível versionado. Não salve nem publique o JSON compilado de parâmetros quando usar segredos reais. Referência: [implantação Bicep por CLI](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/deploy-cli).

### Segredos, identidade e rede

`AzureWebJobsStorage` é montada no deployment com a chave gerada do Storage (`listKeys`); não há chave literal ou saída de segredo no template. O acesso ao Storage ainda usa essa chave, não a identidade gerenciada. Restrinja o acesso às configurações da Function e planeje rotação; regenerar a chave exige atualizar a app setting. O Storage do exemplo não tem private endpoint: endurecimento de rede exige configuração adicional.

`rabbitMqConnection` pode receber uma URI secreta ou uma referência App Service Key Vault (`@Microsoft.KeyVault(...)`). Para Key Vault, conceda à identidade da Function permissão de leitura do segredo e acesso de rede. Para ACR privado, habilite `useManagedIdentityForAcr` e conceda AcrPull à identidade retornada em `managedIdentityPrincipalId`; sem essa permissão o container não inicia. A atribuição e os recursos externos não são criados por este template.

A Function precisa alcançar RabbitMQ em AMQP/AMQPS, com DNS, TLS, vhost e permissões de consumo corretos. Para um broker privado em Kubernetes, use integração VNet e endpoint privado roteável, com subnet delegada a Microsoft.Web/serverFarms. O ClusterIP e o nome `rabbitmq` do cluster/Compose não são diretamente acessíveis do Azure. Configure conectividade entre redes ou VPN quando o broker estiver local; não publique a interface de management na internet. A subnet opcional não cria rotas, DNS, VPN ou o endpoint do broker.

## Migração e limites

O projeto e namespace agora são `TrezzeCloud.Notifications.Functions`, sem underscores. A NotificationsAPI antiga permanece como legado e não deve ser implantada. Os contratos foram copiados para tornar este repositório independente; as cópias usadas pelo legado continuam na origem.

As dependências e a configuração condicional de telemetria existentes foram preservadas durante a extração. Application Insights permanece opcional; esta migração não implementa observabilidade. Validar/implantar no Azure, publicar a imagem e criar o repositório remoto exigem ações externas autorizadas separadamente.
