namespace OutreachStudio.Web.Client.Api;

/// <summary>
/// What the browser paid for the audience. The server implementation of
/// <see cref="IAudienceSource"/> reads from the database and does not implement this, so the UI
/// asks for it with a type check and shows the chip only when there was a download.
/// </summary>
public interface IAudienceDownloadStats
{
    /// <summary>How long the download and the decode took.</summary>
    long ElapsedMs { get; }

    /// <summary>Bytes on the wire, when the response reported a content length.</summary>
    long? Bytes { get; }
}
