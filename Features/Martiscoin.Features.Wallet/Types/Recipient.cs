﻿using Martiscoin.Consensus.ScriptInfo;
using Martiscoin.NBitcoin;

namespace Martiscoin.Features.Wallet.Types
{
    /// <summary>
    /// Represents recipients of a payment, used in <see cref="WalletTransactionHandler.BuildTransaction"/>.
    /// </summary>
    public class Recipient
    {
        /// <summary>
        /// The destination script.
        /// </summary>
        public Script ScriptPubKey { get; set; }

        /// <summary>
        /// The amount that will be sent.
        /// </summary>
        public Money Amount { get; set; }

        /// <summary>
        /// An indicator if the fee is subtracted from the current recipient.
        /// </summary>
        public bool SubtractFeeFromAmount { get; set; }
    }
}
