using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TrezzeCloud.Notifications.Functions.Serialization;
using TrezzeCloud.Contracts.Events;

namespace TrezzeCloud.Notifications.Functions.Functions;

public class UserCreatedFunction
{
    private readonly ILogger<UserCreatedFunction> _logger;

    public UserCreatedFunction(
        ILogger<UserCreatedFunction> logger)
    {
        _logger = logger;
    }

    [Function("UserCreatedNotification")]
    public Task Run(
        [RabbitMQTrigger(
            "notifications-user-created",
            ConnectionStringSetting = "RabbitMqConnection")]
        string rawMessage)
    {
        _logger.LogInformation(
            "Mensagem recebida da fila notifications-user-created.");

        if (!MassTransitEnvelopeDeserializer.TryDeserialize<UserCreatedEvent>(rawMessage, out var userCreatedEvent))
        {
            _logger.LogWarning(
                "Não foi possível desserializar UserCreatedEvent.");

            return Task.CompletedTask;
        }

        _logger.LogInformation(
            """
            ====================================
            WELCOME EMAIL
            To: {Email}
            User: {Name}
            Welcome to TrezzeCloud!
            ====================================
            """,
            userCreatedEvent.Email,
            userCreatedEvent.Name);

        return Task.CompletedTask;
    }
}
