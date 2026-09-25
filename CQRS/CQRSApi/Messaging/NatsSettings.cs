namespace CQRSApi.Messaging;

/// <summary>Sektionen "Nats" i appsettings.json / miljövariabler (Nats__Url, Nats__Stream).</summary>
public sealed class NatsSettings
{
    public const string SectionName = "Nats";

    public string Url { get; set; } = "nats://localhost:4222";

    /// <summary>Namnet på JetStream-strömmen som lagrar kommandona.</summary>
    public string Stream { get; set; } = "PERSONS";
}
