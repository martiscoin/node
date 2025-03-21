﻿using System;
using System.Collections.Generic;
using System.Linq;
using Martiscoin.Consensus.Chain;
using Martiscoin.Consensus.ScriptInfo;
using Martiscoin.Consensus.TransactionInfo;
using Martiscoin.Features.Wallet.Database;
using Martiscoin.Features.Wallet.Exceptions;
using Martiscoin.Features.Wallet.Helpers;
using Martiscoin.NBitcoin;
using Martiscoin.NBitcoin.BIP32;
using Martiscoin.Networks;
using Martiscoin.Utilities;
using Martiscoin.Utilities.JsonConverters;
using Newtonsoft.Json;

namespace Martiscoin.Features.Wallet.Types
{
    /// <summary>
    /// A wallet.
    /// </summary>
    public class Wallet
    {
        /// <summary>Default pattern for accounts in the wallet. The first account will be called 'account 0', then 'account 1' and so on.</summary>
        public const string AccountNamePattern = "account {0}";

        /// <summary>Account numbers greater or equal to this number are reserved for special purpose account indexes.</summary>
        public const int SpecialPurposeAccountIndexesStart = 100_000_000;

        /// <summary>Filter for identifying normal wallet accounts.</summary>
        public static Func<HdAccount, bool> NormalAccounts = a => a.Index < SpecialPurposeAccountIndexesStart;

        /// <summary>Filter for all wallet accounts.</summary>
        public static Func<HdAccount, bool> AllAccounts = a => true;

        /// <summary>
        /// Initializes a new instance of the wallet.
        /// </summary>
        public Wallet()
        {
            this.AccountsRoot = new List<AccountRoot>();
        }

        [JsonIgnore]
        public IWalletStore walletStore { get; set; }

        /// <summary>
        /// The wallet version.
        /// </summary>
        [JsonProperty(PropertyName = "version")]
        public int Version { get; set; }

        /// <summary>
        /// The name of this wallet.
        /// </summary>
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        /// <summary>
        /// Flag indicating if it is a watch only wallet.
        /// </summary>
        [JsonProperty(PropertyName = "isExtPubKeyWallet")]
        public bool IsExtPubKeyWallet { get; set; }

        /// <summary>
        /// The seed for this wallet, password encrypted.
        /// </summary>
        [JsonProperty(PropertyName = "encryptedSeed", NullValueHandling = NullValueHandling.Ignore)]
        public string EncryptedSeed { get; set; }

        /// <summary>
        /// The chain code.
        /// </summary>
        [JsonProperty(PropertyName = "chainCode", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(ByteArrayConverter))]
        public byte[] ChainCode { get; set; }

        /// <summary>
        /// Gets or sets the merkle path.
        /// </summary>
        //[JsonProperty(PropertyName = "blockLocator", ItemConverterType = typeof(UInt256JsonConverter))]
        [JsonIgnore]
        public ICollection<uint256> BlockLocator { get; set; }

        /// <summary>
        /// The network this wallet is for.
        /// </summary>
        [JsonProperty(PropertyName = "network")]
        [JsonConverter(typeof(NetworkConverter))]
        public Network Network { get; set; }

        /// <summary>
        /// The time this wallet was created.
        /// </summary>
        [JsonProperty(PropertyName = "creationTime")]
        [JsonConverter(typeof(DateTimeOffsetConverter))]
        public DateTimeOffset CreationTime { get; set; }

        /// <summary>
        /// The root of the accounts tree.
        /// </summary>
        [JsonProperty(PropertyName = "accountsRoot")]
        public ICollection<AccountRoot> AccountsRoot { get; set; }

        /// <summary>
        /// Gets the accounts in the wallet.
        /// </summary>
        /// <param name="accountFilter">An optional filter for filtering the accounts being returned.</param>
        /// <returns>The accounts in the wallet.</returns>
        public IEnumerable<HdAccount> GetAccounts(Func<HdAccount, bool> accountFilter = null)
        {
            return this.AccountsRoot.SelectMany(a => a.Accounts).Where(accountFilter ?? NormalAccounts);
        }

        /// <summary>
        /// Gets an account from the wallet's accounts.
        /// </summary>
        /// <param name="accountName">The name of the account to retrieve.</param>
        /// <returns>The requested account or <c>null</c> if the account does not exist.</returns>
        public HdAccount GetAccount(string accountName)
        {
            return this.AccountsRoot.SingleOrDefault()?.GetAccountByName(accountName);
        }

        /// <summary>
        /// Gets an account from the wallet's accounts.
        /// </summary>
        /// <param name="index">The index of the account to retrieve.</param>
        /// <returns>The requested account or <c>null</c> if the account does not exist.</returns>
        public HdAccount GetAccount(int index)
        {
            return this.AccountsRoot.SingleOrDefault()?.GetAccountByIndex(index);
        }

        /// <summary>
        /// Update the last block synced height and hash in the wallet.
        /// </summary>
        /// <param name="block">The block whose details are used to update the wallet.</param>
        public void SetLastBlockDetails(ChainedHeader block)
        {
            AccountRoot accountRoot = this.AccountsRoot.SingleOrDefault();

            if (accountRoot == null)
            {
                return;
            }

            accountRoot.LastBlockSyncedHeight = block.Height;
            accountRoot.LastBlockSyncedHash = block.HashBlock;
        }

