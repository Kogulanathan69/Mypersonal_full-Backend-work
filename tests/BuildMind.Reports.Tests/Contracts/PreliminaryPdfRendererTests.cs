using System.Text;
using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.Reports.Contracts;
using BuildMind.Application.Features.Reports.Services;
using BuildMind.Infrastructure.Reports;

namespace BuildMind.Reports.Tests.Contracts;

public sealed class PreliminaryPdfRendererTests
{
    private static async Task SaveEvidence(string name, ReportRenderData data, byte[] bytes)
    {
        var root=Environment.GetEnvironmentVariable("BUILDMIND_SYNTHETIC_PDF_EVIDENCE");
        if(root is null) return;
        await File.WriteAllBytesAsync(Path.Combine(root,name+".pdf"),bytes);
        var s=data.Snapshot;
        var expected=new { references=new[]{s.ProjectId.ToString(),s.Plan.PlanId.ToString(),s.Plan.RevisionId.ToString(),s.Requirements.Id.ToString(),s.Plan.LandRevisionId.ToString(),s.Confirmation.ConfirmationId.ToString(),s.Plan.GeometryHash}, roomCount=data.Floors.Sum(f=>f.Rooms.Count), area=data.SumRoomAreaSquareMetres };
        await File.WriteAllTextAsync(Path.Combine(root,name+".expected.json"),JsonSerializer.Serialize(expected));
    }
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
    [Theory] [InlineData(1)] [InlineData(2)]
    public async Task ActualPdfPreservesSourceAndUnknownEvidence(int floors)
    {
        var data=Data(floors); var hash=data.Snapshot.Plan.GeometryHash;
        var bytes=await new PreliminaryPdfRenderer().RenderAsync(data,default);
        Assert.StartsWith("%PDF-",Encoding.ASCII.GetString(bytes,0,8));
        Assert.InRange(bytes.Length,1000,10*1024*1024);
        Assert.Equal(hash,data.Snapshot.Plan.GeometryHash);
        Assert.Equal("UNKNOWN",data.Snapshot.Plan.Validation.RegulatoryEvidenceStatus);
        Assert.Equal(12*floors,data.SumRoomAreaSquareMetres);
        await SaveEvidence($"synthetic-{floors}-floor",data,bytes);
    }
    [Theory] [InlineData("தமிழ்")] [InlineData("සිංහල")]
    public async Task UnsupportedShapingFailsWithoutCorruptingText(string name)
    {
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>new PreliminaryPdfRenderer().RenderAsync(Data(name:name),default));
        Assert.Equal("Preliminary report rendering could not be completed.",error.Message);
        Assert.Null(error.InnerException);
    }
    [Fact] public async Task CancellationIsPreserved()
    {
        using var source=new CancellationTokenSource();source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new PreliminaryPdfRenderer().RenderAsync(Data(),source.Token));
    }
    [Fact] public async Task ComplexityLimitFailsSafely()
    {
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>new PreliminaryPdfRenderer().RenderAsync(Data(21),default));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("21",error.Message);
    }
    [Fact] public async Task MaximumLengthAsciiNamePreservesSnapshot()
    {
        var data=Data(name:new string('A',198));
        var bytes=await new PreliminaryPdfRenderer().RenderAsync(data,default);
        Assert.True(bytes.Length>1000);
        Assert.Equal(200,data.Floors[0].Rooms[0].Measurement.Name.Length);
    }
    [Fact] public async Task MultiRoomPdfAndPageOutputLimits()
    {
        var data=Data(rooms:3);
        Assert.Equal(36,data.SumRoomAreaSquareMetres);
        var bytes=await new PreliminaryPdfRenderer(maximumPages:3).RenderAsync(data,default);
        Assert.True(bytes.Length>1000);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new PreliminaryPdfRenderer(maximumPages:2).RenderAsync(data,default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new PreliminaryPdfRenderer(maximumOutputBytes:1000).RenderAsync(data,default));
        await SaveEvidence("synthetic-multiroom",data,bytes);
    }
    [Fact] public async Task MaximumFloorCountIsAccepted()
    {
        var bytes=await new PreliminaryPdfRenderer().RenderAsync(Data(20),default);
        Assert.True(bytes.Length>1000);
    }
    [Fact] public async Task ConcaveRoomAndBothOpeningTypesRenderActualGeometry()
    {
        var data=Data(concave:true);
        Assert.Equal(6,data.SumRoomAreaSquareMetres);
        Assert.Equal(2,data.Floors[0].Openings.Count);
        var bytes=await new PreliminaryPdfRenderer().RenderAsync(data,default);
        Assert.StartsWith("%PDF-",Encoding.ASCII.GetString(bytes,0,8));
    }

    public sealed class IndependentPdfFactAttribute : FactAttribute
    {
        public IndependentPdfFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BUILDMIND_PDF_QA_PYTHON")))
                Skip = "Set BUILDMIND_PDF_QA_PYTHON to the approved Python executable with pypdf.";
        }
    }

    [IndependentPdfFact]
    public async Task IndependentParserVerifiesExactSyntheticReport()
    {
        var data = Data();
        var snapshot = data.Snapshot;
        var bytes = await new PreliminaryPdfRenderer(maximumPages: 3).RenderAsync(data, default);
        Assert.InRange(bytes.Length, 1000, 10 * 1024 * 1024);

        const string script = """
            import json, sys
            from io import BytesIO
            from pypdf import PdfReader
            from pypdf.errors import PdfReadError

            try:
                PdfReader(BytesIO(b"%PDF-1.7\n"), strict=True)
            except PdfReadError:
                pass
            else:
                raise AssertionError("Header-only PDF was accepted")

            reader = PdfReader(BytesIO(sys.stdin.buffer.read()), strict=True)
            print(json.dumps([
                [float(page.mediabox.width), float(page.mediabox.height), page.extract_text()]
                for page in reader.pages
            ]))
            """;
        var start = new System.Diagnostics.ProcessStartInfo(
            Environment.GetEnvironmentVariable("BUILDMIND_PDF_QA_PYTHON")!)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        foreach (var argument in new[] { "-I", "-B", "-c", script })
            start.ArgumentList.Add(argument);

        using var process = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the independent PDF parser.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.StandardInput.BaseStream.WriteAsync(bytes.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(output, error);
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(); }
                catch (InvalidOperationException) when (process.HasExited) { }
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        Assert.True(process.ExitCode == 0, await error);
        using var result = JsonDocument.Parse(await output);
        var pages = result.RootElement;
        Assert.Equal(3, pages.GetArrayLength());
        foreach (var page in pages.EnumerateArray())
        {
            Assert.InRange(page[0].GetDouble(), 594d, 596d);
            Assert.InRange(page[1].GetDouble(), 841d, 843d);
        }

        static string Normalize(string text) =>
            System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

        var cover = Normalize(pages[0][2].GetString()!);
        Assert.Contains($"Project: {snapshot.ProjectId}", cover);
        Assert.Contains($"Requirements revision: {snapshot.Requirements.Id}", cover);
        Assert.Contains($"Land revision: {snapshot.Plan.LandRevisionId}", cover);
        Assert.Contains($"Plan: {snapshot.Plan.PlanId}", cover);
        Assert.Contains($"Plan revision: {snapshot.Plan.RevisionId} / version {snapshot.Plan.VersionNumber}", cover);
        Assert.Contains($"Confirmation: {snapshot.Confirmation.ConfirmationId}", cover);
        var hash = System.Text.RegularExpressions.Regex.Match(cover, @"Geometry hash:\s*(.*?)\s*Snapshot UTC:");
        Assert.True(hash.Success);
        Assert.Equal(snapshot.Plan.GeometryHash,
            System.Text.RegularExpressions.Regex.Replace(hash.Groups[1].Value, @"\s+", ""));

        var evidence = Normalize(pages[2][2].GetString()!);
        Assert.Contains($"Validation: {snapshot.Plan.Validation.ValidationId}; validator: test-validator", evidence);
        Assert.Contains("Geometry valid: True; blocking issues: False", evidence);
        Assert.Contains("Requirements match: UNKNOWN", evidence);
        Assert.Contains("Regulatory evidence: UNKNOWN", evidence);
        Assert.Contains("Warnings acknowledged: True", evidence);
        Assert.Contains($"Disclaimer version: {snapshot.Disclaimer.Version} {snapshot.Disclaimer.Text}", evidence);
    }
}
