﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Martiscoin.Consensus.ScriptInfo;
using Martiscoin.Consensus.TransactionInfo;
using Martiscoin.Features.Wallet.Exceptions;
using Martiscoin.Features.Wallet.Interfaces;
using Martiscoin.Features.Wallet.Types;
using Martiscoin.NBitcoin;
using Martiscoin.NBitcoin.BIP32;
using Martiscoin.NBitcoin.Policy;
using Martiscoin.Networks;
using Martiscoin.Utilities;
using Microsoft.Extensions.Logging;

namespace Martiscoin.Features.Wallet
{
    /// <summary>
    /// A handler that has various functionalities related to transaction operations.
    /// </summary>
    /// <seealso cref="IWalletTransactionHandler" />
    /// <remarks>
    /// This will uses the <see cref="IWalletFeePolicy" /> and the <see cref="TransactionBuilder" />.
    /// TODO: Move also the broadcast transaction to this class
    /// TODO: Implement lockUnspents
    /// TODO: Implement subtractFeeFromOutputs
    /// </remarks>
    public class WalletTransactionHandler : IWalletTransactionHandler
    {
        /// <summary>
        /// We will assume that we're never going to have a fee over 1 STRAT.
        /// </summary>
        private static readonly Money PretendMaxFee = Money.Coins(1);

        private readonly ILogger logger;

        private readonly Network network;

        protected readonly StandardTransactionPolicy TransactionPolicy;

        private readonly IWalletManager walletManager;

        private readonly IWalletFeePolicy walletFeePolicy;

        public WalletTransactionHandler(
            ILoggerFactory loggerFactory,
            IWalletManager walletManager,
            IWalletFeePolicy walletFeePolicy,
            Network network,
            StandardTransactionPolicy transactionPolicy)
        {
            this.network = network;
            this.walletManager = walletManager;
            this.walletFeePolicy = walletFeePolicy;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            this.TransactionPolicy = transactionPolicy;
        }

        /// <inheritdoc />
        public Transaction BuildTransaction(TransactionBuildContext context)
        {
            this.InitializeTransactionBuilder(context);

            const int maxRetries = 5;
            int retryCount = 0;

            TransactionPolicyError[] errors = null;
            while (retryCount <= maxRetries)
            {
                if (context.Shuffle)
                    context.TransactionBuilder.Shuffle();

                Transaction transaction = context.TransactionBuilder.BuildTransaction(false);
                if (context.Sign)
                {
                    ICoin[] coinsSpent = context.TransactionBuilder.FindSpentCoins(transaction);
                    // TODO: Improve this as we already have secrets when running a retry iteration.
                    this.AddSecrets(context, coinsSpent);
                    context.TransactionBuilder.SignTransactionInPlace(transaction);
                }

                if (context.TransactionBuilder.Verify(transaction, out errors))
                    return transaction;

                // Retry only if error is of type 'FeeTooLowPolicyError'
                if (!errors.Any(e => e is FeeTooLowPolicyError)) break;

                retryCount++;
            }

            string errorsMessage = string.Join(" - ", errors.Select(s => s.ToString()));
            this.logger.LogError($"Build transaction failed: {errorsMessage}");
            throw new WalletException($"Could not build the transaction. Details: {errorsMessage}");
        }

        /// <inheritdoc />
        public void FundTransaction(TransactionBuildContext context, Transaction transaction)
        {
            if (context.Recipients.Any())
                throw new WalletException("Adding outputs is not allowed.");

            // Turn the txout set into a Recipient array
            context.Recipients.AddRange(transaction.Outputs
                .Select(s => new Recipient
                {
                    ScriptPubKey = s.ScriptPubKey,
                    Amount = s.Value,
                    SubtractFeeFromAmount = false // default for now
                }));

            context.AllowOtherInputs = true;

            foreach (TxIn transactionInput in transaction.Inputs)
                context.SelectedInputs.Add(transactionInput.PrevOut);

            Transaction newTransaction = this.BuildTransaction(context);

            if (context.ChangeAddress != null)
            {
                // find the position of the change and move it over.
                int index = 0;
                foreach (TxOut newTransactionOutput in newTransaction.Outputs)
                {
                    if (newTransactionOutput.ScriptPubKey == context.ChangeAddress.ScriptPubKey)
                    {
                        transaction.Outputs.Insert(index, newTransactionOutput);
                    }

                    index++;
                }
            }

            // TODO: copy the new output amount size (this also includes spreading the fee over all outputs)

            // copy all the inputs from the new transaction.
            foreach (TxIn newTransactionInput in newTransaction.Inputs)
            {
                if (!context.SelectedInputs.Contains(newTransactionInput.PrevOut))
                {
                    transaction.Inputs.Add(newTransactionInput);

                    // TODO: build a mechanism to lock inputs
                }
            }
        }