        /// <summary>
        /// Gets all the transactions in the wallet.
        /// </summary>
        /// <returns>A list of all the transactions in the wallet.</returns>
        public IEnumerable<TransactionOutputData> GetAllTransactions(Func<HdAccount, bool> accountFilter = null)
        {
            List<HdAccount> accounts = this.GetAccounts(accountFilter).ToList();

            // First we iterate normal accounts
            foreach (TransactionOutputData txData in accounts.Where(a => a.IsNormalAccount()).SelectMany(x => x.ExternalAddresses).SelectMany(x => this.walletStore.GetForAddress(x.Address)))
            {
                // If this is a cold coin stake UTXO, we won't return it for a normal account.
                if (txData.IsColdCoinStake.HasValue && txData.IsColdCoinStake.Value == true)
                {
                    continue;
                }

                yield return txData;
            }

            foreach (TransactionOutputData txData in accounts.Where(a => a.IsNormalAccount()).SelectMany(x => x.InternalAddresses).SelectMany(x => this.walletStore.GetForAddress(x.Address)))
            {
                // If this is a cold coin stake UTXO, we won't return it for a normal account.
                if (txData.IsColdCoinStake.HasValue && txData.IsColdCoinStake.Value == true)
                {
                    continue;
                }

                yield return txData;
            }

            // Then we iterate special accounts.
            foreach (TransactionOutputData txData in accounts.Where(a => !a.IsNormalAccount()).SelectMany(x => x.ExternalAddresses).SelectMany(x => this.walletStore.GetForAddress(x.Address)))
            {
                yield return txData;
            }

            foreach (TransactionOutputData txData in accounts.Where(a => !a.IsNormalAccount()).SelectMany(x => x.InternalAddresses).SelectMany(x => this.walletStore.GetForAddress(x.Address)))
            {
                yield return txData;
            }
        }

        /// <summary>
        /// Gets all the pub keys contained in this wallet.
        /// </summary>
        /// <returns>A list of all the public keys contained in the wallet.</returns>
        public IEnumerable<Script> GetAllPubKeys()
        {
            List<HdAccount> accounts = this.GetAccounts().ToList();

            foreach (Script script in accounts.SelectMany(x => x.ExternalAddresses).Select(x => x.ScriptPubKey))
            {
                yield return script;
            }

            foreach (Script script in accounts.SelectMany(x => x.InternalAddresses).Select(x => x.ScriptPubKey))
            {
                yield return script;
            }
        }

        /// <summary>
        /// Gets all the addresses contained in this wallet.
        /// </summary>
        /// <param name="accountFilter">An optional filter for filtering the accounts being returned.</param>
        /// <returns>A list of all the addresses contained in this wallet.</returns>
        public IEnumerable<HdAddress> GetAllAddresses(Func<HdAccount, bool> accountFilter = null)
        {
            IEnumerable<HdAccount> accounts = this.GetAccounts(accountFilter);

            var allAddresses = new List<HdAddress>();
            foreach (HdAccount account in accounts)
            {
                allAddresses.AddRange(account.GetCombinedAddresses());
            }
            return allAddresses;
        }

        /// <summary>
        /// Adds an account to the current wallet.
        /// </summary>
        /// <remarks>
        /// The name given to the account is of the form "account (i)" by default, where (i) is an incremental index starting at 0.
        /// According to BIP44, an account at index (i) can only be created when the account at index (i - 1) contains at least one transaction.
        /// </remarks>
        /// <seealso cref="https://github.com/bitcoin/bips/blob/master/bip-0044.mediawiki"/>
        /// <param name="password">The password used to decrypt the wallet's <see cref="EncryptedSeed"/>.</param>
        /// <param name="accountCreationTime">Creation time of the account to be created.</param>
        /// <param name="purpose">A BIP44 purpose (also used in BIP84 and BIP49), this will allow to overwrite the default BIP44 purpose.</param>
        /// <param name="accountIndex">The index at which an account will be created. If left null, a new account will be created after the last used one.</param>
        /// <param name="accountName">The name of the account to be created. If left null, an account will be created according to the <see cref="Wallet.AccountNamePattern"/>.</param>
        /// <returns>A new HD account.</returns>
        public HdAccount AddNewAccount(string password, DateTimeOffset accountCreationTime, int purpose, int? accountIndex = null, string accountName = null)
        {
            Guard.NotEmpty(password, nameof(password));

            AccountRoot accountRoot = this.AccountsRoot.Single();
            return accountRoot.AddNewAccount(password, this.EncryptedSeed, this.ChainCode, this.Network, accountCreationTime, purpose, accountIndex, accountName);
        }

        /// <summary>
        /// Adds an account to the current wallet.
        /// </summary>
        /// <remarks>
        /// The name given to the account is of the form "account (i)" by default, where (i) is an incremental index starting at 0.
        /// According to BIP44, an account at index (i) can only be created when the account at index (i - 1) contains at least one transaction.
        /// </remarks>
        /// <seealso cref="https://github.com/bitcoin/bips/blob/master/bip-0044.mediawiki"/>
        /// <param name="extPubKey">The extended public key for the wallet<see cref="EncryptedSeed"/>.</param>
        /// <param name="accountIndex">Zero-based index of the account to add.</param>
        /// <param name="accountCreationTime">Creation time of the account to be created.</param>
        /// <param name="purpose">A BIP44 purpose (also used in BIP84 and BIP49), this will allow to overwrite the default BIP44 purpose.</param>
        /// <returns>A new HD account.</returns>
        public HdAccount AddNewAccount(ExtPubKey extPubKey, int accountIndex, DateTimeOffset accountCreationTime, int purpose)
        {
            AccountRoot accountRoot = this.AccountsRoot.Single();
            return accountRoot.AddNewAccount(extPubKey, accountIndex, this.Network, accountCreationTime, purpose);
        }

