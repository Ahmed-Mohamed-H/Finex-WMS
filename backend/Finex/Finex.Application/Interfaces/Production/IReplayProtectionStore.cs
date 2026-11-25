namespace Finex.Application.Interfaces.Production
{
    public interface IReplayProtectionStore
    {
        Task<bool> TryConsumeNonceAsync(string nonceKey, TimeSpan ttl, CancellationToken cancellationToken = default);
    }
}