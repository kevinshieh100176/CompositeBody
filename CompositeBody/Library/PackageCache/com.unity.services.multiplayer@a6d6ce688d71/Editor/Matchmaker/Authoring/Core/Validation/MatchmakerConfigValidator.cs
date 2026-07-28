using System.Collections.Generic;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Validation
{
    /// <summary>
    /// Validates matchmaker queue and pool configuration, including Multiplay Cloud Code fields.
    /// </summary>
    /// <remarks>
    /// Used by the deploy handler to block pushing configs that lack required Cloud Code fields.
    /// Fetch does not use this validator; configs without Cloud Code fields can be pulled and edited locally.
    /// </remarks>
    static class MatchmakerConfigValidator
    {
        /// <summary>
        /// Gets a validation error message if any Multiplay pool in the queue is missing required Cloud Code fields.
        /// </summary>
        /// <param name="queueConfig">The queue configuration to validate. Can be <c>null</c>.</param>
        /// <returns>
        /// An error message describing the first invalid pool, or <c>null</c> if the config is valid or <paramref name="queueConfig"/> is <c>null</c>.
        /// </returns>
        /// <remarks>
        /// For Multiplay hosting, all of <c>moduleName</c>, <c>allocateFunctionName</c>, and <c>pollFunctionName</c> must be non-empty and non-null.
        /// Configs without these fields can be fetched and edited locally but cannot be deployed (pushed).
        /// </remarks>
        public static string GetMultiplayCloudCodeValidationError(QueueConfig queueConfig)
        {
            if (queueConfig == null)
                return null;

            foreach (var pool in EnumerateAllPools(queueConfig))
            {
                if (pool.MatchHosting is MultiplayConfig multiplay &&
                    (string.IsNullOrEmpty(multiplay.ModuleName) ||
                     string.IsNullOrEmpty(multiplay.AllocateFunctionName) ||
                     string.IsNullOrEmpty(multiplay.PollFunctionName)))
                {
                    var poolName = pool.Name?.ToString() ?? "unknown";
                    var queueName = queueConfig.Name?.ToString() ?? "unknown";
                    return $"Multiplay hosting for queue '{queueName}', pool '{poolName}' must specify all of: moduleName, allocateFunctionName, and pollFunctionName.";
                }
            }

            return null;
        }

        /// <summary>
        /// Enumerates all pools defined in a queue config, including the default pool, its variants, and each filtered pool and its variants.
        /// </summary>
        /// <param name="queueConfig">The queue configuration to enumerate. Must not be <c>null</c>.</param>
        /// <returns>A list of all <see cref="PoolConfig"/> instances in the queue; never <c>null</c>.</returns>
        public static List<PoolConfig> EnumerateAllPools(QueueConfig queueConfig)
        {
            var pools = new List<PoolConfig>();

            if (queueConfig.DefaultPool != null)
            {
                pools.Add(queueConfig.DefaultPool);
                if (queueConfig.DefaultPool.Variants != null)
                    pools.AddRange(queueConfig.DefaultPool.Variants);
            }

            if (queueConfig.FilteredPools == null)
                return pools;

            foreach (var fp in queueConfig.FilteredPools)
            {
                pools.Add(fp);
                if (fp.Variants != null)
                    pools.AddRange(fp.Variants);
            }

            return pools;
        }
    }
}
