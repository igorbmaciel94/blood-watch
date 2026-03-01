namespace BloodWatch.Copilot;

public sealed class LLMClientException : Exception
{
    public LLMClientException(string message, bool isTransient)
        : base(message)
    {
        IsTransient = isTransient;
    }

    public LLMClientException(string message, bool isTransient, Exception innerException)
        : base(message, innerException)
    {
        IsTransient = isTransient;
    }

    public bool IsTransient { get; }
}
