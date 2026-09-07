targetScope = 'resourceGroup'

@description('Globally unique Function App name (2-60 characters).')
@minLength(2)
@maxLength(60)
param functionAppName string

@description('Globally unique Storage Account name, lowercase letters/digits only.')
@minLength(3)
@maxLength(24)
param storageAccountName string

param location string = resourceGroup().location
param environment string = 'dev'
param planName string = '${functionAppName}-plan'

@allowed(['EP1', 'EP2', 'EP3'])
@description('Elastic Premium supports containerized Functions and RabbitMQ triggers.')
param planSku string = 'EP1'

@minValue(1)
param maximumElasticWorkerCount int = 3

@minLength(1)
@description('Published container image, preferably pinned by digest; no registry credentials in the URI.')
param containerImage string

@secure()
@minLength(1)
@description('AMQP(S) connection or an App Service Key Vault reference. Never store the value in a parameter file.')
param rabbitMqConnection string

@description('Optional existing delegated Microsoft.Web/serverFarms subnet for outbound VNet integration.')
param integrationSubnetResourceId string = ''

@description('Use the system-assigned identity for an existing private ACR. Grant AcrPull separately.')
param useManagedIdentityForAcr bool = false

@secure()
@description('Optional existing Application Insights connection string. Empty disables the pre-existing exporter. No monitoring resources are created.')
param applicationInsightsConnectionString string = ''

var tags = {
  environment: environment
  application: 'TrezzeCloud.Notifications.Functions'
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'elastic'
  sku: {
    name: planSku
    tier: 'ElasticPremium'
    capacity: 1
  }
  properties: {
    reserved: true
    maximumElasticWorkerCount: maximumElasticWorkerCount
  }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux,container'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    virtualNetworkSubnetId: empty(integrationSubnetResourceId) ? null : integrationSubnetResourceId
    siteConfig: {
      linuxFxVersion: 'DOCKER|${containerImage}'
      acrUseManagedIdentityCreds: useManagedIdentityForAcr
      minimumElasticInstanceCount: 1
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: concat([
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${az.environment().suffixes.storage}'
        }
        {
          name: 'RabbitMqConnection'
          value: rabbitMqConnection
        }
        {
          name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE'
          value: 'false'
        }
      ], empty(applicationInsightsConnectionString) ? [] : [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: applicationInsightsConnectionString
        }
      ])
    }
  }
}

output functionAppResourceId string = functionApp.id
output functionAppHostName string = functionApp.properties.defaultHostName
output managedIdentityPrincipalId string = functionApp.identity.principalId
output storageAccountResourceId string = storage.id
