using System.Text.Json.Serialization;

namespace HARAnalyzer.Models;

public class HarFile
{
    [JsonPropertyName("log")]
    public HarLog Log { get; set; } = new();
}

public class HarLog
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("creator")]
    public HarCreator? Creator { get; set; }

    [JsonPropertyName("pages")]
    public List<HarPage> Pages { get; set; } = [];

    [JsonPropertyName("entries")]
    public List<HarEntry> Entries { get; set; } = [];
}

public class HarCreator
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
}

public class HarPage
{
    [JsonPropertyName("startedDateTime")]
    public string StartedDateTime { get; set; } = "";

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
}

public class HarEntry
{
    [JsonPropertyName("startedDateTime")]
    public string StartedDateTime { get; set; } = "";

    [JsonPropertyName("time")]
    public double Time { get; set; }

    [JsonPropertyName("request")]
    public HarRequest Request { get; set; } = new();

    [JsonPropertyName("response")]
    public HarResponse Response { get; set; } = new();

    [JsonPropertyName("timings")]
    public HarTimings Timings { get; set; } = new();

    [JsonPropertyName("serverIPAddress")]
    public string? ServerIPAddress { get; set; }

    [JsonPropertyName("pageref")]
    public string? Pageref { get; set; }
}

public class HarRequest
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("httpVersion")]
    public string HttpVersion { get; set; } = "";

    [JsonPropertyName("headers")]
    public List<HarHeader> Headers { get; set; } = [];

    [JsonPropertyName("queryString")]
    public List<HarHeader> QueryString { get; set; } = [];

    [JsonPropertyName("headersSize")]
    public long HeadersSize { get; set; }

    [JsonPropertyName("bodySize")]
    public long BodySize { get; set; }

    [JsonPropertyName("postData")]
    public HarPostData? PostData { get; set; }
}

public class HarPostData
{
    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "";

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

public class HarResponse
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("statusText")]
    public string StatusText { get; set; } = "";

    [JsonPropertyName("headers")]
    public List<HarHeader> Headers { get; set; } = [];

    [JsonPropertyName("content")]
    public HarContent Content { get; set; } = new();

    [JsonPropertyName("redirectURL")]
    public string RedirectURL { get; set; } = "";

    [JsonPropertyName("headersSize")]
    public long HeadersSize { get; set; }

    [JsonPropertyName("bodySize")]
    public long BodySize { get; set; }
}

public class HarContent
{
    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "";

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("encoding")]
    public string? Encoding { get; set; }
}

public class HarTimings
{
    [JsonPropertyName("blocked")]
    public double Blocked { get; set; }

    [JsonPropertyName("dns")]
    public double Dns { get; set; }

    [JsonPropertyName("connect")]
    public double Connect { get; set; }

    [JsonPropertyName("ssl")]
    public double Ssl { get; set; }

    [JsonPropertyName("send")]
    public double Send { get; set; }

    [JsonPropertyName("wait")]
    public double Wait { get; set; }

    [JsonPropertyName("receive")]
    public double Receive { get; set; }
}

public class HarHeader
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
}
