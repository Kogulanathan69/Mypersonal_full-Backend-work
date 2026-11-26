using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.Reports.Contracts;
using BuildMind.Application.Features.Reports.Services;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;

namespace BuildMind.Reports.Tests.Contracts;

public sealed class ReportGenerationCandidateTests
{
    private static ReportRenderData Data(int floors = 1, string name = "SYNTHETIC room", string evidence = "UNKNOWN", bool concave = false, int rooms = 1)
    {
        var old = JsonSerializer.Deserialize<PlanGeometryDto>(SyntheticReportFixtures.Geometry())!;
        var levels = Enumerable.Range(0, floors).Select(level =>
        {
            CoordinatePointDto[] points = [new(){X=0,Y=0},new(){X=4,Y=0},new(){X=4,Y=3},new(){X=0,Y=3}];
            if(concave) points=[new(){X=0,Y=0},new(){X=4,Y=0},new(){X=4,Y=1},new(){X=1,Y=1},new(){X=1,Y=3},new(){X=0,Y=3}];
            var walls = Enumerable.Range(0,rooms).SelectMany(index=>Enumerable.Range(0,points.Length).Select(i=>new WallDto { Id=SyntheticReportFixtures.Id(100000+level*1000+index*20+i), Start=new(){X=points[i].X+index*6,Y=points[i].Y}, End=new(){X=points[(i+1)%points.Length].X+index*6,Y=points[(i+1)%points.Length].Y}, Thickness=0.2 })).ToArray();
            var selections=Enumerable.Range(0,rooms).Select(index=>new RoomDto {Id=SyntheticReportFixtures.Id(20000+level*100+index),Name=name+" "+(index+1),Type="ROOM_A",Polygon=points.Select(p=>new CoordinatePointDto{X=p.X+index*6,Y=p.Y}).Append(new(){X=points[0].X+index*6,Y=points[0].Y}).ToArray(),WallIds=walls.Skip(index*points.Length).Take(points.Length).Select(w=>w.Id).ToArray()}).ToArray();
            return new FloorDto { Id=SyntheticReportFixtures.Id(10000+level),Level=level,
                Rooms=selections,Walls=walls,
                Openings=[new OpeningDto {Id=SyntheticReportFixtures.Id(30000+level),Type="door",WallId=walls[0].Id,Offset=1,Width=0.9},
                    new OpeningDto {Id=SyntheticReportFixtures.Id(40000+level),Type="window",WallId=walls[1].Id,Offset=0.1,Width=0.6}] };
        }).ToArray();
        var geometry = new PlanGeometryDto {SchemaVersion=old.SchemaVersion,ProjectId=old.ProjectId,PlanId=old.PlanId,LandRevisionId=old.LandRevisionId,RequirementsRevisionId=old.RequirementsRevisionId,
            CoordinateSystem=new(){Type="localCartesian",Unit="m",Origin=new(){X=0,Y=0},XDirection="right",YDirection="up"},Floors=levels};
        var plan=SyntheticReportFixtures.PlanSource(JsonSerializer.Serialize(geometry),null,evidence);
        return ReportDataBuilder.Build(ReportInputSnapshot.FromResolvedSources("test-report-v1",SyntheticReportFixtures.Time.AddMinutes(2),SyntheticReportFixtures.Requirements(),plan,SyntheticReportFixtures.Confirm(plan),SyntheticReportFixtures.Disclaimer()).Snapshot);
    }

