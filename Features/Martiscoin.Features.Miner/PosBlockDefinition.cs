﻿using Martiscoin.Base.Deployments;
using Martiscoin.Consensus;
using Martiscoin.Consensus.BlockInfo;
using Martiscoin.Consensus.Chain;
using Martiscoin.Consensus.ScriptInfo;
using Martiscoin.Features.Consensus;
using Martiscoin.Features.Consensus.Interfaces;
using Martiscoin.Features.Consensus.Rules.CommonRules;
using Martiscoin.Features.MemoryPool;
using Martiscoin.Features.MemoryPool.Interfaces;
using Martiscoin.Mining;
using Martiscoin.NBitcoin;
using Martiscoin.Networks;
using Martiscoin.Utilities;
using Microsoft.Extensions.Logging;

namespace Martiscoin.Features.Miner
{
    public class PosBlockDefinition : BlockDefinition
    {
        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>Database of stake related data for the current blockchain.</summary>
        private readonly IStakeChain stakeChain;

        /// <summary>Provides functionality for checking validity of PoS blocks.</summary>
        private readonly IStakeValidator stakeValidator;

        /// <summary>
        /// The POS rule to determine the allowed drift in time between nodes.
        /// </summary>
        private PosFutureDriftRule futureDriftRule;

        public PosBlockDefinition(
            IConsensusManager consensusManager,
            IDateTimeProvider dateTimeProvider,
            ILoggerFactory loggerFactory,
            ITxMempool mempool,
            MempoolSchedulerLock mempoolLock,
            MinerSettings minerSettings,
            Network network,
            IStakeChain stakeChain,
            IStakeValidator stakeValidator,
            NodeDeployments nodeDeployments)
            : base(consensusManager, dateTimeProvider, loggerFactory, mempool, mempoolLock, minerSettings, network, nodeDeployments)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.stakeChain = stakeChain;
            this.stakeValidator = stakeValidator;
        }

        /// <inheritdoc/>
        public override void AddToBlock(TxMempoolEntry mempoolEntry)
        {
            this.AddTransactionToBlock(mempoolEntry.Transaction);
            this.UpdateBlockStatistics(mempoolEntry);
            this.UpdateTotalFees(mempoolEntry.Fee);
        }

        /// <inheritdoc/>
        public override BlockTemplate Build(ChainedHeader chainTip, Script scriptPubKey)
        {
            this.OnBuild(chainTip, scriptPubKey);

            this.coinbase.Outputs[0].ScriptPubKey = new Script();
            this.coinbase.Outputs[0].Value = Money.Zero;

            return this.BlockTemplate;
        }

        /// <inheritdoc/>
        public override void UpdateHeaders()
        {
            base.UpdateBaseHeaders();

            this.block.Header.Bits = this.stakeValidator.GetNextTargetRequired(this.stakeChain, this.ChainTip, this.Network.Consensus, true);
        }

        /// <inheritdoc/>
        protected override bool TestPackage(TxMempoolEntry entry, long packageSize, long packageSigOpsCost)
        {
            if (this.futureDriftRule == null)
                this.futureDriftRule = this.ConsensusManager.ConsensusRules.GetRule<PosFutureDriftRule>();

            if (entry.Transaction is IPosTransactionWithTime posTrx)
            {
                long adjustedTime = this.DateTimeProvider.GetAdjustedTimeAsUnixTimestamp();

                if (posTrx.Time > adjustedTime + this.futureDriftRule.GetFutureDrift(adjustedTime))
                    return false;

                if (posTrx.Time > ((PosTransaction)this.block.Transactions[0]).Time)
                    return false;
            }

            return base.TestPackage(entry, packageSize, packageSigOpsCost);
        }
    }
}