        /// <inheritdoc />
        public (Money maximumSpendableAmount, Money Fee) GetMaximumSpendableAmount(WalletAccountReference accountReference, FeeType feeType, bool allowUnconfirmed)
        {
            Guard.NotNull(accountReference, nameof(accountReference));
            Guard.NotEmpty(accountReference.WalletName, nameof(accountReference.WalletName));
            Guard.NotEmpty(accountReference.AccountName, nameof(accountReference.AccountName));

            // Get the total value of spendable coins in the account.
            long maxSpendableAmount = this.walletManager.GetSpendableTransactionsInAccount(accountReference, allowUnconfirmed ? 0 : 1).Sum(x => x.Transaction.Amount);

            // Return 0 if the user has nothing to spend.
            if (maxSpendableAmount == Money.Zero)
            {
                return (Money.Zero, Money.Zero);
            }

            // Create a recipient with a dummy destination address as it's required by NBitcoin's transaction builder.
            List<Recipient> recipients = new[] { new Recipient { Amount = new Money(maxSpendableAmount), ScriptPubKey = new Key().ScriptPubKey } }.ToList();
            Money fee = Money.Zero;

            try
            {
                // Here we try to create a transaction that contains all the spendable coins, leaving no room for the fee.
                // When the transaction builder throws an exception informing us that we have insufficient funds,
                // we use the amount we're missing as the fee.
                var context = new TransactionBuildContext(this.network)
                {
                    FeeType = feeType,
                    MinConfirmations = allowUnconfirmed ? 0 : 1,
                    Recipients = recipients,
                    AccountReference = accountReference
                };

                this.AddRecipients(context);
                this.AddCoins(context);
                this.AddFee(context);

                if (this.network.MinTxFee > Money.Zero)
                {
                    // Throw an exception if this code is reached, as building a transaction without any funds for the fee should always throw an exception.
                    throw new WalletException("This should be unreachable; please find and fix the bug that caused this to be reached.");
                }
            }
            catch (NotEnoughFundsException e)
            {
                fee = (Money)e.Missing;
            }

            return (maxSpendableAmount - fee, fee);
        }

        /// <inheritdoc />
        public Money EstimateFee(TransactionBuildContext context)
        {
            this.InitializeTransactionBuilder(context);

            return context.TransactionFee;
        }

        /// <summary>
        /// Initializes the context transaction builder from information in <see cref="TransactionBuildContext"/>.
        /// </summary>
        /// <param name="context">Transaction build context.</param>
        protected virtual void InitializeTransactionBuilder(TransactionBuildContext context)
        {
            Guard.NotNull(context, nameof(context));
            Guard.NotNull(context.Recipients, nameof(context.Recipients));
            Guard.NotNull(context.AccountReference, nameof(context.AccountReference));

            context.TransactionBuilder.CoinSelector = new DefaultCoinSelector
            {
                GroupByScriptPubKey = false
            };

            context.TransactionBuilder.DustPrevention = false;

            // If inputs are selected by the user, we just choose them all.
            if (context.SelectedInputs != null && context.SelectedInputs.Any())
            {
                context.TransactionBuilder.CoinSelector = new AllCoinsSelector();
            }

            this.AddRecipients(context);
            this.AddOpReturnOutput(context);
            this.AddCoins(context);
            this.FindChangeAddress(context);
            this.AddFee(context);

            if (context.Time.HasValue)
                context.TransactionBuilder.SetTimeStamp(context.Time.Value);
        }

