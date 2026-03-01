namespace BloodWatch.Api.Copilot;

public interface ICopilotFeatureFlagState
{
    bool IsEnabled { get; }

    DateTime UpdatedAtUtc { get; }

    void SetEnabled(bool enabled);
}