        /// <summary>
        /// Gets the first account that contains no transaction.
        /// </summary>
        /// <returns>An unused account.</returns>
        public HdAccount GetFirstUnusedAccount(IWalletStore walletStore)
        {
            // Get the accounts root for this type of coin.
            AccountRoot accountsRoot = this.AccountsRoot.Single();

            if (accountsRoot.Accounts.Any())
            {
                // Get an unused account.
                HdAccount firstUnusedAccount = accountsRoot.GetFirstUnusedAccount(walletStore);
                if (firstUnusedAccount != null)
                {
                    return firstUnusedAccount;
                }
            }

            return null;
        }

        /// <summary>
        /// Determines whether the wallet contains the specified address.
        /// </summary>
        /// <param name="address">The address to check.</param>
        /// <returns>A value indicating whether the wallet contains the specified address.</returns>
        public bool ContainsAddress(HdAddress address)
        {
            if (!this.AccountsRoot.Any(r => r.Accounts.Any(
                a => a.ExternalAddresses.Any(i => i.Address == address.Address) ||
                     a.InternalAddresses.Any(i => i.Address == address.Address))))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets the extended private key for the given address.
        /// </summary>
        /// <param name="password">The password used to encrypt/decrypt sensitive info.</param>
        /// <param name="address">The address to get the private key for.</param>
        /// <returns>The extended private key.</returns>
        public ISecret GetExtendedPrivateKeyForAddress(string password, HdAddress address)
        {
            Guard.NotEmpty(password, nameof(password));
            Guard.NotNull(address, nameof(address));

            // Check if the wallet contains the address.
            if (!this.ContainsAddress(address))
            {
                throw new WalletException("Address not found on wallet.");
            }

            // get extended private key
            Key privateKey = HdOperations.DecryptSeed(this.EncryptedSeed, password, this.Network);
            return HdOperations.GetExtendedPrivateKey(privateKey, this.ChainCode, address.HdPath, this.Network);
        }

        /// <summary>
        /// Lists all spendable transactions from all accounts in the wallet.
        /// </summary>
        /// <param name="currentChainHeight">Height of the current chain, used in calculating the number of confirmations.</param>
        /// <param name="confirmations">The number of confirmations required to consider a transaction spendable.</param>
        /// <param name="accountFilter">An optional filter for filtering the accounts being returned.</param>
        /// <returns>A collection of spendable outputs.</returns>
        public IEnumerable<UnspentOutputReference> GetAllSpendableTransactions(IWalletStore walletStore, int currentChainHeight, int confirmations = 0, Func<HdAccount, bool> accountFilter = null)
        {
            IEnumerable<HdAccount> accounts = this.GetAccounts(accountFilter);

            return accounts.SelectMany(x => x.GetSpendableTransactions(walletStore, currentChainHeight, this.Network.Consensus.CoinbaseMaturity, confirmations));
        }

        /// <summary>
        /// Lists all unspent transactions from all accounts in the wallet.
        /// This is distinct from the list of spendable transactions. A transaction can be unspent but not yet spendable due to coinbase/stake maturity, for example.
        /// </summary>
        /// <param name="currentChainHeight">Height of the current chain, used in calculating the number of confirmations.</param>
        /// <param name="confirmations">The number of confirmations required to consider a transaction spendable.</param>
        /// <param name="accountFilter">An optional filter for filtering the accounts being returned.</param>
        /// <returns>A collection of spendable outputs.</returns>
        public IEnumerable<UnspentOutputReference> GetAllUnspentTransactions(IWalletStore walletStore, int currentChainHeight, int confirmations = 0, Func<HdAccount, bool> accountFilter = null)
        {
            IEnumerable<HdAccount> accounts = this.GetAccounts(accountFilter);

            // The logic for retrieving unspent transactions is almost identical to determining spendable transactions, we just don't take coinbase/stake maturity into consideration.
            return accounts.SelectMany(x => x.GetSpendableTransactions(walletStore, currentChainHeight, 0, confirmations));
        }

        /// <summary>
        /// Calculates the fee paid by the user on a transaction sent.
        /// </summary>
        /// <param name="transactionId">The transaction id to look for.</param>
        /// <returns>The fee paid.</returns>
        public Money GetSentTransactionFee(uint256 transactionId)
        {
            List<TransactionOutputData> allTransactions = this.GetAllTransactions(Wallet.NormalAccounts).ToList();

            // Get a list of all the inputs spent in this transaction.
            List<TransactionOutputData> inputsSpentInTransaction = allTransactions.Where(t => t.SpendingDetails?.TransactionId == transactionId).ToList();

            if (!inputsSpentInTransaction.Any())
            {
                throw new WalletException("Not a sent transaction");
            }

            // Get the details of the spending transaction, which can be found on any input spent.
            SpendingDetails spendingTransaction = inputsSpentInTransaction.Select(s => s.SpendingDetails).First();

            // The change is the output paid into one of our addresses. We make sure to exclude the output received to one of
            // our addresses if this transaction is self-sent.
            IEnumerable<TransactionOutputData> changeOutput = allTransactions.Where(t => t.Id == transactionId && spendingTransaction.Payments.All(p => p.OutputIndex != t.Index)).ToList();

            Money inputsAmount = new Money(inputsSpentInTransaction.Sum(i => i.Amount));
            Money outputsAmount = new Money(spendingTransaction.Payments.Sum(p => p.Amount) + changeOutput.Sum(c => c.Amount));

            return inputsAmount - outputsAmount;
        }

        /// <summary>
        /// Finds the HD addresses for the address.
        /// </summary>
        /// <remarks>
        /// Returns an HDAddress.
        /// </remarks>
        /// <param name="address">An address.</param>
        /// <param name="accountFilter">An optional filter for filtering the accounts being returned.</param>
        /// <returns>HD Address</returns>
        public HdAddress GetAddress(string address, Func<HdAccount, bool> accountFilter = null)
        {
            Guard.NotNull(address, nameof(address));
            return this.GetAllAddresses(accountFilter).SingleOrDefault(a => a.Address == address);
        }
    }

