using CQRSWorker;
using CQRSWorker.Data;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Net;

// ============================================================================
// CQRSWorker - skriv-sidan i CQRS-mönstret.
//
//   NATS JetStream (persons.cmd.*) -> Worker -> EF Core -> MySQL
//
// Workern har ingen HTTP-yta och ingen Service/Ingress i klustret. Den enda
// vägen in är kommandon på JetStream. Den äger databasschemat och är den
// enda som skriver till det.
// ============================================================================

var builder = Host.CreateApplicationBuilder(args);

var nats = builder.Configuration.GetSection(NatsSettings.SectionName).Get<NatsSettings>() ?? new NatsSettings();
builder.Services.AddSingleton(nats);
builder.Services.AddSingleton(_ => new NatsClient(new NatsOpts { Url = nats.Url, Name = "cqrs-worker" }));
builder.Services.AddSingleton<INatsJSContext>(sp => sp.GetRequiredService<NatsClient>().CreateJetStreamContext());

var writeDb = builder.Configuration.GetConnectionString("WriteDb")
    ?? throw new InvalidOperationException("ConnectionStrings:WriteDb saknas (miljövariabel ConnectionStrings__WriteDb).");
builder.Services.AddDbContextFactory<PersonsDbContext>(o => o.UseMySQL(writeDb));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
