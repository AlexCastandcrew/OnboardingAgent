# Kafka Core Library

This library provides easy integration with Kafka producers and consumers using Dependency Injection (DI) and supports multiple serialization strategies.

---

## ✅ How to Use

### 1. Register Producers and Consumers in `Startup.cs` (or `Program.cs` in minimal APIs)

#### 🔸 JSON (System.Text.Json)

```csharp
builder.Services.AddKafkaConsumer(
    builder.Configuration,
    new List<string> { "your-topic-name" },
    "excel-mapping", // Your application prefix
    _ => KafkaSerializationHelper.CreateJsonDeserializer<ProjectDto>());

builder.Services.AddKafkaProducer<string, ProjectDto>(
    builder.Configuration,
    _ => KafkaSerializationHelper.CreateJsonSerializer<ProjectDto>());
```

#### 🔸 JSON (Newtonsoft.Json)

```csharp
builder.Services.AddKafkaConsumer(
    builder.Configuration,
    new List<string> { "your-topic-name" },
    "excel-mapping", // Your application prefix
    _ => KafkaSerializationHelper.CreateNewtonsoftDeserializer<JObject>());

builder.Services.AddKafkaProducer<string, JObject>(
    builder.Configuration,
    _ => KafkaSerializationHelper.CreateNewtonsoftSerializer<JObject>());
```

#### 🔸 AVRO

```csharp
builder.Services.AddKafkaConsumer(
    builder.Configuration,
    new List<string> { "your-topic-name" },
    "excel-mapping", // Your application prefix
    _ => KafkaSerializationHelper.CreateAvroDeserializer<TestAvroEvent>(builder.Configuration));

// One call registers the shared schema registry client and a producer for every
// generated Avro type (ISpecificRecord). A new Avro message type needs no further registration.
builder.Services.AddKafkaAvroProducers(builder.Configuration);
```

Inject `IKafkaProducer<string, TestAvroEvent>` as usual. A type with its own `AddKafkaProducer` registration (e.g. `JObject`) keeps it.
`CreateAvroSerializer` still works for a producer that needs a custom `ProducerConfig`, but it creates its own schema registry client.

Where `ProjectDto`, `TestAvroEvent`, or `JObject` is the type you want to produce/consume.

---

## 💡 Usage

### Consuming messages

Inject `IKafkaConsumer<T>` into your class:

```csharp
_consumer.Consume(message =>
{
    // Process message
}, cancellationToken);
```

### Producing messages

Inject `IKafkaProducer<TKey, TValue>` into your class:

```csharp
await _producer.ProduceAsync(new List<string> { "your-topic-name" }, messageKey, messageObject, cancellationToken);
```

---

## ⚙️ Required Configuration (`appsettings.json`)

```json
{
  "Application": {
    "Name": "App Name",
    "Prefix": "Your Prefix"
  },
  "Kafka": {
    "ConsumerGroupIdSuffix": "your-app-suffix",
    "ClientConfig": {
      "BootstrapServers": "your-bootstrap-server",
      "SaslMechanism": "PLAIN",
      "SaslUsername": "your-username",
      "SaslPassword": "your-password",
      "SecurityProtocol": "SASL_SSL"
    },
    "SchemaRegistryConnection": "https://your-schema-registry-url"
  }
}
```

---

## 📦 Available Serialization Types

| Type               | Description                                                                 | Notes                                |
|--------------------|-----------------------------------------------------------------------------|--------------------------------------|
| `System.Text.Json` | Default .NET JSON serializer, high performance.                             | ❌ Not compatible with `JObject`.    |
| `Newtonsoft.Json`  | Flexible and widely used JSON serializer with full support for `JObject`.   | ✅ Recommended for dynamic JSON.     |
| `Avro`             | Compact binary format for high-efficiency message serialization.            | Requires Schema Registry.            |

---

## ⚠️ Deprecation Notice

> The `KafkaFactory` for static methods remain available only for **legacy compatibility** within the application.

Going forward, it is strongly recommended to register all Kafka components via **Dependency Injection (DI)** as shown above.

---

## 📌 Project Reference

> Don’t forget to add a reference to `HTGPlus.Kafka` in your project.

```
<ProjectReference Include="..\HTGPlus.Kafka\HTGPlus.Kafka.csproj" />
```