    /// <summary>
    /// The root for the accounts for any type of coins.
    /// </summary>
    public class AccountRoot
    {
        /// <summary>
        /// Initializes a new instance of the object.
        /// </summary>
        public AccountRoot()
        {
            this.Accounts = new List<HdAccount>();
        }

        /// <summary>
        /// The type of coin, Bitcoin or Stratis.
        /// </summary>
        [JsonProperty(PropertyName = "coinType")]
        public int CoinType { get; set; }

        /// <summary>
        /// The height of the last block that was synced.
        /// </summary>
        //[JsonProperty(PropertyName = "lastBlockSyncedHeight", NullValueHandling = NullValueHandling.Ignore)]
        [JsonIgnore]
        public int? LastBlockSyncedHeight { get; set; }

        /// <summary>
        /// The hash of the last block that was synced.
        /// </summary>
        //[JsonProperty(PropertyName = "lastBlockSyncedHash", NullValueHandling = NullValueHandling.Ignore)]
        //[JsonConverter(typeof(UInt256JsonConverter))]
        [JsonIgnore]
        public uint256 LastBlockSyncedHash { get; set; }

        /// <summary>
        /// The accounts used in the wallet.
        /// </summary>
        [JsonProperty(PropertyName = "accounts")]
        public ICollection<HdAccount> Accounts { get; set; }

        /// <summary>
        /// Gets the first account that contains no transaction.
        /// </summary>
        /// <returns>An unused account</returns>
        public HdAccount GetFirstUnusedAccount(IWalletStore walletStore)
        {
            if (this.Accounts == null)
                return null;

            List<HdAccount> unusedAccounts = this.Accounts
                .Where(Wallet.NormalAccounts)
                .Where(acc =>
                !acc.ExternalAddresses.SelectMany(add => walletStore.GetForAddress(add.Address)).Any()
                &&
                !acc.InternalAddresses.SelectMany(add => walletStore.GetForAddress(add.Address)).Any()).ToList();

            if (!unusedAccounts.Any())
                return null;

            // gets the unused account with the lowest index
            int index = unusedAccounts.Min(a => a.Index);
            return unusedAccounts.Single(a => a.Index == index);
        }

        /// <summary>
        /// Gets the account matching the name passed as a parameter.
        /// </summary>
        /// <param name="accountName">The name of the account to get.</param>
        /// <returns>The HD account specified by the parameter or <c>null</c> if the account does not exist.</returns>
        public HdAccount GetAccountByName(string accountName)
        {
            return this.Accounts?.SingleOrDefault(a => a.Name == accountName);
        }

        /// <summary>
        /// Gets the account matching the index passed as a parameter.
        /// </summary>
        /// <param name="index">The index of the account to get.</param>
        /// <returns>The HD account specified by the parameter or <c>null</c> if the account does not exist.</returns>
        public HdAccount GetAccountByIndex(int index)
        {
            return this.Accounts?.SingleOrDefault(a => a.Index == index);
        }

        /// <summary>
        /// Adds an account to the current account root using encrypted seed and password.
        /// </summary>
        /// <remarks>The name given to the account is of the form "account (i)" by default, where (i) is an incremental index starting at 0.
        /// According to BIP44, an account at index (i) can only be created when the account at index (i - 1) contains transactions.
        /// <seealso cref="https://github.com/bitcoin/bips/blob/master/bip-0044.mediawiki"/></remarks>
        /// <param name="password">The password used to decrypt the wallet's encrypted seed.</param>
        /// <param name="encryptedSeed">The encrypted private key for this wallet.</param>
        /// <param name="chainCode">The chain code for this wallet.</param>
        /// <param name="network">The network for which this account will be created.</param>
        /// <param name="accountCreationTime">Creation time of the account to be created.</param>
        /// <param name="accountIndex">The index at which an account will be created. If left null, a new account will be created after the last used one.</param>
        /// <param name="accountName">The name of the account to be created. If left null, an account will be created according to the <see cref="AccountNamePattern"/>.</param>
        /// <param name="purpose">A BIP44 purpose (also used in BIP84 and BIP49), this will allow to overwrite the default BIP44 purpose.</param>
        /// <returns>A new HD account.</returns>
        public HdAccount AddNewAccount(string password, string encryptedSeed, byte[] chainCode, Network network, DateTimeOffset accountCreationTime, int purpose, int? accountIndex = null, string accountName = null)
        {
            Guard.NotEmpty(password, nameof(password));
            Guard.NotEmpty(encryptedSeed, nameof(encryptedSeed));
            Guard.NotNull(chainCode, nameof(chainCode));

            ICollection<HdAccount> hdAccounts = this.Accounts;

            // If an account needs to be created at a specific index or with a specific name, make sure it doesn't already exist.
            if (hdAccounts.Any(a => a.Index == accountIndex || a.Name == accountName))
            {
                throw new WalletException($"An account at index {accountIndex} or with name {accountName} already exists.");
            }

            if (accountIndex == null)
            {
                if (hdAccounts.Any())
                {
                    // Hide account indexes used for cold staking from the "Max" calculation.
                    accountIndex = hdAccounts.Where(Wallet.NormalAccounts).Max(a => a.Index) + 1;
                }
                else
                {
                    accountIndex = 0;
                }
            }

            HdAccount newAccount = this.CreateAccount(password, encryptedSeed, chainCode, network, accountCreationTime, purpose, accountIndex.Value, accountName);

            hdAccounts.Add(newAccount);
            this.Accounts = hdAccounts;

            return newAccount;
        }

