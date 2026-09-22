namespace OutreachStudio.Providers;

/// <summary>What the worker sends to a provider. The same body for push and email, the provider decides what it means.</summary>
public sealed record SendRequest(long DeliveryId, string To, string Title, string Body);

public sealed record SendResponse(string MessageId);

/// <summary>Posted back to the web app some seconds after a send, out of order and sometimes twice.</summary>
public sealed record Receipt(string MessageId, string Type, DateTimeOffset At)
{
    public const string Delivered = "delivered";
    public const string Opened = "opened";
}
