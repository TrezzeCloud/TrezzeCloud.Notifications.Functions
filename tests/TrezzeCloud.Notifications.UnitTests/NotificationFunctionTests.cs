using System.Text.Json;
using System.Globalization;
using Microsoft.Extensions.Logging;
using TrezzeCloud.Contracts.Events;
using TrezzeCloud.Notifications.Functions.Functions;
using TrezzeCloud.Notifications.Functions.Serialization;

namespace TrezzeCloud.Notifications.UnitTests;

public sealed class NotificationFunctionTests
{
    [Theory]
    [InlineData(true, "pt-BR")]
    [InlineData(false, "pt-BR")]
    [InlineData(true, "en-US")]
    [InlineData(false, "en-US")]
    public async Task Real_Payment_Fixture_Accepts_Number_Or_String_Regardless_Of_Culture(bool stringPrice, string culture)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var payload = await PaymentFixture();
            if (!stringPrice)
                payload = payload.Replace("\"price\": \"10.00\"", "\"price\": 10.00");

            Assert.True(MassTransitEnvelopeDeserializer.TryDeserialize<PaymentProcessedEvent>(payload, out var payment));
            Assert.Equal(10.00m, payment.Price);
            Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), payment.OrderId);
            var logger = new RecordingLogger<PaymentProcessedFunction>();

            await new PaymentProcessedFunction(logger).Run(payload);

            var confirmation = Assert.Single(logger.Entries, x => x.Message.Contains("PURCHASE CONFIRMATION EMAIL"));
            Assert.Equal(10.00m, confirmation.Properties["Price"]);
            Assert.DoesNotContain(logger.Entries, x => x.Level >= LogLevel.Warning);
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("Pending")]
    [InlineData("Failed")]
    public async Task Real_Payment_Fixture_With_String_Price_Does_Not_Confirm_Non_Approved_Payment(string status)
    {
        var payload = (await PaymentFixture()).Replace("\"Approved\"", $"\"{status}\"");
        var logger = new RecordingLogger<PaymentProcessedFunction>();

        await new PaymentProcessedFunction(logger).Run(payload);

        Assert.DoesNotContain(logger.Entries, x => x.Message.Contains("PURCHASE CONFIRMATION EMAIL"));
        Assert.DoesNotContain(logger.Entries, x => x.Level >= LogLevel.Warning);
    }

    [Theory]
    [InlineData("\"invalid\"")]
    [InlineData("\"10,00\"")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task Real_Payment_Fixture_With_Invalid_Price_Is_Still_Rejected(string price)
    {
        var payload = (await PaymentFixture()).Replace("\"price\": \"10.00\"", $"\"price\": {price}");
        await AssertRejectedByBoth(payload);
    }

    private static Task<string> PaymentFixture() => File.ReadAllTextAsync(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "payment-processed.masstransit.json"));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task User_Envelope_Preserves_Fields_And_Logs_Welcome(bool camelCase)
    {
        var user = new UserCreatedEvent(Guid.NewGuid(), "Test User", "user@example.com", DateTime.UtcNow);
        var payload = Envelope(user, camelCase);
        Assert.True(MassTransitEnvelopeDeserializer.TryDeserialize<UserCreatedEvent>(payload, out var parsed));
        Assert.Equal(user, parsed);
        var logger = new RecordingLogger<UserCreatedFunction>();

        await new UserCreatedFunction(logger).Run(payload);

        var notification = Assert.Single(logger.Entries, x => x.Message.Contains("WELCOME EMAIL"));
        Assert.Contains(user.Email, notification.Message);
        Assert.Contains(user.Name, notification.Message);
        Assert.DoesNotContain(logger.Entries, x => x.Level >= LogLevel.Warning);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Approved_Payment_Envelope_Preserves_Fields_And_Logs_Confirmation(bool camelCase)
    {
        var payment = Payment("Approved");
        var payload = Envelope(payment, camelCase);
        Assert.True(MassTransitEnvelopeDeserializer.TryDeserialize<PaymentProcessedEvent>(payload, out var parsed));
        Assert.Equal(payment, parsed);
        var logger = new RecordingLogger<PaymentProcessedFunction>();

        await new PaymentProcessedFunction(logger).Run(payload);

        var notification = Assert.Single(logger.Entries, x => x.Message.Contains("PURCHASE CONFIRMATION EMAIL"));
        Assert.Contains(payment.UserId.ToString(), notification.Message);
        Assert.Contains(payment.GameId.ToString(), notification.Message);
        Assert.Equal(payment.Price, notification.Properties["Price"]);
        Assert.DoesNotContain(logger.Entries, x => x.Level >= LogLevel.Warning);
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("Pending")]
    [InlineData("Failed")]
    public async Task Non_Approved_Payment_Does_Not_Notify(string status)
    {
        var logger = new RecordingLogger<PaymentProcessedFunction>();
        await new PaymentProcessedFunction(logger).Run(Envelope(Payment(status)));
        Assert.DoesNotContain(logger.Entries, x => x.Message.Contains("PURCHASE CONFIRMATION EMAIL"));
        Assert.Contains(logger.Entries, x => x.Message.Contains("Pagamento não aprovado"));
        Assert.DoesNotContain(logger.Entries, x => x.Level >= LogLevel.Warning);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("{\"message\":null}")]
    [InlineData("{\"message\":[]}")]
    [InlineData("{\"message\":\"text\"}")]
    [InlineData("{\"message\":{}}")]
    [InlineData("{\"message\":{\"userId\":\"invalid-guid\"}}")]
    public async Task Invalid_Payload_Does_Not_Throw_Or_Notify(string? payload)
    {
        await AssertRejectedByBoth(payload);
    }

    [Fact]
    public async Task Invalid_Event_Fields_And_Unwrapped_Events_Are_Rejected()
    {
        var user = new UserCreatedEvent(Guid.NewGuid(), "Test", "user@example.com", DateTime.UtcNow);
        var userPayload = Envelope(user);
        var paymentPayload = Envelope(Payment("Approved"));
        await AssertRejectedByBoth(JsonSerializer.Serialize(user));
        await AssertRejectedByBoth(JsonSerializer.Serialize(Payment("Approved")));
        await AssertRejectedByBoth(userPayload.Replace("\"name\":\"Test\"", "\"name\":null"));
        await AssertRejectedByBoth(userPayload.Replace("\"name\":\"Test\"", "\"name\":123"));
        await AssertRejectedByBoth(paymentPayload.Replace("\"status\":\"Approved\"", "\"status\":null"));
        await AssertRejectedByBoth(paymentPayload.Replace("\"price\":99.9", "\"price\":\"invalid\""));
    }

    private static async Task AssertRejectedByBoth(string? payload)
    {
        var userLogger = new RecordingLogger<UserCreatedFunction>();
        var paymentLogger = new RecordingLogger<PaymentProcessedFunction>();
        Assert.False(MassTransitEnvelopeDeserializer.TryDeserialize<UserCreatedEvent>(payload, out var user));
        Assert.Null(user);
        Assert.False(MassTransitEnvelopeDeserializer.TryDeserialize<PaymentProcessedEvent>(payload, out var payment));
        Assert.Null(payment);
        await new UserCreatedFunction(userLogger).Run(payload!);
        await new PaymentProcessedFunction(paymentLogger).Run(payload!);
        Assert.Single(userLogger.Entries, x => x.Level == LogLevel.Warning);
        Assert.Single(paymentLogger.Entries, x => x.Level == LogLevel.Warning);
        Assert.DoesNotContain(userLogger.Entries, x => x.Message.Contains("WELCOME EMAIL"));
        Assert.DoesNotContain(paymentLogger.Entries, x => x.Message.Contains("PURCHASE CONFIRMATION EMAIL"));
    }

    private static PaymentProcessedEvent Payment(string status) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 99.9m, status, DateTime.UtcNow);

    private static string Envelope<T>(T message, bool camelCase = true) => JsonSerializer.Serialize(new
    {
        messageId = Guid.NewGuid(),
        messageType = new[] { $"urn:message:TrezzeCloud.Contracts.Events:{typeof(T).Name}" },
        message,
        sentTime = DateTime.UtcNow,
        headers = new { }
    }, new JsonSerializerOptions { PropertyNamingPolicy = camelCase ? JsonNamingPolicy.CamelCase : null });

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Dictionary<string, object?> Properties)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception),
                ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(x => x.Key, x => x.Value)));
    }
}
