﻿using Martiscoin.Consensus.BlockInfo;
using Martiscoin.Consensus.Chain;
using Martiscoin.Features.BlockStore.Pruning;
using Martiscoin.Features.BlockStore.Repository;
using Martiscoin.Networks;
using Martiscoin.Utilities;
using LevelDB;
using Microsoft.Extensions.Logging;

namespace Martiscoin.Features.BlockStore.Persistence.LevelDb

{
    /// <inheritdoc />
    public class LevelDbPrunedBlockRepository : IPrunedBlockRepository

    {
        private readonly IBlockRepository blockRepository;
        private readonly DataStoreSerializer dataStoreSerializer;
        private readonly ILogger logger;
        private static readonly byte[] prunedTipKey = new byte[2]; // the key of the index column
        private readonly StoreSettings storeSettings;
        private readonly Network network;

        /// <inheritdoc />
        public HashHeightPair PrunedTip { get; private set; }

        public LevelDbPrunedBlockRepository(IBlockRepository blockRepository, DataStoreSerializer dataStoreSerializer, ILoggerFactory loggerFactory, StoreSettings storeSettings, Network network)
        {
            this.blockRepository = blockRepository;

            this.dataStoreSerializer = dataStoreSerializer;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            this.storeSettings = storeSettings;
            this.network = network;
        }

        /// <inheritdoc />
        public void Initialize()
        {
            this.LoadPrunedTip((DB)this.blockRepository.DbInstance);
        }

        /// <inheritdoc />
        public void PrepareDatabase()
        {
            if (this.PrunedTip == null)
            {
                Block genesis = this.network.GetGenesis();

                this.PrunedTip = new HashHeightPair(genesis.GetHash(), 0);

                lock (this.blockRepository.Locker)
                {
                    ((DB)this.blockRepository.DbInstance).Put(DBH.Key(LevelDbBlockRepository.CommonTableName, prunedTipKey), this.dataStoreSerializer.Serialize(this.PrunedTip));
                }
            }

            return;
        }

        private void LoadPrunedTip(DB db)
        {
            if (this.PrunedTip == null)
            {
                lock (this.blockRepository.Locker)

                {
                    byte[] row = db.Get(DBH.Key(LevelDbBlockRepository.CommonTableName, prunedTipKey));
                    if (row != null)
                    {
                        this.PrunedTip = this.dataStoreSerializer.Deserialize<HashHeightPair>(row);
                    }
                }
            }
        }

        /// <inheritdoc />
        public void UpdatePrunedTip(ChainedHeader tip)
        {
            this.PrunedTip = new HashHeightPair(tip);

            lock (this.blockRepository.Locker)
            {
                ((DB)this.blockRepository.DbInstance).Put(DBH.Key(LevelDbBlockRepository.CommonTableName, prunedTipKey), this.dataStoreSerializer.Serialize(this.PrunedTip));
            }
        }
    }
}