﻿using Martiscoin.Consensus.BlockInfo;
using Martiscoin.Consensus.Chain;
using Martiscoin.NBitcoin;

namespace Martiscoin.Features.Consensus
{
    /// <summary>
    /// An interface to read and write PoS information from store.
    /// </summary>
    public interface IStakeChain
    {
        /// <summary>
        /// Get the stake coresponding to the block.
        /// </summary>
        BlockStake Get(uint256 blockid);

        /// <summary>
        /// Set the stake for the given block header.
        /// </summary>
        void Set(ChainedHeader chainedHeader, BlockStake blockStake);

        /// <summary>
        /// Initialize the stake store.
        /// </summary>
        void Load();
    }
}