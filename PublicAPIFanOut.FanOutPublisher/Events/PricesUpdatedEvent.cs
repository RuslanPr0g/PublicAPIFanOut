namespace PublicAPIFanOut.FanOutPublisher.Events;

public class PricesUpdatedEvent
{
    public string Currency { get; set; }
    public decimal Price { get; set; }
}
