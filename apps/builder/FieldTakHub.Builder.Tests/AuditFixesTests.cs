using System;
using System.Net;
using FieldTakHub.Builder.Services;
using Xunit;

namespace FieldTakHub.Builder.Tests;

/// <summary>Testy poprawek z audytu kodu Buildera (docs/AUDYT_KODU_BUILDERA.md).</summary>
public class AuditFixesTests
{
    [Fact]
    public void LanAddress_PrivateRangesFollowRfc1918()
    {
        Assert.True(LanAddressHelper.IsPrivateIPv4(IPAddress.Parse("10.0.0.5")));
        Assert.True(LanAddressHelper.IsPrivateIPv4(IPAddress.Parse("172.16.0.1")));
        Assert.True(LanAddressHelper.IsPrivateIPv4(IPAddress.Parse("172.31.255.254")));
        Assert.True(LanAddressHelper.IsPrivateIPv4(IPAddress.Parse("192.168.137.1")));
        Assert.False(LanAddressHelper.IsPrivateIPv4(IPAddress.Parse("172.32.0.1")));
        Assert.False(LanAddressHelper.IsPrivateIPv4(IPAddress.Parse("169.254.10.10")));
        Assert.False(LanAddressHelper.IsPrivateIPv4(IPAddress.Parse("8.8.8.8")));
        Assert.False(LanAddressHelper.IsPrivateIPv4(IPAddress.Parse("127.0.0.1")));
    }

    [Fact]
    public void LanAddress_WarnsOnlyWhenQrPointsAtLoopback()
    {
        Assert.NotNull(LanAddressHelper.DescribePhoneReachabilityProblem("http://127.0.0.1:8766/deploy/BLUFOR.ftak"));
        Assert.NotNull(LanAddressHelper.DescribePhoneReachabilityProblem(""));
        Assert.Null(LanAddressHelper.DescribePhoneReachabilityProblem("http://192.168.137.1:8766/deploy/BLUFOR.ftak"));
    }

    [Fact]
    public void LanHttp_RoutesOnlyExactPrefixAndExtension()
    {
        Assert.Equal("BLUFOR", LanHttp.ResourceName("/deploy/BLUFOR.ftak", "/deploy/", ".ftak"));
        Assert.Equal("Strona 2", LanHttp.ResourceName("/opord/Strona 2.html", "/opord/", ".html", ".json"));
        Assert.Null(LanHttp.ResourceName("/deploy/BLUFOR.ftak.exe", "/deploy/", ".ftak"));
        Assert.Null(LanHttp.ResourceName("/opord/BLUFOR.html", "/deploy/", ".ftak"));
        Assert.Null(LanHttp.ResourceName("/deploy/.ftak", "/deploy/", ".ftak"));
        Assert.Null(LanHttp.ResourceName("/", "/deploy/", ".ftak"));
    }

    [Fact]
    public void LanHttp_DecodesPathAndDropsQuery()
    {
        Assert.Equal("/deploy/Strona 2.ftak", LanHttp.DecodeRequestPath("GET /deploy/Strona%202.ftak?t=1 HTTP/1.1"));
        Assert.Equal("/", LanHttp.DecodeRequestPath("GET"));
    }

    [Fact]
    public void UsbProvisioning_RejectsMissingOrMalformedPsk()
    {
        Assert.NotNull(NativeMeshtasticSerialEngine.DescribePskProblem(null));
        Assert.NotNull(NativeMeshtasticSerialEngine.DescribePskProblem(""));
        Assert.NotNull(NativeMeshtasticSerialEngine.DescribePskProblem("to nie jest base64!"));
        Assert.NotNull(NativeMeshtasticSerialEngine.DescribePskProblem(Convert.ToBase64String(new byte[] { 1 })));
        Assert.Null(NativeMeshtasticSerialEngine.DescribePskProblem(Convert.ToBase64String(new byte[16])));
        Assert.Null(NativeMeshtasticSerialEngine.DescribePskProblem(Convert.ToBase64String(new byte[32])));
    }

    [Fact]
    public void Qr_IsEncodedLocallyAndFallsBackToLowerCorrectionForLongPayloads()
    {
        using (var small = QrService.CreateDataFitting("http://192.168.137.1:8766/deploy/BLUFOR.ftak"))
        {
            Assert.True(small.ModuleMatrix.Count >= 21);
        }

        // ~2000 bajtów nie mieści się z korekcją Q, ale mieści się z M – kod ma powstać, a nie zniknąć po cichu.
        using (var large = QrService.CreateDataFitting(new string('a', 2000)))
        {
            Assert.True(large.ModuleMatrix.Count > 100);
        }

        Assert.Throws<InvalidOperationException>(() => QrService.CreateDataFitting(new string('a', 4000)));
    }
}
