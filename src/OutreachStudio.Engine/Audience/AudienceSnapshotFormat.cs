using System.Text;

namespace OutreachStudio.Engine.Audience;

/// <summary>
/// The wire format of the user base for the browser. JSON for twenty thousand users with their
/// events is twelve megabytes and takes the WebAssembly interpreter twenty seconds to parse.
/// A flat binary stream with delta-coded event timestamps is a fifth of the size and parses in
/// well under a second. Both sides are in the engine so they cannot drift apart.
/// </summary>
public static class AudienceSnapshotFormat
{
    private const int Version = 1;

    public static void Write(Stream stream, IReadOnlyList<AudienceUser> users)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Version);
        w.Write(users.Count);
        foreach (var u in users)
        {
            w.Write(u.Id);
            w.Write(u.FirstName);
            w.Write(u.Email);
            w.Write(u.Country);
            w.Write((byte)u.Tier);
            w.Write(u.SignupDate.DayNumber);
            w.Write(u.LastActive.DayNumber);
            w.Write((byte)u.Platform);
            w.Write(u.MarketingConsent);
            w.Write(u.TimeZone);
            w.Write(u.PointsBalance);
            w.Write((byte)u.Events.Count);
            foreach (var (type, stamps) in u.Events)
            {
                w.Write((byte)type);
                w.Write7BitEncodedInt(stamps.Length);
                long previous = 0;
                foreach (var stamp in stamps)
                {
                    // Sorted ascending, so every delta is non-negative and small.
                    w.Write7BitEncodedInt64(stamp - previous);
                    previous = stamp;
                }
            }
        }
    }

    public static IReadOnlyList<AudienceUser> Read(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var version = r.ReadInt32();
        if (version != Version)
        {
            throw new InvalidDataException($"Audience snapshot version {version}, expected {Version}");
        }
        var count = r.ReadInt32();
        var users = new List<AudienceUser>(count);
        for (var i = 0; i < count; i++)
        {
            var id = r.ReadInt32();
            var firstName = r.ReadString();
            var email = r.ReadString();
            var country = r.ReadString();
            var tier = (Tier)r.ReadByte();
            var signup = DateOnly.FromDayNumber(r.ReadInt32());
            var lastActive = DateOnly.FromDayNumber(r.ReadInt32());
            var platform = (Platform)r.ReadByte();
            var consent = r.ReadBoolean();
            var timeZone = r.ReadString();
            var points = r.ReadInt32();
            var typeCount = r.ReadByte();
            var events = new Dictionary<EventType, long[]>(typeCount);
            for (var t = 0; t < typeCount; t++)
            {
                var type = (EventType)r.ReadByte();
                var stamps = new long[r.Read7BitEncodedInt()];
                long previous = 0;
                for (var s = 0; s < stamps.Length; s++)
                {
                    previous += r.Read7BitEncodedInt64();
                    stamps[s] = previous;
                }
                events[type] = stamps;
            }
            users.Add(new AudienceUser(id, firstName, email, country, tier, signup, lastActive, platform, consent, timeZone, points, events));
        }
        return users;
    }
}
