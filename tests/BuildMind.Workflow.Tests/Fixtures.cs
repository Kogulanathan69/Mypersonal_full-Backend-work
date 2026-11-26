using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;
namespace BuildMind.Workflow.Tests;
// Synthetic identities and policy values only; never production authorization evidence.
internal static class Fixtures
{
    internal static Guid Id(int n) => new(n, 0, 0, new byte[8]);
    internal static readonly DateTimeOffset Time = new(2026,11,17,12,0,0,TimeSpan.FromMinutes(330));
    internal static ShareConsent Consent() => new(Id(1),Id(2),Id(3),Id(4),Id(5),Id(6), new[]{Id(7)}, "test-policy", Time,true);
    internal static ArchitectRequest Pending() => ArchitectRequest.CreatePending(Consent(),Time);
    internal static RequirementRevision Requirements() => new(Id(8),Id(3),1,"test-schema","test-catalogue",new RequirementContent(1,[new("ROOM_A",1),new("ROOM_B",1)],[new("SPACE_TEXT","text")],[new("ROOM_A","ROOM_B","NEAR")],"METHOD_A"),Time);
}
