using PublicAPIFanOut.FanOutPublisher;
using PublicAPIFanOut.FanOutPublisher.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

builder.Services.AddOptions<RedisOptions>()
    .Bind(builder.Configuration.GetSection("Redis"));
builder.Services.AddOptions<PricesAPIOptions>()
    .Bind(builder.Configuration.GetSection("PricesAPI"));

var host = builder.Build();
host.Run();
