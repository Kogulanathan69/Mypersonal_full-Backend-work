using System.Buffers.Binary;
using System.Text;
using BuildMind.Application.Features.Requirements.Contracts;

namespace BuildMind.Infrastructure.Persistence.Repositories.Requirements;

internal static class RequirementMutationCanonicalPayload
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static byte[] Encode(RequirementMutation mutation)
    {
        using var output = new MemoryStream();
        output.Write("BMRQ"u8);
        output.WriteByte(1);
        output.WriteByte((byte)mutation.Category);
        var fields = mutation.Fields;
        String(fields.CatalogueVersion);
        switch (mutation.Category)
        {
            case RequirementMutationCategory.Full:
                Integer(fields.FloorCount!.Value);
                Integer(fields.GroundFloorRooms!.Count);
                foreach (var room in fields.GroundFloorRooms)
                {
                    String(room!.RoomType);
                    Integer(room.Count!.Value);
                }
                Spaces();
                Relationships();
                String(fields.DesignMethod);
                break;
            case RequirementMutationCategory.Spaces: Spaces(); break;
            case RequirementMutationCategory.Relationships: Relationships(); break;
            case RequirementMutationCategory.DesignMethod: String(fields.DesignMethod); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        return output.ToArray();

        void Spaces()
        {
            Integer(fields.AdditionalSpaces.Count);
            foreach (var space in fields.AdditionalSpaces)
            {
                String(space!.Option);
                String(space.OtherText);
            }
        }

        void Relationships()
        {
            Integer(fields.Relationships.Count);
            foreach (var relationship in fields.Relationships)
            {
                String(relationship!.SourceRoomType);
                String(relationship.TargetRoomType);
                String(relationship.Kind);
            }
        }

        void Integer(int number)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, number);
            output.Write(bytes);
        }

        void String(string? value)
        {
            output.WriteByte(value is null ? (byte)0 : (byte)1);
            if (value is null) return;
            var bytes = Utf8.GetBytes(value);
            Integer(bytes.Length);
            output.Write(bytes);
        }
    }
}
