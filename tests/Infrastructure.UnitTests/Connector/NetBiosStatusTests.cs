using FluentAssertions;
using Helix.Infrastructure.Connector;
using System.Buffers.Binary;
using System.Text;

namespace Infrastructure.UnitTests.Connector;

public sealed class NetBiosStatusTests
{
    private static byte[] Reply(params (string Name, byte Suffix, bool Group)[] names)
    {
        List<byte> bytes = [0x12, 0x34, 0x84, 0x00, 0, 0, 0, 1, 0, 0, 0, 0];

        byte[] query = NetBiosStatus.BuildQuery(0x1234);
        bytes.AddRange(query.AsSpan(12, 34).ToArray());
        bytes.AddRange(new byte[] { 0, 0x21, 0, 1, 0, 0, 0, 0 });

        int dataLength = 1 + (names.Length * 18) + 46;
        bytes.Add((byte)(dataLength >> 8));
        bytes.Add((byte)dataLength);
        bytes.Add((byte)names.Length);

        foreach ((string name, byte suffix, bool group) in names)
        {
            bytes.AddRange(Encoding.ASCII.GetBytes(name.PadRight(15)));
            bytes.Add(suffix);
            bytes.Add(group ? (byte)0x84 : (byte)0x04);
            bytes.Add(0);
        }

        bytes.AddRange(new byte[46]);

        return [.. bytes];
    }

    [Fact]
    public void BuildQuery_Should_AskTheWildcardNameForItsStatus()
    {
        byte[] query = NetBiosStatus.BuildQuery(7);

        query.Should().HaveCount(50);
        query[12].Should().Be(32);
        Encoding.ASCII.GetString(query, 13, 32).Should().Be("CK" + new string('A', 30));
        BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(46)).Should().Be(0x21);
    }

    [Fact]
    public void ParseName_Should_PreferTheFileServerName()
    {
        byte[] reply = Reply(("WORKGROUP", 0x00, true), ("DESKTOP", 0x00, false), ("STUDIO", 0x20, false));

        NetBiosStatus.ParseName(reply).Should().Be("STUDIO");
    }

    [Fact]
    public void ParseName_Should_FallBackToTheWorkstationName()
    {
        byte[] reply = Reply(("WORKGROUP", 0x00, true), ("NAS", 0x00, false));

        NetBiosStatus.ParseName(reply).Should().Be("NAS");
    }

    [Fact]
    public void ParseName_Should_IgnoreGroupNames()
    {
        NetBiosStatus.ParseName(Reply(("WORKGROUP", 0x00, true))).Should().BeNull();
    }

    [Fact]
    public void ParseName_Should_ReturnNull_ForATruncatedReply()
    {
        byte[] reply = Reply(("NAS", 0x20, false));

        NetBiosStatus.ParseName(reply.AsSpan(0, 60)).Should().BeNull();
    }
}
