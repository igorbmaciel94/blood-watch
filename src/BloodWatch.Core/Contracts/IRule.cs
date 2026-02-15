using BloodWatch.Core.Models;

namespace BloodWatch.Core.Contracts;

public interface IRule
{
    string RuleKey { get; }

    Task<IReadOnlyCollection<Event>> EvaluateAsync(
        Snapshot? previousSnapshot,
        Snapshot currentSnapshot,
        CancellationToken cancellationToken = default);
}
