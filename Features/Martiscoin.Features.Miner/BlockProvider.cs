﻿using System.Collections.Generic;
using System.Linq;
using Martiscoin.Consensus.BlockInfo;
using Martiscoin.Consensus.Chain;
using Martiscoin.Consensus.ScriptInfo;
using Martiscoin.Mining;
using Martiscoin.Networks;

namespace Martiscoin.Features.Miner
{
    /// <inheritdoc/>
    public sealed class BlockProvider : IBlockProvider
    {
        private readonly Network network;

        /// <summary>Defines how proof of work blocks are built.</summary>
        private readonly PowBlockDefinition powBlockDefinition;

        /// <summary>Defines how proof of stake blocks are built.</summary>
        private readonly PosBlockDefinition posBlockDefinition;

        /// <summary>Defines how proof of work blocks are built on a Proof-of-Stake network.</summary>
        private readonly PosPowBlockDefinition posPowBlockDefinition;

        /// <param name="definitions">A list of block definitions that the builder can utilize.</param>
        public BlockProvider(Network network, IEnumerable<BlockDefinition> definitions)
        {
            this.network = network;

            this.powBlockDefinition = definitions.OfType<PowBlockDefinition>().FirstOrDefault();
            this.posBlockDefinition = definitions.OfType<PosBlockDefinition>().FirstOrDefault();
            this.posPowBlockDefinition = definitions.OfType<PosPowBlockDefinition>().FirstOrDefault();
        }

        /// <inheritdoc/>
        public BlockTemplate BuildPosBlock(ChainedHeader chainTip, Script script)
        {
            return this.posBlockDefinition.Build(chainTip, script);
        }

        /// <inheritdoc/>
        public BlockTemplate BuildPowBlock(ChainedHeader chainTip, Script script)
        {
            if (this.network.Consensus.IsProofOfStake)
                return this.posPowBlockDefinition.Build(chainTip, script);

            return this.powBlockDefinition.Build(chainTip, script);
        }

        /// <inheritdoc/>
        public void BlockModified(ChainedHeader chainTip, Block block)
        {
            if (this.network.Consensus.IsProofOfStake)
            {
                if (BlockStake.IsProofOfStake(block))
                {
                    this.posBlockDefinition.BlockModified(chainTip, block);
                }
                else
                {
                    this.posPowBlockDefinition.BlockModified(chainTip, block);
                }
            }

            this.powBlockDefinition.BlockModified(chainTip, block);
        }

    }
}