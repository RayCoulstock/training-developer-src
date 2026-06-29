using System.Collections.Concurrent;
using Confluent.Kafka;

var builder = WebApplication.CreateBuilder(args);

var bootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS") ?? "kafka:9092";
var topic = Environment.GetEnvironmentVariable("KAFKA_TOPIC") ?? "dotnet-messages";
var groupId = Environment.GetEnvironmentVariable("KAFKA_GROUP_ID") ?? $"dotnet-webserver-{Environment.MachineName}";

builder.Services.AddSingleton(new KafkaSettings(bootstrapServers, topic, groupId));
builder.Services.AddSingleton<MessageStore>();
builder.Services.AddSingleton<IProducer<string, string>>(_ =>
{
    var config = new ProducerConfig
    {
        BootstrapServers = bootstrapServers,
        ClientId = "dotnet-webserver",
    };

    return new ProducerBuilder<string, string>(config).Build();
});
builder.Services.AddHostedService<KafkaConsumerService>();

var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/produce"));

app.MapGet("/produce", (KafkaSettings settings) => Results.Content(Html.Page("Send Messages", $"""
    <p class="lead">Send a string message to Kafka topic <code>{settings.Topic}</code>.</p>
    <form id="produce-form" class="card p-3">
      <label class="form-label" for="key">Message key</label>
      <input class="form-control" id="key" name="key" value="student-1" />
      <label class="form-label mt-3" for="value">Message value</label>
      <textarea class="form-control" id="value" name="value" rows="5">Hello from .NET and Kafka!</textarea>
      <button class="btn btn-primary mt-3" type="submit">Send to Kafka</button>
      <pre id="result" class="mt-3 mb-0"></pre>
    </form>
    <script>
      document.getElementById('produce-form').addEventListener('submit', async (event) => {{
        event.preventDefault();
        const response = await fetch('/api/messages', {{
          method: 'POST',
          headers: {{ 'Content-Type': 'application/json' }},
          body: JSON.stringify({{
            key: document.getElementById('key').value,
            value: document.getElementById('value').value
          }})
        }});
        document.getElementById('result').textContent = JSON.stringify(await response.json(), null, 2);
      }});
    </script>
    """), "text/html"));

app.MapGet("/consume", (KafkaSettings settings) => Results.Content(Html.Page("Consume Messages", $"""
    <p class="lead">Recent messages consumed from Kafka topic <code>{settings.Topic}</code>.</p>
    <button class="btn btn-secondary mb-3" id="refresh">Refresh now</button>
    <div id="messages"></div>
    <script>
      function escapeHtml(value) {{
        return String(value ?? '').replace(/[&<>"']/g, character => ({{
          '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }}[character]));
      }}
      async function loadMessages() {{
        const response = await fetch('/api/messages');
        const messages = await response.json();
        document.getElementById('messages').innerHTML = messages.map(message => `
          <div class="card mb-2">
            <div class="card-body">
              <h5 class="card-title">${{escapeHtml(message.key || '(no key)')}}</h5>
              <h6 class="card-subtitle mb-2 text-muted">partition ${{message.partition}}, offset ${{message.offset}}</h6>
              <pre class="mb-0">${{escapeHtml(message.value)}}</pre>
            </div>
          </div>`).join('') || '<p>No messages consumed yet.</p>';
      }}
      document.getElementById('refresh').addEventListener('click', loadMessages);
      loadMessages();
      setInterval(loadMessages, 2000);
    </script>
    """), "text/html"));

app.MapPost("/api/messages", async (ProduceRequest request, IProducer<string, string> producer, KafkaSettings settings) =>
{
    if (string.IsNullOrWhiteSpace(request.Value))
    {
        return Results.BadRequest(new { error = "Message value is required." });
    }

    var result = await producer.ProduceAsync(settings.Topic, new Message<string, string>
    {
        Key = string.IsNullOrWhiteSpace(request.Key) ? null : request.Key,
        Value = request.Value,
    });

    return Results.Ok(new
    {
        status = "sent",
        result.Topic,
        result.Partition,
        result.Offset,
        result.Message.Key,
        result.Message.Value,
    });
});

app.MapGet("/api/messages", (MessageStore store) => Results.Ok(store.GetAll()));

app.Lifetime.ApplicationStopping.Register(() =>
{
    var producer = app.Services.GetRequiredService<IProducer<string, string>>();
    producer.Flush(TimeSpan.FromSeconds(10));
    producer.Dispose();
});

app.Run();

record KafkaSettings(string BootstrapServers, string Topic, string GroupId);
record ProduceRequest(string? Key, string Value);
record ConsumedMessage(string? Key, string Value, string Topic, int Partition, long Offset, DateTime Timestamp);

class MessageStore
{
    private const int MaxMessages = 50;
    private readonly ConcurrentQueue<ConsumedMessage> messages = new();

    public void Add(ConsumedMessage message)
    {
        messages.Enqueue(message);
        while (messages.Count > MaxMessages && messages.TryDequeue(out _))
        {
        }
    }

    public ConsumedMessage[] GetAll() => messages.Reverse().ToArray();
}

class KafkaConsumerService(KafkaSettings settings, MessageStore store, ILogger<KafkaConsumerService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(() => Consume(stoppingToken), stoppingToken);

    private void Consume(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = settings.BootstrapServers,
            GroupId = settings.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(settings.Topic);
        logger.LogInformation("Consuming topic {Topic} from {BootstrapServers}", settings.Topic, settings.BootstrapServers);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(stoppingToken);
                store.Add(new ConsumedMessage(
                    result.Message.Key,
                    result.Message.Value,
                    result.Topic,
                    result.Partition.Value,
                    result.Offset.Value,
                    result.Message.Timestamp.UtcDateTime));
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Kafka consumer is stopping.");
        }
        finally
        {
            consumer.Close();
        }
    }
}

static class Html
{
    public static string Page(string title, string body) => $$"""
    <!doctype html>
    <html lang="en">
    <head>
      <meta charset="utf-8">
      <meta name="viewport" content="width=device-width, initial-scale=1">
      <title>{{title}}</title>
      <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css" rel="stylesheet">
    </head>
    <body>
      <nav class="navbar navbar-expand navbar-dark bg-dark mb-4">
        <div class="container">
          <a class="navbar-brand" href="/produce">.NET Kafka Webserver</a>
          <div class="navbar-nav">
            <a class="nav-link" href="/produce">Send</a>
            <a class="nav-link" href="/consume">Consume</a>
          </div>
        </div>
      </nav>
      <main class="container">
        <h1>{{title}}</h1>
        {{body}}
      </main>
    </body>
    </html>
    """;
}
