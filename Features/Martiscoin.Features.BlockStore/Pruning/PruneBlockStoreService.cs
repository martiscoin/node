﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Martiscoin.AsyncWork;
using Martiscoin.Base;
using Martiscoin.Consensus.Chain;
using Martiscoin.Features.BlockStore.Repository;
using Martiscoin.Utilities;
using Microsoft.Extensions.Logging;

namespace Martiscoin.Features.BlockStore.Pruning
{
    /// <inheritdoc/>
    public sealed class PruneBlockStoreService : IPruneBlockStoreService
    {
        private IAsyncLoop asyncLoop;
        private readonly IAsyncProvider asyncProvider;
        private readonly IBlockRepository blockRepository;
        private readonly IChainState chainState;
        private readonly ILogger logger;
        private readonly INodeLifetime nodeLifetime;
        private readonly IPrunedBlockRepository prunedBlockRepository;
        private readonly StoreSettings storeSettings;

        /// <inheritdoc/>
        public ChainedHeader PrunedUpToHeaderTip { get; private set; }

        public PruneBlockStoreService(
            IAsyncProvider asyncProvider,
            IBlockRepository blockRepository,
            IPrunedBlockRepository prunedBlockRepository,
            IChainState chainState,
            ILoggerFactory loggerFactory,
            INodeLifetime nodeLifetime,
            StoreSettings storeSettings)
        {
            this.asyncProvider = asyncProvider;
            this.blockRepository = blockRepository;
            this.prunedBlockRepository = prunedBlockRepository;
            this.chainState = chainState;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.nodeLifetime = nodeLifetime;
            this.storeSettings = storeSettings;
        }

        /// <inheritdoc/>
        public void Initialize()
        {
            this.PrunedUpToHeaderTip = this.chainState.BlockStoreTip.GetAncestor(this.prunedBlockRepository.PrunedTip.Height);

            this.asyncLoop = this.asyncProvider.CreateAndRunAsyncLoop($"{this.GetType().Name}.{nameof(this.PruneBlocks)}", token =>
           {
               this.PruneBlocks();
               return Task.CompletedTask;
           },
            this.nodeLifetime.ApplicationStopping,
            repeatEvery: TimeSpans.Minute,
            startAfter: TimeSpans.Minute);
        }

        /// <inheritdoc/>
        public void PruneBlocks()
        {
            if (this.PrunedUpToHeaderTip == null)
                throw new BlockStoreException($"{nameof(this.PrunedUpToHeaderTip)} has not been set, please call initialize first.");

            if (this.blockRepository.TipHashAndHeight.Height < this.storeSettings.AmountOfBlocksToKeep)
            {
                this.logger.LogTrace("(-)[PRUNE_ABORTED_BLOCKSTORE_TIP_BELOW_AMOUNTOFBLOCKSTOKEEP]");
                return;
            }

            if (this.blockRepository.TipHashAndHeight.Height == this.PrunedUpToHeaderTip.Height)
            {
                this.logger.LogTrace("(-)[PRUNE_ABORTED_BLOCKSTORE_TIP_EQUALS_PRUNEDTIP]");
                return;
            }

            if (this.blockRepository.TipHashAndHeight.Height <= (this.PrunedUpToHeaderTip.Height + this.storeSettings.AmountOfBlocksToKeep))
            {
                this.logger.LogTrace("(-)[PRUNE_ABORTED_BLOCKSTORE_TIP_BELOW_OR_EQUAL_THRESHOLD]");
                return;
            }

            int heightToPruneFrom = this.blockRepository.TipHashAndHeight.Height - this.storeSettings.AmountOfBlocksToKeep;
            ChainedHeader startFrom = this.chainState.BlockStoreTip.GetAncestor(heightToPruneFrom);
            if (startFrom == null)
            {
                this.logger.LogInformation("(-)[PRUNE_ABORTED_START_BLOCK_NOT_FOUND]{0}:{1}", nameof(heightToPruneFrom), heightToPruneFrom);
                return;
            }

            this.logger.LogInformation("Pruning triggered, delete from {0} to {1}.", heightToPruneFrom, this.PrunedUpToHeaderTip.Height);

            var chainedHeadersToDelete = new List<ChainedHeader>();
            while (startFrom.Previous != null && this.PrunedUpToHeaderTip != startFrom)
            {
                chainedHeadersToDelete.Add(startFrom);
                startFrom = startFrom.Previous;
            }

            this.logger.LogDebug($"{chainedHeadersToDelete.Count} blocks will be pruned.");

            ChainedHeader prunedTip = chainedHeadersToDelete.First();

            this.blockRepository.DeleteBlocks(chainedHeadersToDelete.Select(c => c.HashBlock).ToList());
            this.prunedBlockRepository.UpdatePrunedTip(prunedTip);

            this.PrunedUpToHeaderTip = prunedTip;

            this.logger.LogInformation($"Store has been pruned up to {this.PrunedUpToHeaderTip.Height}.");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.asyncLoop?.Dispose();
        }
    }
}