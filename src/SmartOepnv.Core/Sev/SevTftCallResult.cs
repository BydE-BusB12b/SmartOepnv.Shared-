namespace SmartOepnv.Core.Sev;

public sealed class SevTftCallResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public int? StatusCode { get; init; }

    public static SevTftCallResult Ok(string message, int? statusCode = null) =>
        new()
        {
            Success = true,
            Message = message,
            StatusCode = statusCode
        };

    public static SevTftCallResult Fail(string message, int? statusCode = null) =>
        new()
        {
            Success = false,
            Message = message,
            StatusCode = statusCode
        };
}
