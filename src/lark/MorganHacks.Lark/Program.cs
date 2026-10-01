using Amazon.SimpleEmailV2;
using MorganHacks.Lark;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Sending;
using MorganHacks.Observability;
using Npgsql;
using MorganHacks.Features;

var builder = Host.CreateApplicationBuilder(args);

builder.UseArcticLogging("lark");

var connectionString =
    builder.Configuration.GetConnectionString("Postgres")
    ?? Environment.GetEnvironmentVariable("ARCTIC_DB")
    ?? "Host=localhost;Port=5432;Database=morganhacks;Username=arctic;Password=local-dev-only";

builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<MessageQueue>();
builder.Services.AddSingleton<LinkTrackingStore>();
builder.Services.AddSingleton<UnsubscribeStore>();
builder.Services.AddSingleton<TemplateStore>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.Configure<SendLoopOptions>(builder.Configuration.GetSection("SendLoop"));

// Region and credentials come from the environment, so nothing secret is in
// the repo and the same binary runs locally, on staging and in production.
//
// Constructing the SES client without a region throws, so an unset variable
// would take the whole worker down at startup and keep it down — a crash loop
// that reports a dependency-injection stack trace rather than "no region
// configured". Checked here instead, and the worker runs either way.
var awsRegion = builder.Configuration["AWS_REGION"]
                ?? Environment.GetEnvironmentVariable("AWS_REGION");

// Which configuration set each send is tagged with, and therefore whether
// SES reports anything back about it. Optional for the same reason the region
// is checked rather than assumed: a worker that refuses to start because event
// publishing is not set up yet is worse than one that sends without it.
//
// The cost of leaving it unset is not nothing, and is worth saying out loud —
// bounces and complaints never arrive, so notify.suppressions stays empty of
// real ones and every message stops at 'sent'. See docs/runbooks.
var sesConfigurationSet = builder.Configuration["SES_CONFIGURATION_SET"]
                          ?? Environment.GetEnvironmentVariable("SES_CONFIGURATION_SET");

if (!string.IsNullOrWhiteSpace(awsRegion))
{
    builder.Services.AddSingleton<IAmazonSimpleEmailServiceV2>(
        _ => new AmazonSimpleEmailServiceV2Client(
            Amazon.RegionEndpoint.GetBySystemName(awsRegion)));

    builder.Services.AddSingleton<IEmailProvider>(sp => new SesEmailProvider(
        sp.GetRequiredService<IAmazonSimpleEmailServiceV2>(),
        sp.GetRequiredService<ILogger<SesEmailProvider>>(),
        string.IsNullOrWhiteSpace(sesConfigurationSet) ? null : sesConfigurationSet.Trim()));

    if (string.IsNullOrWhiteSpace(sesConfigurationSet))
    {
        // Loud once at startup rather than silent forever. The symptom
        // otherwise is every message sitting at 'sent' and nobody wondering
        // why none ever reaches 'delivered'.
        Console.WriteLine(
            "SES_CONFIGURATION_SET is not set, so SES will report no bounces, "
            + "complaints or deliveries and addresses will not be suppressed.");
    }
}
else
{
    builder.Services.AddSingleton<IEmailProvider, UnconfiguredEmailProvider>();
}

builder.Services.AddHostedService<SendLoop>();

// Feature flags. None are read here yet; the call is what makes adding the
// first one a one-line change, and what makes a missing features.json fail
// at start-up rather than at the moment somebody first relies on a flag.
builder.AddFeatures();

var host = builder.Build();
host.Run();
