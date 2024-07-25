using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using PublicAPIFanOut.FanOutPublisher.Events;
using PublicAPIFanOut.FanOutPublisher.Options;
using StackExchange.Redis;
using System.Diagnostics;
using System.Text.Json;
using Websocket.Client;
using static StackExchange.Redis.RedisChannel;

namespace PublicAPIFanOut.FanOutPublisher
{

    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly RedisOptions _redisOptions;
        private readonly PricesAPIOptions _pricesApiOptions;
        private readonly ConnectionMultiplexer _redisConnection;

        private static readonly Stopwatch _timer = new();

        public Worker(
            ILogger<Worker> logger,
            IOptions<RedisOptions> redisOptions,
            IOptions<PricesAPIOptions> pricesApiOptions)
        {
            _logger = logger;
            _redisOptions = redisOptions.Value;
            _pricesApiOptions = pricesApiOptions.Value;
            _redisConnection = ConnectionMultiplexer.Connect(_redisOptions.ConnectionString);

            // TODO web socket:
            // 1. once per 24 hours we would need to reconnect to the binance wss
            // 2. once per 3 minutes when we recieve ping we should respond with pong
            // conside other points from here https://github.com/binance/binance-spot-api-docs/blob/master/web-socket-streams.md#websocket-limits
            // n. make sure that we're using backoffs, and other best practices when using websockets

            // TODO other:
            // - where to deploy redis?
            // - how to create scalable servers that will connect to redis and send websocket events to UI app?
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Publisher started at: {time} UTC", DateTimeOffset.UtcNow);
            }

            Dictionary<string, RedisChannel> streamSubscribers = new();

            foreach (var stream in _pricesApiOptions.Streams)
            {
                streamSubscribers.Add(stream, new(stream, PatternMode.Literal));
            }

            await SubscribeToPriceAPI(
                _redisConnection.GetSubscriber(),
                streamSubscribers,
                new Uri($"{_pricesApiOptions.BaseUrl}?streams={string.Join("/", _pricesApiOptions.Streams)}"));
        }

        private async Task SubscribeToPriceAPI(ISubscriber subscriber, Dictionary<string, RedisChannel> streamToChannel, Uri url)
        {
            try
            {
                var exitEvent = new ManualResetEvent(false);
                using var client = new WebsocketClient(url);
                client.DisconnectionHappened.Subscribe(info =>
                {
                    _logger.LogWarning("Publisher (wss): Disconnect happened, type: {type}", info.Type);
                });
                client.ReconnectTimeout = TimeSpan.FromSeconds(30);
                client.ReconnectionHappened.Subscribe(info =>
                {
                    if (info.Type is not ReconnectionType.Initial)
                    {
                        _logger.LogWarning("Publisher (wss): Reconnection happened, type: {type}", info.Type);
                    }
                    else
                    {
                        _logger.LogInformation("Publisher (wss): Initial reconnection happened, type: {type}", info.Type);
                    }
                });
                client.MessageReceived.Subscribe(msg =>
                {
                    _timer.Stop();
                    _logger.LogInformation("Publisher (wss): Message received (in {elapsed} ms): {msg}", _timer.ElapsedMilliseconds, msg);

                    if (msg.MessageType is System.Net.WebSockets.WebSocketMessageType.Text)
                    {
                        if (msg.Text is null)
                        {
                            _logger.LogWarning("Publisher (wss): Message received (in {elapsed} ms): {msg}", _timer.ElapsedMilliseconds, "EMPTY");
                        }
                        else
                        {
                            var json = JObject.Parse(msg.Text);

                            var streamFired = (string?)json["stream"];

                            var klineData = json["data"]?["k"];

                            var curency = (string?)json["data"]?["s"];

                            var openPrice = (string?)klineData?["o"];
                            var closePrice = (string?)klineData?["c"];
                            var highPrice = (string?)klineData?["h"];
                            var lowPrice = (string?)klineData?["l"];
                            var volume = (string?)klineData?["v"];

                            var priceParsed = decimal.TryParse(closePrice, out var price);

                            var priceEvent = JsonSerializer.Serialize(new PricesUpdatedEvent
                            {
                                Currency = curency ?? "UNKNOWN",
                                Price = priceParsed ? price : -1
                            });

                            if (!string.IsNullOrEmpty(streamFired))
                            {
                                var channelExists = streamToChannel.TryGetValue(streamFired, out var channel);

                                if (channelExists)
                                {
                                    subscriber.PublishAsync(channel, priceEvent);
                                }
                            }
                        }
                    }

                    _timer.Restart();
                });

                await client.Start();
                _timer.Start();

                exitEvent.WaitOne();
            }
            catch (Exception ex)
            {
                _logger.LogError("ERROR in publisher: " + ex.ToString());
            }
        }
    }
}