        /// <summary>
        /// Loads all the private keys for each of the <see cref="HdAddress"/> in <see cref="TransactionBuildContext.UnspentOutputs"/>
        /// </summary>
        /// <param name="context">The context associated with the current transaction being built.</param>
        /// <param name="coinsSpent">The coins spent to generate the transaction.</param>
        protected void AddSecrets(TransactionBuildContext context, IEnumerable<ICoin> coinsSpent)
        {
            if (!context.Sign)
                return;

            Types.Wallet wallet = this.walletManager.GetWalletByName(context.AccountReference.WalletName);
            ExtKey seedExtKey = this.walletManager.GetExtKey(context.AccountReference, context.WalletPassword, context.CacheSecret);

            var signingKeys = new HashSet<ISecret>();
            var added = new HashSet<HdAddress>();
            foreach (Coin coinSpent in coinsSpent)
            {
                //obtain the address relative to this coin (must be improved)
                HdAddress address = context.UnspentOutputs.First(output => output.ToOutPoint() == coinSpent.Outpoint).Address;

                if (added.Contains(address))
                    continue;

                ExtKey addressExtKey = seedExtKey.Derive(new KeyPath(address.HdPath));
                BitcoinExtKey addressPrivateKey = addressExtKey.GetWif(wallet.Network);
                signingKeys.Add(addressPrivateKey);
                added.Add(address);
            }

            context.TransactionBuilder.AddKeys(signingKeys.ToArray());
        }

        /// <summary>
        /// Find the next available change address.
        /// </summary>
        /// <param name="context">The context associated with the current transaction being built.</param>
        protected void FindChangeAddress(TransactionBuildContext context)
        {
            if (context.ChangeAddress == null)
            {
                // If no change address is supplied, get a new address to send the change to.
                context.ChangeAddress = this.walletManager.GetUnusedChangeAddress(new WalletAccountReference(context.AccountReference.WalletName, context.AccountReference.AccountName));
            }

            context.TransactionBuilder.SetChange(context.ChangeAddress.ScriptPubKey);
        }

        /// <summary>
        /// Find all available outputs (UTXO's) that belong to <see cref="WalletAccountReference.AccountName"/>.
        /// Then add them to the <see cref="TransactionBuildContext.UnspentOutputs"/>.
        /// </summary>
        /// <param name="context">The context associated with the current transaction being built.</param>
        protected void AddCoins(TransactionBuildContext context)
        {
            context.UnspentOutputs = this.walletManager.GetSpendableTransactionsInAccount(context.AccountReference, context.MinConfirmations).ToList();

            if (context.UnspentOutputs.Count == 0)
            {
                throw new WalletException("No spendable transactions found.");
            }

            // Get total spendable balance in the account.
            long balance = context.UnspentOutputs.Sum(t => t.Transaction.Amount);
            long totalToSend = context.Recipients.Sum(s => s.Amount) + (context.OpReturnAmount ?? Money.Zero);
            if (balance < totalToSend)
                throw new WalletException("Not enough funds.");

            Money sum = 0;
            var coins = new List<Coin>();

            if (context.SelectedInputs != null && context.SelectedInputs.Any())
            {
                // 'SelectedInputs' are inputs that must be included in the
                // current transaction. At this point we check the given
                // input is part of the UTXO set and filter out UTXOs that are not
                // in the initial list if 'context.AllowOtherInputs' is false.

                Dictionary<OutPoint, UnspentOutputReference> availableHashList = context.UnspentOutputs.ToDictionary(item => item.ToOutPoint(), item => item);

                if (!context.SelectedInputs.All(input => availableHashList.ContainsKey(input)))
                    throw new WalletException("Not all the selected inputs were found on the wallet.");

                if (!context.AllowOtherInputs)
                {
                    foreach (KeyValuePair<OutPoint, UnspentOutputReference> unspentOutputsItem in availableHashList)
                    {
                        if (!context.SelectedInputs.Contains(unspentOutputsItem.Key))
                            context.UnspentOutputs.Remove(unspentOutputsItem.Value);
                    }
                }

                foreach (OutPoint outPoint in context.SelectedInputs)
                {
                    UnspentOutputReference item = availableHashList[outPoint];

                    coins.Add(new Coin(item.Transaction.Id, (uint)item.Transaction.Index, item.Transaction.Amount, item.Transaction.ScriptPubKey));
                    sum += item.Transaction.Amount;
                }
            }

            foreach (UnspentOutputReference item in context.UnspentOutputs
                .OrderByDescending(a => a.Confirmations > 0)
                .ThenByDescending(a => a.Transaction.Amount))
            {
                if (context.SelectedInputs?.Contains(item.ToOutPoint()) ?? false)
                    continue;

                // If the total value is above the target
                // then it's safe to stop adding UTXOs to the coin list.
                // The primary goal is to reduce the time it takes to build a trx
                // when the wallet is bloated with UTXOs.

                // Get to our total, and then check that we're a little bit over to account for tx fees.
                // If it gets over totalToSend but doesn't hit this break, that's fine too.
                // The TransactionBuilder will have a go with what we give it, and throw NotEnoughFundsException accurately if it needs to.
                if (sum > totalToSend + PretendMaxFee)
                    break;

                coins.Add(new Coin(item.Transaction.Id, (uint)item.Transaction.Index, item.Transaction.Amount, item.Transaction.ScriptPubKey));
                sum += item.Transaction.Amount;
            }

            // All the UTXOs are added to the builder without filtering.
            // The builder then has its own coin selection mechanism
            // to select the best UTXO set for the corresponding amount.
            // To add a custom implementation of a coin selection override
            // the builder using builder.SetCoinSelection().

            context.TransactionBuilder.AddCoins(coins);
        }

