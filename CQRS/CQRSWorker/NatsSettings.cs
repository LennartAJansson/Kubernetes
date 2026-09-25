namespace CQRSWorker;

/// <summary>Sektionen "Nats" (Nats__Url, Nats__Stream, Nats__Consumer).</summary>
public sealed class NatsSettings
{
    public const string SectionName = "Nats";

    public string Url { get; set; } = "nats://localhost:4222";

    /// <summary>Måste vara samma ström som API:t publicerar till.</summary>
    public string Stream { get; set; } = "PERSONS";

    /// <summary>Namnet på den varaktiga (durable) konsumenten. JetStream kommer
    /// ihåg hur långt den har läst, även om podden startas om.</summary>
    public string Consumer { get; set; } = "cqrs-worker";
}
