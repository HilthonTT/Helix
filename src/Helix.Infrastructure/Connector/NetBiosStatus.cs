using System.Buffers.Binary;
using System.Text;

namespace Helix.Infrastructure.Connector;

internal static class NetBiosStatus
{
    internal const int Port = 137;

    private const ushort TypeNodeStatus = 0x21;
    private const int HeaderLength = 12;
    private const int NameEntryLength = 18;
    private const byte WorkstationSuffix = 0x00;
    private const byte FileServerSuffix = 0x20;
    private const ushort GroupFlag = 0x8000;

    internal static byte[] BuildQuery(ushort id)
    {
        byte[] packet = new byte[HeaderLength + 34 + 4];

        BinaryPrimitives.WriteUInt16BigEndian(packet, id);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), 1);

        packet[HeaderLength] = 32;

        byte[] wildcard = new byte[16];
        wildcard[0] = (byte)'*';

        for (int i = 0; i < 16; i++)
        {
            packet[HeaderLength + 1 + (2 * i)] = (byte)('A' + (wildcard[i] >> 4));
            packet[HeaderLength + 2 + (2 * i)] = (byte)('A' + (wildcard[i] & 0x0F));
        }

        int tail = HeaderLength + 34;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(tail), TypeNodeStatus);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(tail + 2), 1);

        return packet;
    }

    internal static string? ParseName(ReadOnlySpan<byte> packet)
    {
        try
        {
            if (packet.Length < HeaderLength || BinaryPrimitives.ReadUInt16BigEndian(packet[6..]) == 0)
            {
                return null;
            }

            int offset = SkipName(packet, HeaderLength);

            if (BinaryPrimitives.ReadUInt16BigEndian(packet[offset..]) != TypeNodeStatus)
            {
                return null;
            }

            offset += 10;

            int count = packet[offset++];
            string? workstation = null;

            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> entry = packet.Slice(offset + (i * NameEntryLength), NameEntryLength);

                byte suffix = entry[15];
                bool isGroup = (BinaryPrimitives.ReadUInt16BigEndian(entry[16..]) & GroupFlag) != 0;

                if (isGroup)
                {
                    continue;
                }

                string name = Encoding.ASCII.GetString(entry[..15]).TrimEnd(' ', '\0');

                if (name.Length == 0)
                {
                    continue;
                }

                if (suffix == FileServerSuffix)
                {
                    return name;
                }

                if (suffix == WorkstationSuffix)
                {
                    workstation ??= name;
                }
            }

            return workstation;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static int SkipName(ReadOnlySpan<byte> packet, int offset)
    {
        while (true)
        {
            byte length = packet[offset];

            if (length == 0)
            {
                return offset + 1;
            }

            if ((length & 0xC0) == 0xC0)
            {
                return offset + 2;
            }

            offset += 1 + length;
        }
    }
}