        /// <summary>
        /// Add recipients to the <see cref="TransactionBuilder"/>.
        /// </summary>
        /// <param name="context">The context associated with the current transaction being built.</param>
        /// <remarks>
        /// Add outputs to the <see cref="TransactionBuilder"/> based on the <see cref="Recipient"/> list.
        /// </remarks>
        protected virtual void AddRecipients(TransactionBuildContext context)
        {
            if (context.Recipients.Any(a => a.Amount == Money.Zero))
                throw new WalletException("No amount specified.");

            if (context.Recipients.Any(a => a.SubtractFeeFromAmount))
                throw new NotImplementedException("Substracting the fee from the recipient is not supported yet.");

            foreach (Recipient recipient in context.Recipients)
                context.TransactionBuilder.Send(recipient.ScriptPubKey, recipient.Amount);
        }

        /// <summary>
        /// Use the <see cref="FeeRate"/> from the <see cref="walletFeePolicy"/>.
        /// </summary>
        /// <param name="context">The context associated with the current transaction being built.</param>
        protected void AddFee(TransactionBuildContext context)
        {
            Money fee;
            Money minTrxFee = new Money(this.network.MinTxFee, MoneyUnit.Satoshi);

            // If the fee hasn't been set manually, calculate it based on the fee type that was chosen.
            if (context.TransactionFee == null)
            {
                FeeRate feeRate = context.OverrideFeeRate ?? this.walletFeePolicy.GetFeeRate(context.FeeType.ToConfirmations());
                fee = context.TransactionBuilder.EstimateFees(feeRate);

                // Make sure that the fee is at least the minimum transaction fee.
                fee = Math.Max(fee, minTrxFee);
            }
            else
            {
                if (context.TransactionFee < minTrxFee)
                {
                    throw new WalletException($"Not enough fees. The minimun fee is {minTrxFee}.");
                }

                fee = context.TransactionFee;
            }

            context.TransactionBuilder.SendFees(fee);
            context.TransactionFee = fee;
        }

        /// <summary>
        /// Add extra unspendable output to the transaction if there is anything in OpReturnData.
        /// </summary>
        /// <param name="context">The context associated with the current transaction being built.</param>
        protected void AddOpReturnOutput(TransactionBuildContext context)
        {
            if (string.IsNullOrEmpty(context.OpReturnData) && context.OpReturnRawData == null)
                return;

            byte[] bytes = context.OpReturnRawData ?? Encoding.UTF8.GetBytes(context.OpReturnData);

            // TODO: Get the template from the network standard scripts instead
            Script opReturnScript = TxNullDataTemplate.Instance.GenerateScriptPubKey(bytes);
            context.TransactionBuilder.Send(opReturnScript, context.OpReturnAmount ?? Money.Zero);
        }
    }