    private static Report Pending() => Report.CreatePending(SyntheticReportFixtures.ReportId,
        SyntheticReportFixtures.Project, SyntheticReportFixtures.User, SyntheticReportFixtures.Time.AddMinutes(2), Data().Snapshot);
    private static ReportGenerationRequest Request(Report p) => new(p.ProjectId,p.Id,p.InputSnapshot.Plan.PlanId,
        p.InputSnapshot.Plan.RevisionId,p.InputSnapshot.Confirmation.ConfirmationId,p.InputSnapshot.Plan.GeometryHash);
    private sealed class Renderer : IPreliminaryReportRenderer
    {
        public int Calls; public bool Throws; public bool Invalid;
        public Task<byte[]> RenderAsync(ReportRenderData data, CancellationToken token)
        { Calls++; if(Throws) throw new InvalidOperationException("synthetic private error");
          return Task.FromResult(Invalid ? new byte[]{1,2} : "%PDF-1.4 synthetic test bytes"u8.ToArray()); }
    }
    private sealed class Boundary(Report p) : IReportGenerationBoundaryCandidate
    {
        public int AcceptCalls, PublishCalls, FailCalls;
        public ReportAcceptanceStatus Status = ReportAcceptanceStatus.Accepted;
        public bool BadHash, PublishThrows, AcceptThrows, FailReturns = true;
        public CancellationTokenSource? CancelOnPublish;
        public CancellationTokenSource? CancelOnFail;
        public Report? OverridePending; public CancellationTokenSource? CancelOnAccept;
        public Task<ReportAcceptance> AcceptAsync(Guid actor, ReportGenerationRequest request, CancellationToken token)
        { AcceptCalls++; if(AcceptThrows) throw new TimeoutException(); CancelOnAccept?.Cancel();
          return Task.FromResult(new ReportAcceptance(Status,Status==ReportAcceptanceStatus.Accepted ? OverridePending ?? p : null)); }
        public Task<ReportPublication> PublishAsync(Guid actor, Report pending, ReadOnlyMemory<byte> bytes,string hash,CancellationToken token)
        { PublishCalls++; if(PublishThrows) throw new TimeoutException(); Assert.Equal(ReportArtifactIntegrity.Hash(bytes.Span),hash); CancelOnPublish?.Cancel();
          return Task.FromResult(new ReportPublication(ReportPublicationStatus.Published,new ReportArtifact(SyntheticReportFixtures.ArtifactId,"application/pdf",bytes.Length,BadHash ? "invalid" : hash,SyntheticReportFixtures.Time.AddMinutes(3)))); }
        public Task<bool> FailAsync(Guid actor, Report pending,string code,CancellationToken token)
        { FailCalls++; Assert.Equal("REPORT_RENDER_FAILED",code); CancelOnFail?.Cancel(); return Task.FromResult(FailReturns); }
    }
    [Fact] public async Task AcceptedUsesExactSnapshotAndPublishesOnce()
    { var p=Pending(); var b=new Boundary(p); var r=new Renderer();
      Assert.Equal(ReportGenerationOutcome.Published,await new ReportGenerationCandidate(b,r).GenerateAsync(p.RequestedBy,Request(p)));
      Assert.Equal(1,b.AcceptCalls); Assert.Equal(1,r.Calls); Assert.Equal(1,b.PublishCalls); Assert.Equal(0,b.FailCalls); Assert.Equal(ReportStatus.Pending,p.Status); }
    [Fact] public async Task EmptyActorDoesNotCallBoundary()
    { var p=Pending(); var b=new Boundary(p); var r=new Renderer();
      Assert.Equal(ReportGenerationOutcome.Rejected,await new ReportGenerationCandidate(b,r).GenerateAsync(Guid.Empty,Request(p))); Assert.Equal(0,b.AcceptCalls); }
    [Theory] [InlineData(ReportAcceptanceStatus.Rejected,ReportGenerationOutcome.Rejected)] [InlineData(ReportAcceptanceStatus.Existing,ReportGenerationOutcome.Existing)] [InlineData(ReportAcceptanceStatus.Unavailable,ReportGenerationOutcome.Unavailable)] [InlineData(ReportAcceptanceStatus.Unknown,ReportGenerationOutcome.OutcomeUnknown)]
    public async Task NonAcceptanceNeverRenders(ReportAcceptanceStatus status,ReportGenerationOutcome expected)
    { var p=Pending(); var b=new Boundary(p){Status=status}; var r=new Renderer();
      Assert.Equal(expected,await new ReportGenerationCandidate(b,r).GenerateAsync(p.RequestedBy,Request(p))); Assert.Equal(0,r.Calls); Assert.Equal(0,b.PublishCalls); }
    [Fact] public async Task MismatchedActorReceiptNeverRenders()
    { var p=Pending(); var b=new Boundary(p); var r=new Renderer();
      Assert.Equal(ReportGenerationOutcome.OutcomeUnknown,await new ReportGenerationCandidate(b,r).GenerateAsync(Guid.NewGuid(),Request(p))); Assert.Equal(0,r.Calls); }
    [Fact] public async Task MismatchedRevisionReceiptNeverRenders()
    { var p=Pending(); var b=new Boundary(p); var r=new Renderer();
      Assert.Equal(ReportGenerationOutcome.OutcomeUnknown,await new ReportGenerationCandidate(b,r).GenerateAsync(p.RequestedBy,Request(p) with {PlanRevisionId=Guid.NewGuid()})); Assert.Equal(0,r.Calls); }
    [Theory] [InlineData(true,false)] [InlineData(false,true)]
    public async Task KnownRenderFailureNeverPublishes(bool throws,bool invalid)
    { var p=Pending(); var b=new Boundary(p); var r=new Renderer(){Throws=throws,Invalid=invalid};
      Assert.Equal(ReportGenerationOutcome.Failed,await new ReportGenerationCandidate(b,r).GenerateAsync(p.RequestedBy,Request(p))); Assert.Equal(1,b.FailCalls); Assert.Equal(0,b.PublishCalls); }
    [Theory] [InlineData(true,false)] [InlineData(false,true)]
    public async Task UncertainPublicationNeverRetriesOrFails(bool throws,bool hash)
    { var p=Pending(); var b=new Boundary(p){PublishThrows=throws,BadHash=hash}; var r=new Renderer();
      Assert.Equal(ReportGenerationOutcome.OutcomeUnknown,await new ReportGenerationCandidate(b,r).GenerateAsync(p.RequestedBy,Request(p))); Assert.Equal(1,b.PublishCalls); Assert.Equal(0,b.FailCalls); }
    [Fact] public async Task AcceptanceTimeoutIsUnknownWithoutRetry()
    { var p=Pending(); var b=new Boundary(p){AcceptThrows=true}; var r=new Renderer();
      Assert.Equal(ReportGenerationOutcome.OutcomeUnknown,await new ReportGenerationCandidate(b,r).GenerateAsync(p.RequestedBy,Request(p))); Assert.Equal(1,b.AcceptCalls); Assert.Equal(0,r.Calls); }
    [Fact] public async Task CancellationAfterDispatchIsUnknown()
    { using var c=new CancellationTokenSource(); var p=Pending(); var b=new Boundary(p){CancelOnAccept=c}; var r=new Renderer();
      Assert.Equal(ReportGenerationOutcome.OutcomeUnknown,await new ReportGenerationCandidate(b,r).GenerateAsync(p.RequestedBy,Request(p),c.Token)); Assert.Equal(0,r.Calls); }
    [Fact]
    public async Task CancellationAfterPublishDispatchNeverClaimsPublicationOrRetries()
    {
        using var cancellation = new CancellationTokenSource();
        var pending = Pending();
        var boundary = new Boundary(pending) { CancelOnPublish = cancellation };
        var renderer = new Renderer();

        var outcome = await new ReportGenerationCandidate(boundary, renderer)
            .GenerateAsync(pending.RequestedBy, Request(pending), cancellation.Token);

        Assert.Equal(ReportGenerationOutcome.OutcomeUnknown, outcome);
        Assert.Equal(1, boundary.AcceptCalls);
        Assert.Equal(1, renderer.Calls);
        Assert.Equal(1, boundary.PublishCalls);
        Assert.Equal(0, boundary.FailCalls);
    }