        /// <summary>
        /// Create an account for a specific account index and account name pattern.
        /// </summary>
        /// <param name="password">The password used to decrypt the wallet's encrypted seed.</param>
        /// <param name="encryptedSeed">The encrypted private key for this wallet.</param>
        /// <param name="chainCode">The chain code for this wallet.</param>
        /// <param name="network">The network for which this account will be created.</param>
        /// <param name="accountCreationTime">Creation time of the account to be created.</param>
        /// <param name="newAccountIndex">The optional account index to use.</param>
        /// <param name="newAccountName">The optional account name to use.</param>
        /// <param name="purpose">A BIP44 purpose (also used in BIP84 and BIP49), this will allow to overwrite the default BIP44 purpose.</param>
        /// <returns>A new HD account.</returns>
        public HdAccount CreateAccount(string password, string encryptedSeed, byte[] chainCode,
            Network network, DateTimeOffset accountCreationTime, int purpose,
            int newAccountIndex, string newAccountName = null)
        {
            if (string.IsNullOrEmpty(newAccountName))
            {
                newAccountName = string.Format(Wallet.AccountNamePattern, newAccountIndex);
            }

            // Get the extended pub key used to generate addresses for this account.
            string accountHdPath = HdOperations.GetAccountHdPath(purpose, this.CoinType, newAccountIndex);
            Key privateKey = HdOperations.DecryptSeed(encryptedSeed, password, network);
            ExtPubKey accountExtPubKey = HdOperations.GetExtendedPublicKey(privateKey, chainCode, accountHdPath);

            return new HdAccount
            {
                Index = newAccountIndex,
                ExtendedPubKey = accountExtPubKey.ToString(network),
                ExternalAddresses = new List<HdAddress>(),
                InternalAddresses = new List<HdAddress>(),
                Name = newAccountName,
                HdPath = accountHdPath,
                Purpose = purpose,
                CreationTime = accountCreationTime
            };
        }

        /// <inheritdoc cref="AddNewAccount(string, string, byte[], Network, DateTimeOffset)"/>
        /// <summary>
        /// Adds an account to the current account root using extended public key and account index.
        /// </summary>
        /// <param name="accountExtPubKey">The extended public key for the account.</param>
        /// <param name="accountIndex">The zero-based account index.</param>
        /// <param name="purpose">A BIP44 purpose (also used in BIP84 and BIP49), this will allow to overwrite the default BIP44 purpose.</param>
        public HdAccount AddNewAccount(ExtPubKey accountExtPubKey, int accountIndex, Network network, DateTimeOffset accountCreationTime, int purpose)
        {
            ICollection<HdAccount> hdAccounts = this.Accounts.ToList();

            if (hdAccounts.Any(a => a.Index == accountIndex))
            {
                throw new WalletException("There is already an account in this wallet with index: " + accountIndex);
            }

            if (hdAccounts.Any(x => x.ExtendedPubKey == accountExtPubKey.ToString(network)))
            {
                throw new WalletException("There is already an account in this wallet with this xpubkey: " +
                                            accountExtPubKey.ToString(network));
            }

            string accountHdPath = HdOperations.GetAccountHdPath(purpose, this.CoinType, accountIndex);

            var newAccount = new HdAccount
            {
                Index = accountIndex,
                ExtendedPubKey = accountExtPubKey.ToString(network),
                ExternalAddresses = new List<HdAddress>(),
                InternalAddresses = new List<HdAddress>(),
                Name = $"account {accountIndex}",
                HdPath = accountHdPath,
                Purpose = purpose,
                CreationTime = accountCreationTime
            };

            hdAccounts.Add(newAccount);
            this.Accounts = hdAccounts;

            return newAccount;
        }
    }

    /// <summary>
    /// An HD account's details.
    /// </summary>
    public class HdAccount
    {
        public HdAccount()
        {
            this.ExternalAddresses = new List<HdAddress>();
            this.InternalAddresses = new List<HdAddress>();
        }

        /// <summary>
        /// The index of the account.
        /// </summary>
        /// <remarks>
        /// According to BIP44, an account at index (i) can only be created when the account
        /// at index (i - 1) contains transactions.
        /// </remarks>
        [JsonProperty(PropertyName = "index")]
        public int Index { get; set; }

        /// <summary>
        /// The purpose of the coin as described in BIP44.
        /// </summary>
        [JsonProperty(PropertyName = "purpose")]
        public int Purpose { get; set; }

        /// <summary>
        /// The name of this account.
        /// </summary>
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        /// <summary>
        /// A path to the account as defined in BIP44.
        /// </summary>
        [JsonProperty(PropertyName = "hdPath")]
        public string HdPath { get; set; }

        /// <summary>
        /// An extended pub key used to generate addresses.
        /// </summary>
        [JsonProperty(PropertyName = "extPubKey")]
        public string ExtendedPubKey { get; set; }

        /// <summary>
        /// Gets or sets the creation time.
        /// </summary>
        [JsonProperty(PropertyName = "creationTime")]
        [JsonConverter(typeof(DateTimeOffsetConverter))]
        public DateTimeOffset CreationTime { get; set; }

