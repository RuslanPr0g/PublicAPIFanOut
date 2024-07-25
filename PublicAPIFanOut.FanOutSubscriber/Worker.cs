using StackExchange.Redis;
using static StackExchange.Redis.RedisChannel;

namespace PublicAPIFanOut.FanOutSubscriber
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private static readonly string _redisConnectionString = "localhost:6379";
        private static readonly string _redisChannel = "ethusdt@kline_1m"; // btcusdt@kline_1m
        private readonly ConnectionMultiplexer _redisConnection;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
            _redisConnection = ConnectionMultiplexer.Connect(_redisConnectionString);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Subscriber started at: {time} with channel {channel}", DateTimeOffset.UtcNow, _redisChannel);
            }

            var subscriber = _redisConnection.GetSubscriber();

            RedisChannel channelWithLiteral = new(_redisChannel, PatternMode.Literal);
            await subscriber.SubscribeAsync(channelWithLiteral, (channel, message) =>
            {
                _logger.LogInformation("Received message at: {time}. Content: {content}", DateTimeOffset.UtcNow, message);
            });
        }
    }
}