    [Fact]
    public async Task CancellationAfterFailedTransitionDispatchNeverClaimsRollback()
    {
        using var cancellation = new CancellationTokenSource();
        var pending = Pending();
        var boundary = new Boundary(pending) { CancelOnFail = cancellation };
        var renderer = new Renderer { Throws = true };

        var outcome = await new ReportGenerationCandidate(boundary, renderer)
            .GenerateAsync(pending.RequestedBy, Request(pending), cancellation.Token);

        Assert.Equal(ReportGenerationOutcome.OutcomeUnknown, outcome);
        Assert.Equal(1, boundary.AcceptCalls);
        Assert.Equal(1, renderer.Calls);
        Assert.Equal(0, boundary.PublishCalls);
        Assert.Equal(1, boundary.FailCalls);
    }

    [Fact] public async Task PreCancellationDoesNotDispatch()
    { using var c=new CancellationTokenSource(); c.Cancel(); var p=Pending(); var b=new Boundary(p);
      await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new ReportGenerationCandidate(b,new Renderer()).GenerateAsync(p.RequestedBy,Request(p),c.Token)); Assert.Equal(0,b.AcceptCalls); }
    [Fact] public async Task FailedTransitionNotAcknowledgedRemainsUnknown()
    { var p=Pending(); var b=new Boundary(p){FailReturns=false}; var r=new Renderer(){Throws=true};
      Assert.Equal(ReportGenerationOutcome.OutcomeUnknown,await new ReportGenerationCandidate(b,r).GenerateAsync(p.RequestedBy,Request(p))); Assert.Equal(0,b.PublishCalls); }
    [Fact] public async Task UnexpectedTerminalAcceptanceNeverRenders()
    {
        var p=Pending(); var b=new Boundary(p){OverridePending=p.Fail(1,"TEST_FAILED",SyntheticReportFixtures.Time.AddMinutes(3))}; var r=new Renderer();
        Assert.Equal(ReportGenerationOutcome.OutcomeUnknown,await new ReportGenerationCandidate(b,r).GenerateAsync(p.RequestedBy,Request(p)));
        Assert.Equal(0,r.Calls); Assert.Equal(0,b.PublishCalls);
    }
}