        /// <summary>
        /// The list of external addresses, typically used for receiving money.
        /// </summary>
        [JsonProperty(PropertyName = "externalAddresses")]
        public ICollection<HdAddress> ExternalAddresses { get; set; }

        /// <summary>
        /// The list of internal addresses, typically used to receive change.
        /// </summary>
        [JsonProperty(PropertyName = "internalAddresses")]
        public ICollection<HdAddress> InternalAddresses { get; set; }

        /// <summary>
        /// Gets the type of coin this account is for.
        /// </summary>
        /// <returns>A BIP44 CoinType.</returns>
        public int GetCoinType()
        {
            return HdOperations.GetCoinType(this.HdPath);
        }

        /// <summary>
        /// Check if the current account is a normal or special purpose one.
        /// </summary>
        /// <returns>True if this is a normal account (index below the SpecialPurposeAccountIndexesStart).</returns>
        public bool IsNormalAccount()
        {
            return this.Index < Wallet.SpecialPurposeAccountIndexesStart;
        }

        /// <summary>
        /// Gets the first receiving address that contains no transaction.
        /// </summary>
        /// <returns>An unused address</returns>
        public HdAddress GetFirstUnusedReceivingAddress(IWalletStore walletStore)
        {
            return this.GetFirstUnusedAddress(walletStore, false);
        }

        /// <summary>
        /// Gets the first change address that contains no transaction.
        /// </summary>
        /// <returns>An unused address</returns>
        public HdAddress GetFirstUnusedChangeAddress(IWalletStore walletStore)
        {
            return this.GetFirstUnusedAddress(walletStore, true);
        }

        /// <summary>
        /// Gets the first receiving address that contains no transaction.
        /// </summary>
        /// <returns>An unused address</returns>
        private HdAddress GetFirstUnusedAddress(IWalletStore walletStore, bool isChange)
        {
            IEnumerable<HdAddress> addresses = isChange ? this.InternalAddresses : this.ExternalAddresses;
            if (addresses == null)
                return null;

            List<HdAddress> unusedAddresses = addresses.Where(acc => walletStore.CountForAddress(acc.Address) == 0).ToList();
            if (!unusedAddresses.Any())
            {
                return null;
            }

            // gets the unused address with the lowest index
            int index = unusedAddresses.Min(a => a.Index);
            return unusedAddresses.Single(a => a.Index == index);
        }

        /// <summary>
        /// Gets the last address that contains transactions.
        /// </summary>
        /// <param name="isChange">Whether the address is a change (internal) address or receiving (external) address.</param>
        /// <returns></returns>
        public HdAddress GetLastUsedAddress(IWalletStore walletStore, bool isChange)
        {
            IEnumerable<HdAddress> addresses = isChange ? this.InternalAddresses : this.ExternalAddresses;
            if (addresses == null)
                return null;

            List<HdAddress> usedAddresses = addresses.Where(acc => walletStore.CountForAddress(acc.Address) > 0).ToList();
            if (!usedAddresses.Any())
            {
                return null;
            }

            // gets the used address with the highest index
            int index = usedAddresses.Max(a => a.Index);
            return usedAddresses.Single(a => a.Index == index);
        }

        /// <summary>
        /// Get the accounts total spendable value for both confirmed and unconfirmed UTXO.
        /// </summary>
        public (Money ConfirmedAmount, Money UnConfirmedAmount) GetBalances(IWalletStore walletStore, bool excludeColdStakeUtxo)
        {
            var res = walletStore.GetBalanceForAccount(this.Index, excludeColdStakeUtxo);

            return (res.AmountConfirmed, res.AmountUnconfirmed);
        }

        /// <summary>
        /// Return both the external and internal (change) address from an account.
        /// </summary>
        /// <returns>All addresses that belong to this account.</returns>
        public IEnumerable<HdAddress> GetCombinedAddresses()
        {
            IEnumerable<HdAddress> addresses = new List<HdAddress>();
            if (this.ExternalAddresses != null)
            {
                addresses = this.ExternalAddresses;
            }

            if (this.InternalAddresses != null)
            {
                addresses = addresses.Concat(this.InternalAddresses);
            }

            return addresses;
        }

        /// <summary>
        /// Creates a number of additional addresses in the current account.
        /// </summary>
        /// <remarks>
        /// The name given to the account is of the form "account (i)" by default, where (i) is an incremental index starting at 0.
        /// According to BIP44, an account at index (i) can only be created when the account at index (i - 1) contains at least one transaction.
        /// </remarks>
        /// <param name="network">The network these addresses will be for.</param>
        /// <param name="addressesQuantity">The number of addresses to create.</param>
        /// <param name="isChange">Whether the addresses added are change (internal) addresses or receiving (external) addresses.</param>
        /// <returns>The created addresses.</returns>
        public IEnumerable<HdAddress> CreateAddresses(Network network, int addressesQuantity, bool isChange = false)
        {
            ICollection<HdAddress> addresses = isChange ? this.InternalAddresses : this.ExternalAddresses;

            // Get the index of the last address.
            int firstNewAddressIndex = 0;
            if (addresses.Any())
            {
                firstNewAddressIndex = addresses.Max(add => add.Index) + 1;
            }

            var addressesCreated = new List<HdAddress>();
            for (int i = firstNewAddressIndex; i < firstNewAddressIndex + addressesQuantity; i++)
            {
                // Retrieve the pubkey associated with the private key of this address index.
                PubKey pubkey = HdOperations.GeneratePublicKey(this.ExtendedPubKey, i, isChange);

                // Add the new address details to the list of addresses.
                var newAddress = new HdAddress
                {
                    Index = i,
                    HdPath = HdOperations.CreateHdPath(this.Purpose, this.GetCoinType(), this.Index, isChange, i),
                    Pubkey = pubkey.ScriptPubKey, // this is a P2PK script type
                };

                if (newAddress.IsBip44())
                {
                    // Generate the P2PKH address corresponding to the pubkey.
                    BitcoinPubKeyAddress address = pubkey.GetAddress(network);
                    newAddress.ScriptPubKey = address.ScriptPubKey;
                    newAddress.Address = address.ToString();
                }
                else if (newAddress.IsBip84())
                {
                    // Generate the PW2PKH address corresponding to the pubkey.
                    BitcoinWitPubKeyAddress witAddress = pubkey.GetSegwitAddress(network);
                    newAddress.ScriptPubKey = witAddress.ScriptPubKey;
                    newAddress.Address = witAddress.ToString();
                }

                // TODO: should we check this and throw or just skip if no derivation path is found (in that case we default to only P2PK)
                if (newAddress.Address == null)
                {
                    throw new WalletException("Unknown derivation path");
                }

                addresses.Add(newAddress);
                addressesCreated.Add(newAddress);
            }

            if (isChange)
            {
                this.InternalAddresses = addresses;
            }
            else
            {
                this.ExternalAddresses = addresses;
            }

            return addressesCreated;
        }

