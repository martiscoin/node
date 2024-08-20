﻿using XOuranos.Consensus.Rules;
using XOuranos.Features.Consensus.CoinViews;
using XOuranos.Utilities;

namespace XOuranos.Features.Consensus.Rules.UtxosetRules
{
    /// <summary>
    /// Rules that provide easy access to the <see cref="CoinView"/> which is the store for a PoW system.
    /// </summary>
    public abstract class UtxoStoreConsensusRule : FullValidationConsensusRule
    {
        /// <summary>Allow access to the POS parent.</summary>
        protected PowConsensusRuleEngine PowParent;

        protected CoinviewHelper coinviewHelper;

        /// <inheritdoc />
        public override void Initialize()
        {
            this.PowParent = this.Parent as PowConsensusRuleEngine;
            Guard.NotNull(this.PowParent, nameof(this.PowParent));

            this.coinviewHelper = new CoinviewHelper();
        }
    }
}