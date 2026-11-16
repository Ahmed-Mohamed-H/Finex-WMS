using Finex.Application.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using ZiggyCreatures.Caching.Fusion;

namespace Finex.Infrastructure.Security
{
    public sealed class FusionCacheReplayProtectionStore : IReplayProtectionStore
    {
        private readonly IFusionCache _fusionCache;

        public FusionCacheReplayProtectionStore(IFusionCache fusionCache)
        {
            _fusionCache = fusionCache ?? throw new ArgumentNullException(nameof(fusionCache));
        }

        public async Task<bool> TryConsumeNonceAsync(
            string nonceKey,
            TimeSpan ttl,
            CancellationToken cancellationToken = default)
        {
            string cacheKey = $"hmac_nonce:{nonceKey}";
            bool isFirstRequest = false;

            // FusionCache Stampede Protection يضمن تنفيذ الـ Factory لطلب واحد فقط
            // حتى لو وصل طلبان متطابقان في نفس المايكروثانية
            await _fusionCache.GetOrSetAsync<byte>(
                key: cacheKey,
                factory: _ =>
                {
                    isFirstRequest = true;
                    return Task.FromResult((byte)1);
                },
                options => options
                    .SetDuration(ttl)
                    .SetFailSafe(false),
                token: cancellationToken);

            return isFirstRequest;
        }
    }
}
