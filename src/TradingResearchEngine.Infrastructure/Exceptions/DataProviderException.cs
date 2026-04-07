namespace TradingResearchEngine.Infrastructure.Exceptions;

/// <summary>Thrown when an HTTP data provider receives a non-2xx response.</summary>
public sealed class DataProviderException : Exception
{
    /// <summary>HTTP status code from the response.</summary>
    public int StatusCode { get; }

    /// <summary>Response body text.</summary>
    public string ResponseBody { get; }

    /// <inheritdoc cref="DataProviderException"/>
    public DataProviderException(int statusCode, string responseBody)
        : base($"Data provider returned HTTP {statusCode}: {responseBody}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
