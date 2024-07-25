namespace PublicAPIFanOut.FanOutPublisher.Options;

public class RedisOptions
{
    public string ConnectionString { get; set; }
    public string[] Channels { get; set; }
}
