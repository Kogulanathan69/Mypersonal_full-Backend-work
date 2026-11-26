using BuildMind.Infrastructure.Reports;

namespace BuildMind.Infrastructure.Tests.Reports;

public sealed class ReportOutputBoundaryTests
{
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ExactOutputLimitThenOneByteOverIsRejected(int writeMode)
    {
        using var stream=new PreliminaryPdfRenderer.BoundedOutput(4);
        if(writeMode==0) stream.Write(new byte[4],0,4);
        else if(writeMode==1) stream.Write(new byte[4].AsSpan());
        else for(var i=0;i<4;i++) stream.WriteByte(0);
        Assert.Equal(4,stream.Length);
        Assert.Throws<InvalidOperationException>(()=>stream.WriteByte(0));
        Assert.Throws<InvalidOperationException>(()=>stream.SetLength(5));
        Assert.Equal(4,stream.Length);
    }
    [Fact] public void CancellationAfterOutputBeginsIsPreserved()
    {
        using var cts=new CancellationTokenSource();
        using var stream=new PreliminaryPdfRenderer.BoundedOutput(4,cts.Token);
        stream.WriteByte(0);cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>stream.WriteByte(0));
        Assert.ThrowsAny<OperationCanceledException>(()=>stream.SetLength(2));
        Assert.Equal(1,stream.Length);
    }
}