    public class TransactionBuildContext
    {
        /// <summary>
        /// Initialize a new instance of a <see cref="TransactionBuildContext"/>
        /// </summary>
        /// <param name="network">The network for which this transaction will be built.</param>
        public TransactionBuildContext(Network network)
        {
            this.TransactionBuilder = new TransactionBuilder(network);
            this.Recipients = new List<Recipient>();
            this.WalletPassword = string.Empty;
            this.FeeType = FeeType.Medium;
            this.MinConfirmations = (int)network.Consensus.CoinbaseMaturity;
            this.SelectedInputs = new List<OutPoint>();
            this.AllowOtherInputs = false;
            this.Sign = true;
            this.CacheSecret = true;
        }

        /// <summary>
        /// The wallet account to use for building a transaction.
        /// </summary>
        public WalletAccountReference AccountReference { get; set; }

        /// <summary>
        /// The recipients to send Bitcoin to.
        /// </summary>
        public List<Recipient> Recipients { get; set; }

        /// <summary>
        /// An indicator to estimate how much fee to spend on a transaction.
        /// </summary>
        /// <remarks>
        /// The higher the fee the faster a transaction will get in to a block.
        /// </remarks>
        public FeeType FeeType { get; set; }

        /// <summary>
        /// The minimum number of confirmations an output must have to be included as an input.
        /// </summary>
        public int MinConfirmations { get; set; }

        /// <summary>
        /// Coins that are available to be spent.
        /// </summary>
        public List<UnspentOutputReference> UnspentOutputs { get; set; }

        /// <summary>
        /// The builder used to build the current transaction.
        /// </summary>
        public readonly TransactionBuilder TransactionBuilder;

        /// <summary>
        /// The change address, where any remaining funds will be sent to.
        /// </summary>
        /// <remarks>
        /// A Bitcoin has to spend the entire UTXO, if total value is greater then the send target
        /// the rest of the coins go in to a change address that is under the senders control.
        /// </remarks>
        public HdAddress ChangeAddress { get; set; }

        /// <summary>
        /// The total fee on the transaction.
        /// </summary>
        public Money TransactionFee { get; set; }

        /// <summary>
        /// The password that protects the wallet in <see cref="WalletAccountReference"/>.
        /// </summary>
        /// <remarks>
        /// TODO: replace this with System.Security.SecureString (https://github.com/dotnet/corefx/tree/master/src/System.Security.SecureString)
        /// More info (https://github.com/dotnet/corefx/issues/1387)
        /// </remarks>
        public string WalletPassword { get; set; }

        /// <summary>
        /// The inputs that must be used when building the transaction.
        /// </summary>
        /// <remarks>
        /// The inputs are required to be part of the wallet.
        /// </remarks>
        public List<OutPoint> SelectedInputs { get; set; }

        /// <summary>
        /// If false, allows unselected inputs, but requires all selected inputs be used.
        /// </summary>
        public bool AllowOtherInputs { get; set; }

        /// <summary>
        /// Allows the context to specify a <see cref="FeeRate"/> when building a transaction.
        /// </summary>
        public FeeRate OverrideFeeRate { get; set; }

        /// <summary>
        /// Shuffles transaction inputs and outputs for increased privacy.
        /// </summary>
        public bool Shuffle { get; set; }

        /// <summary>
        /// Optional data to be added as an extra OP_RETURN transaction output.
        /// </summary>
        public string OpReturnData { get; set; }

        /// <summary>
        /// The raw data to be added as an extra OP_RETURN transaction output, this will take precedence over <see cref="OpReturnData"/>.
        /// </summary>
        public byte[] OpReturnRawData { get; set; }

        /// <summary>
        /// Optional amount to add to the OP_RETURN transaction output.
        /// </summary>
        public Money OpReturnAmount { get; set; }

        /// <summary>
        /// Whether the transaction should be signed or not.
        /// </summary>
        public bool Sign { get; set; }

        /// <summary>
        /// Whether the secret should be cached for 5 mins after it is used or not.
        /// </summary>
        public bool CacheSecret { get; set; }

        /// <summary>
        /// The timestamp to set on the transaction.
        /// </summary>
        public uint? Time { get; set; }
    }
}