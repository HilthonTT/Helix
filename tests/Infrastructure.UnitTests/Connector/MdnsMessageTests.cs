using FluentAssertions;
using Helix.Infrastructure.Connector;
using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace Infrastructure.UnitTests.Connector;

public sealed class MdnsMessageTests
{
    private sealed class PacketBuilder
    {
        private readonly List<byte> _bytes = [.. new byte[12]];
        private int _records;

        public int Offset => _bytes.Count;

        public PacketBuilder Record(string name, ushort type, byte[] data) =>
            RecordWithName(Name(name), type, data);

        public PacketBuilder RecordWithName(byte[] name, ushort type, byte[] data)
        {
            _bytes.AddRange(name);
            _bytes.AddRange(UShort(type));
            _bytes.AddRange(UShort(0x8001));
            _bytes.AddRange(new byte[] { 0, 0, 0x11, 0x94 });
            _bytes.AddRange(UShort((ushort)data.Length));
            _bytes.AddRange(data);
            _records++;

            return this;
        }

        public byte[] Build()
        {
            byte[] packet = [.. _bytes];
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), 0x8400);
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(6), (ushort)_records);

            return packet;
        }
    }

    private static byte[] Name(string name)
    {
        List<byte> bytes = [];

        foreach (string label in name.Split('.'))
        {
            byte[] encoded = Encoding.UTF8.GetBytes(label);
            bytes.Add((byte)encoded.Length);
            bytes.AddRange(encoded);
        }

        bytes.Add(0);

        return [.. bytes];
    }

    private static byte[] UShort(ushort value)
    {
        byte[] bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);

        return bytes;
    }

    private static byte[] Srv(string target) => [0, 0, 0, 0, 0x01, 0xBD, .. Name(target)];

    private static byte[] Txt(params string[] entries) =>
        [.. entries.SelectMany(entry => new[] { (byte)entry.Length }.Concat(Encoding.UTF8.GetBytes(entry)))];

    [Fact]
    public void BuildQuery_Should_AskForEachServiceAsAPointerQuestion()
    {
        byte[] query = MdnsMessage.BuildQuery(0x1234, MdnsMessage.SmbService, MdnsMessage.DeviceInfoService);

        BinaryPrimitives.ReadUInt16BigEndian(query).Should().Be(0x1234);
        BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(4)).Should().Be(2);

        byte[] firstQuestion = [.. Name("_smb._tcp.local"), 0, 12, 0, 1];
        query.AsSpan(12, firstQuestion.Length).ToArray().Should().Equal(firstQuestion);
    }

    [Fact]
    public void ResolveSmbServices_Should_FollowPointerServiceAndAddress()
    {
        byte[] packet = new PacketBuilder()
            .Record("_smb._tcp.local", MdnsMessage.TypePtr, Name("Studio NAS._smb._tcp.local"))
            .Record("Studio NAS._smb._tcp.local", MdnsMessage.TypeSrv, Srv("ds920.local"))
            .Record("ds920.local", MdnsMessage.TypeA, [192, 0, 2, 10])
            .Record("Studio NAS._device-info._tcp.local", MdnsMessage.TypeTxt, Txt("model=DS920+", "osxvers=21"))
            .Build();

        List<MdnsMessage.Service> services = MdnsMessage.ResolveSmbServices(MdnsMessage.Parse(packet));

        services.Should().ContainSingle().Which.Should().Be(
            new MdnsMessage.Service("Studio NAS", "ds920.local", IPAddress.Parse("192.0.2.10"), "DS920+"));
    }

    [Fact]
    public void Parse_Should_FollowCompressedNames()
    {
        var builder = new PacketBuilder();
        int serviceOffset = builder.Offset;

        builder.Record("_smb._tcp.local", MdnsMessage.TypePtr, Name("NAS._smb._tcp.local"));
        builder.RecordWithName([0xC0, (byte)serviceOffset], MdnsMessage.TypePtr, Name("Other._smb._tcp.local"));

        List<MdnsMessage.Record> records = MdnsMessage.Parse(builder.Build());

        records.Should().HaveCount(2);
        records[1].Name.Should().Be("_smb._tcp.local");
        records[1].Target.Should().Be("Other._smb._tcp.local");
    }

    [Fact]
    public void ResolveSmbServices_Should_KeepAServiceWhoseAddressDidNotArrive()
    {
        byte[] packet = new PacketBuilder()
            .Record("_smb._tcp.local", MdnsMessage.TypePtr, Name("NAS._smb._tcp.local"))
            .Record("NAS._smb._tcp.local", MdnsMessage.TypeSrv, Srv("nas.local"))
            .Build();

        MdnsMessage.Service service = MdnsMessage.ResolveSmbServices(MdnsMessage.Parse(packet)).Single();

        service.HostName.Should().Be("nas.local");
        service.Address.Should().BeNull();
    }

    [Fact]
    public void Parse_Should_KeepWhatItReadBeforeATruncatedRecord()
    {
        byte[] packet = new PacketBuilder()
            .Record("nas.local", MdnsMessage.TypeA, [192, 0, 2, 10])
            .Record("other.local", MdnsMessage.TypeA, [192, 0, 2, 11])
            .Build();

        List<MdnsMessage.Record> records = MdnsMessage.Parse(packet.AsSpan(0, packet.Length - 3));

        records.Should().ContainSingle().Which.Name.Should().Be("nas.local");
    }

    [Fact]
    public void Parse_Should_StopOnAPointerLoop()
    {
        byte[] packet = new PacketBuilder()
            .RecordWithName([0xC0, 12], MdnsMessage.TypeA, [192, 0, 2, 10])
            .Build();

        MdnsMessage.Parse(packet).Should().BeEmpty();
    }
}
