using './main.bicep'

param functionAppName = 'replace-notifications-dev'
param storageAccountName = 'replacenotificationsdev'
param location = 'brazilsouth'
param environment = 'dev'
param planSku = 'EP1'
param maximumElasticWorkerCount = 3
param containerImage = 'ghcr.io/example/trezzecloud-notifications-functions:replace-me'
// Supplied by the shell/CI secret store; no secret is committed here.
param rabbitMqConnection = readEnvironmentVariable('RABBITMQ_CONNECTION')
param integrationSubnetResourceId = ''
param useManagedIdentityForAcr = false
// Application Insights is optional and disabled by default.
