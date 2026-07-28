using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Fetch;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model
{
    class PoolUpdateResult
    {
        public bool DryRun { get; set; }
        public List<PoolName> Created { get; } = new();

        public List<PoolName> Updated { get; } = new();

        public List<PoolName> Deleted { get; } = new();


        internal static PoolUpdateResult GetPoolDeployResultFromQueues(QueueConfig originalQueue, QueueConfig updatedQueue, IDeepEqualityComparer deepEqualityComparer, bool dryRun)
        {
            var poolDeployResult = new PoolUpdateResult();
            poolDeployResult.DryRun = dryRun;
            if (!deepEqualityComparer.IsDeepEqual(originalQueue.DefaultPool, updatedQueue.DefaultPool))
            {
                if (originalQueue.DefaultPool == null)
                {
                    poolDeployResult.Created.AddRange(GetAllPoolNamesFromPool(updatedQueue.DefaultPool));
                }
                else if (updatedQueue.DefaultPool == null)
                {
                    poolDeployResult.Deleted.AddRange(GetAllPoolNamesFromPool(originalQueue.DefaultPool));
                }
                else
                {
                    // Copy bases pools into normal pool to remove variants so that we know if the pool itself has changed
                    var originalDefaultPoolCopy = new PoolConfig(originalQueue.DefaultPool);
                    var updatedDefaultPoolCopy = new PoolConfig(updatedQueue.DefaultPool);
                    if (!deepEqualityComparer.IsDeepEqual(originalDefaultPoolCopy, updatedDefaultPoolCopy))
                    {
                        poolDeployResult.Updated.Add(updatedQueue.DefaultPool?.Name ?? originalQueue.DefaultPool?.Name);
                    }
                    var(variantsPoolsCreated, variantsPoolsUpdated, variantsPoolsDeleted) = GetVariantPoolsDiff(originalQueue.DefaultPool, updatedQueue.DefaultPool, deepEqualityComparer);
                    poolDeployResult.Created.AddRange(variantsPoolsCreated);
                    poolDeployResult.Updated.AddRange(variantsPoolsUpdated);
                    poolDeployResult.Deleted.AddRange(variantsPoolsDeleted);
                }
            }

            foreach (var updatedFilteredPool in updatedQueue.FilteredPools)
            {
                var originalFilteredPool = originalQueue.FilteredPools.Find(op => op.Name.Equals(updatedFilteredPool.Name));
                if (originalFilteredPool == null)
                {
                    poolDeployResult.Created.AddRange(GetAllPoolNamesFromPool(updatedFilteredPool));
                }
                else
                {
                    // Copy bases pools into normal pool to remove variants so that we know if the pool itself has changed
                    var originalDefaultPoolCopy = new PoolConfig(originalFilteredPool);
                    var updatedDefaultPoolCopy = new PoolConfig(updatedFilteredPool);
                    if (!deepEqualityComparer.IsDeepEqual(originalDefaultPoolCopy, updatedDefaultPoolCopy))
                    {
                        poolDeployResult.Updated.Add(updatedDefaultPoolCopy.Name);
                    }

                    var(variantsPoolsCreated, variantsPoolsUpdated, variantsPoolsDeleted) = GetVariantPoolsDiff(
                        originalFilteredPool,
                        updatedFilteredPool, deepEqualityComparer);
                    poolDeployResult.Created.AddRange(variantsPoolsCreated);
                    poolDeployResult.Updated.AddRange(variantsPoolsUpdated);
                    poolDeployResult.Deleted.AddRange(variantsPoolsDeleted);
                }
            }

            var deletedFilteredPools = originalQueue.FilteredPools
                .Where(op => updatedQueue.FilteredPools.All(p => !p.Name.Equals(op.Name)))
                .SelectMany(GetAllPoolNamesFromPool).ToList();
            poolDeployResult.Deleted.AddRange(deletedFilteredPools);

            return poolDeployResult;
        }

        static  (List<PoolName> poolsCreated, List<PoolName> poolsUpdated, List<PoolName> poolsDeleted)
        GetVariantPoolsDiff(BasePoolConfig originalPool,
            BasePoolConfig updatedPool,
            IDeepEqualityComparer deepEqualityComparer)
        {
            var createdVariants = updatedPool.Variants
                .Where(v => originalPool.Variants.All(ov => !ov.Name.Equals(v.Name)))
                .Select(v => v.Name).ToList();
            var updatedVariants = updatedPool.Variants
                .Where(v => !createdVariants.Contains(v.Name))
                .Where(v => !deepEqualityComparer.IsDeepEqual(v,
                    originalPool.Variants.Find(o => o.Name.Equals(v.Name))))
                .Select(v => v.Name).ToList();
            var deletedVariants = originalPool.Variants.Where(ov => updatedPool.Variants.All(v => !v.Name.Equals(ov.Name)))
                .Select(ov => ov.Name).ToList();

            return (createdVariants, updatedVariants, deletedVariants);
        }

        static List<PoolName> GetAllPoolNamesFromPool(BasePoolConfig poolConfig)
        {
            var pollNames = new List<PoolName>();
            if (poolConfig == null)
                return pollNames;
            pollNames.Add(poolConfig.Name);
            pollNames.AddRange(poolConfig.Variants.Select(v => v.Name));
            return pollNames;
        }

        public override string ToString()
        {
            if (Created.Count + Updated.Count + Deleted.Count == 0)
            {
                return String.Empty;
            }

            return $"The following pools {(DryRun ? "will be" : "have been")}" +
                $"{(Created.Count == 0 ? "" : $" created: {string.Join(", ", Created)}.")}" +
                $"{(Updated.Count == 0 ? "" : $" updated: {string.Join(", ", Updated)}.")}" +
                $"{(Deleted.Count == 0 ? "" : $" deleted: {string.Join(", ", Deleted)}.")}";
        }
    }
}
