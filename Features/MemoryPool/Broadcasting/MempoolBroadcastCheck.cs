﻿using System.Threading.Tasks;
using XOuranos.Consensus.TransactionInfo;
using XOuranos.Features.MemoryPool.Interfaces;
using XOuranos.Interfaces;
using XOuranos.Utilities;

namespace XOuranos.Features.MemoryPool.Broadcasting
{
    /// <summary>
    /// Adds the transaction to the mempool and if it failed validation retun the error code.
    /// </summary>
    public class MempoolBroadcastCheck : IBroadcastCheck
    {
        private readonly IMempoolValidator mempoolValidator;

        public MempoolBroadcastCheck(IMempoolValidator mempoolValidator)
        {
            Guard.NotNull(mempoolValidator, nameof(mempoolValidator));

            this.mempoolValidator = mempoolValidator;
        }

        public async Task<string> CheckTransaction(Transaction transaction)
        {
            Guard.NotNull(transaction, nameof(transaction));

            var state = new MempoolValidationState(false);

            if (!await this.mempoolValidator.AcceptToMemoryPool(state, transaction).ConfigureAwait(false))
            {
                return $"{state.Error.ConsensusError?.Message ?? state.Error.Code ?? "Failed"}";
            }

            return string.Empty;
        }
    }
}