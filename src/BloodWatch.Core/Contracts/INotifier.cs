using BloodWatch.Core.Models;

namespace BloodWatch.Core.Contracts;

public interface INotifier
{
    string TypeKey { get; }

    Task<Delivery> SendAsync(Event @event, string target, CancellationToken cancellationToken = default);
}
