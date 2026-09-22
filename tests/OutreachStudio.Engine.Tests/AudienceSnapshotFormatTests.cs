using OutreachStudio.Data.Synthetic;
using OutreachStudio.Engine.Audience;
using static OutreachStudio.Engine.Tests.Fixtures;

namespace OutreachStudio.Engine.Tests;

public class AudienceSnapshotFormatTests
{
    [Fact]
    public void Round_trips_the_synthetic_base()
    {
        var users = SyntheticUsers.Generate(Now, count: 2_000);
        using var stream = new MemoryStream();

        AudienceSnapshotFormat.Write(stream, users);
        stream.Position = 0;
        var back = AudienceSnapshotFormat.Read(stream);

        Assert.Equal(users.Count, back.Count);
        for (var i = 0; i < users.Count; i++)
        {
            var (a, b) = (users[i], back[i]);
            Assert.Equal(a with { Events = b.Events }, b);
            Assert.Equal(a.Events.Count, b.Events.Count);
            foreach (var (type, stamps) in a.Events)
            {
                Assert.Equal(stamps, b.Events[type]);
            }
        }
    }

    [Fact]
    public void Is_a_fraction_of_the_json_size()
    {
        var users = SyntheticUsers.Generate(Now, count: 2_000);
        using var stream = new MemoryStream();
        AudienceSnapshotFormat.Write(stream, users);
        var json = System.Text.Json.JsonSerializer.Serialize(users);

        Assert.True(stream.Length * 3 < json.Length, $"binary {stream.Length} bytes, json {json.Length} bytes");
    }

    [Fact]
    public void Rejects_another_version()
    {
        using var stream = new MemoryStream([9, 0, 0, 0, 0, 0, 0, 0]);
        Assert.Throws<InvalidDataException>(() => AudienceSnapshotFormat.Read(stream));
    }
}
