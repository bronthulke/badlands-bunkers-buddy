using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace BadlandsBunkers.Tests;

/// <summary>
/// Minimal stand-in for a Functions HTTP request. Only the pieces our extension methods
/// actually touch are implemented; everything else throws so we notice if that changes.
/// </summary>
public class FakeHttpRequestData : HttpRequestData
{
    public FakeHttpRequestData(string? cookieHeader = null, string body = "")
        : base(new FakeFunctionContext())
    {
        Headers = new HttpHeadersCollection();
        if (cookieHeader is not null) Headers.Add("Cookie", cookieHeader);
        Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
    }

    /// <summary>Models a client that sent several separate Cookie headers.</summary>
    public FakeHttpRequestData(IEnumerable<string> cookieHeaders)
        : base(new FakeFunctionContext())
    {
        Headers = new HttpHeadersCollection();
        Headers.Add("Cookie", cookieHeaders);
        Body = new MemoryStream();
    }

    public override Stream Body { get; }
    public override HttpHeadersCollection Headers { get; }
    public override IReadOnlyCollection<IHttpCookie> Cookies => Array.Empty<IHttpCookie>();
    public override Uri Url => new("http://localhost/api/test");
    public override IEnumerable<ClaimsIdentity> Identities => Array.Empty<ClaimsIdentity>();
    public override string Method => "GET";

    public override HttpResponseData CreateResponse() => new FakeHttpResponseData(FunctionContext);
}

public class FakeHttpResponseData : HttpResponseData
{
    public FakeHttpResponseData(FunctionContext context) : base(context)
    {
        Headers = new HttpHeadersCollection();
        Body = new MemoryStream();
    }

    public override HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public override HttpHeadersCollection Headers { get; set; }
    public override Stream Body { get; set; }
    public override HttpCookies Cookies => throw new NotSupportedException();

    public string ReadBodyAsString()
    {
        Body.Position = 0;
        return new StreamReader(Body).ReadToEnd();
    }
}

public class FakeFunctionContext : FunctionContext
{
    public override string InvocationId => "test-invocation";
    public override string FunctionId => "test-function";
    public override TraceContext TraceContext => throw new NotSupportedException();
    public override BindingContext BindingContext => throw new NotSupportedException();
    public override RetryContext RetryContext => throw new NotSupportedException();
    public override IServiceProvider InstanceServices { get; set; } = null!;
    public override FunctionDefinition FunctionDefinition => throw new NotSupportedException();
    public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
    public override IInvocationFeatures Features => throw new NotSupportedException();
}
