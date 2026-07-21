using System.Net;
using Microsoft.Extensions.Options;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Tests.Fakes;

namespace OCAutomatica.Api.Tests.Epicor;

public class EpicorClientTests
{
    private sealed record Company(string Company1, string Name);
    private sealed record ODataList<T>(List<T> Value);

    private static EpicorClient BuildClient(FakeHttpMessageHandler handler)
    {
        var options = Options.Create(new EpicorOptions
        {
            BaseUrl = "https://epicor-test/erp102600v2",
            ApiKey = "test-api-key"
        });
        var httpClient = new HttpClient(handler);
        return new EpicorClient(httpClient, options);
    }

    [Fact]
    public async Task GetAsync_BuildsUrlWithCompanyAndPath()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK, """{"Value":[]}""");
        var client = BuildClient(handler);

        await client.GetAsync<ODataList<Company>>(
            "CFSJ_LAF",
            "Erp.BO.CompanySvc/Companies",
            new EpicorCredentials("user", "pass"));

        Assert.Equal(
            "https://epicor-test/erp102600v2/api/v2/odata/CFSJ_LAF/Erp.BO.CompanySvc/Companies",
            handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetAsync_SendsBasicAuthAndApiKey()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK, """{"Value":[]}""");
        var client = BuildClient(handler);

        await client.GetAsync<ODataList<Company>>(
            "CFSJ_LAF",
            "Erp.BO.CompanySvc/Companies",
            new EpicorCredentials("jyanez", "secreto"));

        var request = handler.LastRequest!;
        var expectedAuth = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("jyanez:secreto"));

        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal(expectedAuth, request.Headers.Authorization.Parameter);
        Assert.Equal("test-api-key", request.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task GetAsync_DeserializesResponse()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """{"Value":[{"Company1":"CFSJ_LAF","Name":"CARNES FINAS SAN JUAN LA FE"}]}""");
        var client = BuildClient(handler);

        var result = await client.GetAsync<ODataList<Company>>(
            "CFSJ_LAF",
            "Erp.BO.CompanySvc/Companies",
            new EpicorCredentials("user", "pass"));

        Assert.NotNull(result);
        Assert.Single(result!.Value);
        Assert.Equal("CFSJ_LAF", result.Value[0].Company1);
    }

    [Fact]
    public async Task GetAsync_ClassifiesInvalidCredentials()
    {
        // Real Epicor response body, captured against the test environment.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Unauthorized,
            """{"HttpStatus":401,"ReasonPhrase":"REST API Exception","ErrorMessage":"Invalid username or password.","ErrorType":"System.UnauthorizedAccessException","CorrelationId":"41893675-a27f-467a-be02-7af2ed7d708c"}""");
        var client = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<EpicorException>(() =>
            client.GetAsync<ODataList<Company>>(
                "CFSJ_LAF",
                "Erp.BO.VendorSvc/Vendors",
                new EpicorCredentials("user", "malapass")));

        Assert.Equal(EpicorErrorReason.InvalidCredentials, ex.Reason);
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task GetAsync_ClassifiesInvalidApiKey()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Unauthorized,
            """{"HttpStatus":401,"ReasonPhrase":"REST API Exception","ErrorMessage":"Invalid API Key llave-invalida.\r\nCompany CFSJ_LAF.","ErrorType":"System.UnauthorizedAccessException","CorrelationId":"96d27258-a13c-4b16-8a51-6ce0d28a5619"}""");
        var client = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<EpicorException>(() =>
            client.GetAsync<ODataList<Company>>(
                "CFSJ_LAF",
                "Erp.BO.VendorSvc/Vendors",
                new EpicorCredentials("user", "pass")));

        Assert.Equal(EpicorErrorReason.InvalidApiKey, ex.Reason);
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task GetAsync_ClassifiesAccessDenied()
    {
        // A user can authenticate successfully and still lack rights to a
        // specific Business Object (Epicor security, independent of the
        // password being correct). Must not be confused with bad credentials.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Unauthorized,
            """{"HttpStatus":401,"ReasonPhrase":"REST API Exception","ErrorMessage":"Access denied (Erp.BO.Company.GetRows).","ErrorType":"System.UnauthorizedAccessException","CorrelationId":"1d6f1747-acae-4391-9f75-b04f61229a6d"}""");
        var client = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<EpicorException>(() =>
            client.GetAsync<ODataList<Company>>(
                "CFSJ_LAF",
                "Erp.BO.CompanySvc/Companies",
                new EpicorCredentials("user", "correcta")));

        Assert.Equal(EpicorErrorReason.AccessDenied, ex.Reason);
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task GetAsync_ClassifiesUnrecognizedFailureAsOther()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.InternalServerError, "boom");
        var client = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<EpicorException>(() =>
            client.GetAsync<ODataList<Company>>(
                "CFSJ_LAF",
                "Erp.BO.CompanySvc/Companies",
                new EpicorCredentials("user", "pass")));

        Assert.Equal(EpicorErrorReason.Other, ex.Reason);
        Assert.Equal(500, ex.StatusCode);
    }
}
