using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace Helix.Infrastructure.Connector;

internal static class MdnsMessage
{
    internal const string SmbService = "_smb._tcp.local";

    internal const string DeviceInfoService = "_device-info._tcp.local";

    internal const ushort TypeA = 1;
    internal const ushort TypePtr = 12;
    internal const ushort TypeTxt = 16;
    internal const ushort TypeSrv = 33;

    private const int HeaderLength = 12;
    private const int MaxPointerJumps = 32;

    internal sealed record Record(
        string Name,
        ushort Type,
        string? Target = null,
        IPAddress? Address = null,
        IReadOnlyList<string>? Text = null);

    internal sealed record Service(string Instance, string? HostName, IPAddress? Address, string? Model);

    internal static byte[] BuildQuery(ushort id, params string[] names)
    {
        using var stream = new MemoryStream();

        Span<byte> header = stackalloc byte[HeaderLength];
        BinaryPrimitives.WriteUInt16BigEndian(header, id);
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], (ushort)names.Length);
        stream.Write(header);

        Span<byte> question = stackalloc byte[4];

        foreach (string name in names)
        {
            WriteName(stream, name);

            BinaryPrimitives.WriteUInt16BigEndian(question, TypePtr);
            BinaryPrimitives.WriteUInt16BigEndian(question[2..], 1);
            stream.Write(question);
        }

        return stream.ToArray();
    }

    internal static List<Record> Parse(ReadOnlySpan<byte> packet)
    {
        List<Record> records = [];

        if (packet.Length < HeaderLength)
        {
            return records;
        }

        int questions = BinaryPrimitives.ReadUInt16BigEndian(packet[4..]);
        int answers = BinaryPrimitives.ReadUInt16BigEndian(packet[6..]) +
            BinaryPrimitives.ReadUInt16BigEndian(packet[8..]) +
            BinaryPrimitives.ReadUInt16BigEndian(packet[10..]);

        int offset = HeaderLength;

        try
        {
            for (int i = 0; i < questions; i++)
            {
                ReadName(packet, ref offset);
                offset += 4;
            }

            for (int i = 0; i < answers; i++)
            {
                string name = ReadName(packet, ref offset);

                ushort type = BinaryPrimitives.ReadUInt16BigEndian(packet[offset..]);
                int length = BinaryPrimitives.ReadUInt16BigEndian(packet[(offset + 8)..]);
                offset += 10;

                int dataStart = offset;
                ReadOnlySpan<byte> data = packet.Slice(dataStart, length);
                offset += length;

                Record? record = type switch
                {
                    TypeA when length == 4 => new Record(name, type, Address: new IPAddress(data)),
                    TypePtr => new Record(name, type, Target: ReadNameAt(packet, dataStart)),
                    TypeSrv when length > 6 => new Record(name, type, Target: ReadNameAt(packet, dataStart + 6)),
                    TypeTxt => new Record(name, type, Text: ReadText(data)),
                    _ => null,
                };

                if (record is not null)
                {
                    records.Add(record);
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or FormatException)
        {
        }

        return records;
    }

    internal static List<Service> ResolveSmbServices(IReadOnlyCollection<Record> records)
    {
        List<Service> services = [];

        IEnumerable<string> instances = records
            .Where(r => r.Type == TypePtr && Same(r.Name, SmbService) && r.Target is not null)
            .Select(r => r.Target!)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (string instance in instances)
        {
            string label = InstanceLabelOf(instance, SmbService);

            string? hostName = records
                .FirstOrDefault(r => r.Type == TypeSrv && Same(r.Name, instance))?.Target;

            IPAddress? address = hostName is null
                ? null
                : records.FirstOrDefault(r => r.Type == TypeA && Same(r.Name, hostName))?.Address;

            string? model = records
                .Where(r => r.Type == TypeTxt && Same(r.Name, $"{label}.{DeviceInfoService}"))
                .SelectMany(r => r.Text ?? [])
                .Where(entry => entry.StartsWith("model=", StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry["model=".Length..])
                .FirstOrDefault(value => value.Length > 0);

            services.Add(new Service(label, hostName, address, model));
        }

        return services;
    }

    private static string InstanceLabelOf(string instance, string service)
    {
        string suffix = "." + service;

        return instance.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? instance[..^suffix.Length]
            : instance;
    }

    private static bool Same(string left, string right) =>
        string.Equals(left.TrimEnd('.'), right.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

    private static void WriteName(Stream stream, string name)
    {
        foreach (string label in name.TrimEnd('.').Split('.'))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(label);
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }

        stream.WriteByte(0);
    }

    private static string ReadNameAt(ReadOnlySpan<byte> packet, int offset) => ReadName(packet, ref offset);

    private static string ReadName(ReadOnlySpan<byte> packet, ref int offset)
    {
        List<string> labels = [];
        int position = offset;
        int jumps = 0;
        bool jumped = false;

        while (true)
        {
            byte length = packet[position];

            if (length == 0)
            {
                position++;
                break;
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (++jumps > MaxPointerJumps)
                {
                    throw new FormatException("The name's compression pointers loop.");
                }

                int pointer = ((length & 0x3F) << 8) | packet[position + 1];

                if (!jumped)
                {
                    offset = position + 2;
                    jumped = true;
                }

                position = pointer;
                continue;
            }

            labels.Add(Encoding.UTF8.GetString(packet.Slice(position + 1, length)));
            position += 1 + length;
        }

        if (!jumped)
        {
            offset = position;
        }

        return string.Join('.', labels);
    }

    private static List<string> ReadText(ReadOnlySpan<byte> data)
    {
        List<string> entries = [];
        int position = 0;

        while (position < data.Length)
        {
            int length = data[position];
            entries.Add(Encoding.UTF8.GetString(data.Slice(position + 1, length)));
            position += 1 + length;
        }

        return entries;
    }
}
