using FluentAssertions;
using MpvShell.Player.MpvSidecar;

namespace MpvShell.Player.MpvSidecar.Tests;

public sealed class MpvEventParserTests
{
    [Fact]
    public void Property_change_event_should_extract_pause_state()
    {
        const string line = """
            {"event":"property-change","name":"pause","data":false}
            """;

        var parsed = MpvEventParser.Parse(line);

        parsed.EventName.Should().Be("property-change");
        parsed.PropertyName.Should().Be("pause");
        parsed.BooleanValue.Should().BeFalse();
    }
}