        /// <summary>
        /// Lists all spendable transactions in the current account.
        /// </summary>
        /// <param name="currentChainHeight">The current height of the chain. Used for calculating the number of confirmations a transaction has.</param>
        /// <param name="coinbaseMaturity">The coinbase maturity after which a coinstake transaction is spendable.</param>
        /// <param name="confirmations">The minimum number of confirmations required for transactions to be considered.</param>
        /// <returns>A collection of spendable outputs that belong to the given account.</returns>
        /// <remarks>Note that coinbase and coinstake transaction outputs also have to mature with a sufficient number of confirmations before
        /// they are considered spendable. This is independent of the confirmations parameter.</remarks>
        public IEnumerable<UnspentOutputReference> GetSpendableTransactions(IWalletStore walletStore, int currentChainHeight, long coinbaseMaturity, int confirmations = 0)
        {
            // This will take all the spendable coins that belong to the account and keep the reference to the HdAddress and HdAccount.
            // This is useful so later the private key can be calculated just from a given UTXO.
            foreach (HdAddress address in this.GetCombinedAddresses())
            {
                // A block that is at the tip has 1 confirmation.
                // When calculating the confirmations the tip must be advanced by one.

                int countFrom = currentChainHeight + 1;
                foreach (TransactionOutputData transactionData in address.UnspentTransactions(walletStore))
                {
                    int? confirmationCount = 0;

                    if (transactionData.BlockHeight != null)
                    {
                        confirmationCount = countFrom >= transactionData.BlockHeight ? countFrom - transactionData.BlockHeight : 0;
                    }

                    if (confirmationCount < confirmations)
                    {
                        continue;
                    }

                    bool isCoinBase = transactionData.IsCoinBase ?? false;
                    bool isCoinStake = transactionData.IsCoinStake ?? false;

                    // Check if this wallet is a normal purpose wallet (not cold staking, etc).
                    if (this.IsNormalAccount())
                    {
                        bool isColdCoinStake = transactionData.IsColdCoinStake ?? false;

                        // Skip listing the UTXO if this is a normal wallet, and the UTXO is marked as an cold coin stake.
                        if (isColdCoinStake)
                        {
                            continue;
                        }
                    }

                    // This output can unconditionally be included in the results.
                    // Or this output is a ColdStake, CoinBase or CoinStake and has reached maturity.
                    if ((!isCoinBase && !isCoinStake) || (confirmationCount > coinbaseMaturity))
                    {
                        yield return new UnspentOutputReference
                        {
                            Account = this,
                            Address = address,
                            Transaction = transactionData,
                            Confirmations = confirmationCount.Value
                        };
                    }
                }
            }
        }
    }

    /// <summary>
    /// An HD address.
    /// </summary>
    public class HdAddress
    {
        public HdAddress()
        {
            // this.Transactions = new List<TransactionData>();
        }

        /// <summary>
        /// The index of the address.
        /// </summary>
        [JsonProperty(PropertyName = "index")]
        public int Index { get; set; }

        /// <summary>
        /// The P2PKH (pay-to-pubkey-hash) script pub key for this address.
        /// </summary>
        /// <remarks>The script is of the format OP_DUP OP_HASH160 {pubkeyhash} OP_EQUALVERIFY OP_CHECKSIG</remarks>
        [JsonProperty(PropertyName = "scriptPubKey")]
        [JsonConverter(typeof(ScriptJsonConverter))]
        public Script ScriptPubKey { get; set; }

        /// <summary>
        /// The P2PK (pay-to-pubkey) script pub key corresponding to the private key of this address.
        /// </summary>
        /// <remarks>This is typically only used for mining, as the valid script types for mining are constrained.
        /// Block explorers often depict the P2PKH address as the 'address' of a P2PK scriptPubKey, which is not
        /// actually correct. A P2PK scriptPubKey does not have a defined address format.
        ///
        /// The script itself is of the format: {pubkey} OP_CHECKSIG</remarks>
        [JsonProperty(PropertyName = "pubkey")]
        [JsonConverter(typeof(ScriptJsonConverter))]
        public Script Pubkey { get; set; }

        /// <summary>
        /// The Base58 representation of this address.
        /// </summary>
        [JsonProperty(PropertyName = "address")]
        public string Address { get; set; }

