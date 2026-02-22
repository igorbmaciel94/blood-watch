namespace BloodWatch.Api.Services;

public sealed record ServiceError(
    int StatusCode,
    string Title,
    string Detail,
    string Type);

public sealed class ServiceResult
{
    private ServiceResult(ServiceError? error)
    {
        Error = error;
    }

    public ServiceError? Error { get; }

    public bool IsSuccess => Error is null;

    public static ServiceResult Success() => new(error: null);

    public static ServiceResult Failure(ServiceError error) => new(error);

    public static ServiceResult Failure(int statusCode, string title, string detail)
        => new(new ServiceError(statusCode, title, detail, $"https://httpstatuses.com/{statusCode}"));
}

public sealed class ServiceResult<T>
{
    private ServiceResult(T? value, ServiceError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public ServiceError? Error { get; }

    public bool IsSuccess => Error is null;

    public static ServiceResult<T> Success(T value) => new(value, error: null);

    public static ServiceResult<T> Failure(ServiceError error) => new(default, error);

    public static ServiceResult<T> Failure(int statusCode, string title, string detail)
        => new(default, new ServiceError(statusCode, title, detail, $"https://httpstatuses.com/{statusCode}"));
}