        /// <summary>
        /// A script that is used for P2SH and P2WSH scenarios (mostly used for staking).
        /// </summary>
        /// <remarks>
        /// Obsolete: This is kept for legacy reasons, use the the <see cref="RedeemScripts"/> property instead.
        /// </remarks>
        [JsonProperty(PropertyName = "redeemScript")]
        [JsonConverter(typeof(ScriptJsonConverter))]
        [ObsoleteAttribute("This is kept for legacy reasons, use the the RedeemScripts property instead.")]
        public Script RedeemScriptObsolete { get; set; }

        /// <summary>
        /// A collection of scripts that is used for P2SH and P2WSH scenarios (mostly used for cold staking).
        /// This solves issue https://github.com/block-core/Martiscoin/issues/395
        /// </summary>
        [JsonProperty(PropertyName = "redeemScripts")]
        [JsonConverter(typeof(ScriptCollectionJsonConverter))]
        public ICollection<Script> RedeemScripts { get; set; }

        /// <summary>
        /// A collection of scripts that is used for P2SH and P2WSH scenarios (mostly used for cold staking)
        /// that can be marked as expired for staking, this is to be use together with the -enforceStakingFlag 
        /// mainly for cold staking pools.
        /// </summary>
        [JsonProperty(PropertyName = "redeemScriptsExpiry")]
        public ICollection<RedeemScriptExpiry> RedeemScriptExpiry { get; set; }

        /// <summary>
        /// A path to the address as defined in BIP44.
        /// </summary>
        [JsonProperty(PropertyName = "hdPath")]
        public string HdPath { get; set; }

        ///// <summary>
        ///// A list of transactions involving this address.
        ///// </summary>
        //[JsonProperty(PropertyName = "transactions")]
        //public ICollection<TransactionData> Transactions { get; set; }

        /// <summary>
        /// Specify whether UTXOs associated with this address is within the allowed staking time.
        /// </summary>
        [JsonProperty(PropertyName = "stakingExpiry", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime? StakingExpiry { get; set; }

        /// <summary>
        /// Determines whether this is a change address or a receive address.
        /// </summary>
        /// <returns>
        ///   <c>true</c> if it is a change address; otherwise, <c>false</c>.
        /// </returns>
        public bool IsChangeAddress()
        {
            return HdOperations.IsChangeAddress(this.HdPath);
        }

        /// <summary>
        /// List all spendable transactions in an address.
        /// </summary>
        /// <returns>List of spendable transactions.</returns>
        public IEnumerable<TransactionOutputData> UnspentTransactions(IWalletStore walletStore)
        {
            return walletStore.GetUnspentForAddress(this.Address);
        }

        /// <summary>
        /// Get the address total spendable value for both confirmed and unconfirmed UTXO.
        /// </summary>
        public (Money confirmedAmount, Money unConfirmedAmount, bool anyTrx) GetBalances(IWalletStore walletStore, bool excludeColdStakeUtxo)
        {
            var trx = walletStore.GetForAddress(this.Address).ToList();

            List<TransactionOutputData> allTransactions = excludeColdStakeUtxo
                ? trx.Where(t => t.IsColdCoinStake != true).ToList()
                : trx;

            long confirmed = allTransactions.Sum(t => t.GetUnspentAmount(true));
            long total = allTransactions.Sum(t => t.GetUnspentAmount(false));

            return (confirmed, total - confirmed, trx.Any());
        }

        /// <summary>
        /// Check if the address path is a BIP84 segwit address.
        /// </summary>
        public bool IsBip84()
        {
            return HdOperations.GetPurpose(this.HdPath) == 84;
        }

        /// <summary>
        /// Check if the address path is a BIP44 address.
        /// </summary>
        public bool IsBip44()
        {
            return HdOperations.GetPurpose(this.HdPath) == 44;
        }
    }

    public class RedeemScriptExpiry
    {
        /// <summary>
        /// A script that is used for P2SH and P2WSH scenarios (mostly used for staking).
        /// </summary>
        [JsonProperty(PropertyName = "redeemScript")]
        [JsonConverter(typeof(ScriptJsonConverter))]
        public Script RedeemScript { get; set; }

        /// <summary>
        /// Specify whether UTXOs associated with this address is within the allowed staking time.
        /// </summary>
        [JsonProperty(PropertyName = "stakingExpiry", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime? StakingExpiry { get; set; }
    }

    /// <summary>
    /// Represents an UTXO that keeps a reference to <see cref="HdAddress"/> and <see cref="HdAccount"/>.
    /// </summary>
    /// <remarks>
    /// This is useful when an UTXO needs access to its HD properties like the HD path when reconstructing a private key.
    /// </remarks>
    public class UnspentOutputReference
    {
        /// <summary>
        /// The account associated with this UTXO
        /// </summary>
        public HdAccount Account { get; set; }

        /// <summary>
        /// The address associated with this UTXO
        /// </summary>
        public HdAddress Address { get; set; }

        /// <summary>
        /// The transaction representing the UTXO.
        /// </summary>
        public TransactionOutputData Transaction { get; set; }

        /// <summary>
        /// Number of confirmations for this UTXO.
        /// </summary>
        public int Confirmations { get; set; }

        /// <summary>
        /// Convert the <see cref="TransactionOutputData"/> to an <see cref="OutPoint"/>
        /// </summary>
        /// <returns>The corresponding <see cref="OutPoint"/>.</returns>
        public OutPoint ToOutPoint()
        {
            return new OutPoint(this.Transaction.Id, (uint)this.Transaction.Index);
        }
    }
}