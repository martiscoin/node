﻿using System;
using System.Collections.Generic;
using Martiscoin.Consensus.BlockInfo;
using Martiscoin.Consensus.Chain;
using Martiscoin.Consensus.ScriptInfo;
using Martiscoin.Consensus.TransactionInfo;
using Martiscoin.Features.BlockStore.Models;
using Martiscoin.Features.Wallet.Types;
using Martiscoin.NBitcoin;
using Martiscoin.NBitcoin.BIP32;
using Martiscoin.NBitcoin.BIP39;
using Martiscoin.NBitcoin.BuilderExtensions;

namespace Martiscoin.Features.Wallet.Interfaces
{
    /// <summary>
    /// Interface for a manager providing operations on wallets.
    /// </summary>
    public interface IWalletManager
    {
        /// <summary>
        /// Starts this wallet manager.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the wallet manager.
        /// <para>Internally it waits for async loops to complete before saving the wallets to disk.</para>
        /// </summary>
        void Stop();

        /// <summary>
        /// The last processed block.
        /// </summary>
        uint256 WalletTipHash { get; set; }

        /// <summary>
        /// The last processed block height.
        /// </summary>
        int WalletTipHeight { get; set; }

        /// <summary>
        /// Lists all spendable transactions from all accounts in the wallet.
        /// </summary>
        /// <returns>A collection of spendable outputs</returns>
        IEnumerable<UnspentOutputReference> GetSpendableTransactionsInWallet(string walletName, int confirmations = 0);

        /// <summary>
        /// Lists all spendable transactions from the accounts in the wallet participating in staking.
        /// </summary>
        /// <returns>A collection of spendable outputs</returns>
        IEnumerable<UnspentOutputReference> GetSpendableTransactionsInWalletForStaking(string walletName, int confirmations = 0);

        /// <summary>
        /// List all unspent transactions contained in a given wallet.
        /// This is distinct from the list of spendable transactions. A transaction can be unspent but not yet spendable due to coinbase/stake maturity, for example.
        /// </summary>
        /// <returns>A collection of unspent outputs</returns>
        IEnumerable<UnspentOutputReference> GetUnspentTransactionsInWallet(string walletName, int confirmations, Func<HdAccount, bool> accountFilter);

        /// <summary>
        /// Helps identify UTXO's that can participate in staking.
        /// </summary>
        /// <returns>A dictionary containing string and template pairs - e.g. { "P2PK", PayToPubkeyTemplate.Instance }</returns>
        Dictionary<string, ScriptTemplate> GetValidStakingTemplates();

        /// <summary>
        /// Returns additional transaction builder extensions to use for building staking transactions.
        /// </summary>
        /// <returns>Transaction builder extensions to use for building staking transactions.</returns>
        IEnumerable<BuilderExtension> GetTransactionBuilderExtensionsForStaking();

        /// <summary>
        /// Lists all spendable transactions from the account specified in <see cref="WalletAccountReference"/>.
        /// </summary>
        /// <returns>A collection of spendable outputs that belong to the given account.</returns>
        IEnumerable<UnspentOutputReference> GetSpendableTransactionsInAccount(WalletAccountReference walletAccountReference, int confirmations = 0);

        /// <summary>
        /// Creates a wallet and persist it as a file on the local system.
        /// </summary>
        /// <param name="password">The password used to encrypt sensitive info.</param>
        /// <param name="name">The name of the wallet.</param>
        /// <param name="passphrase">The passphrase used in the seed.</param>
        /// <param name="mnemonic">The user's mnemonic for the wallet.</param>
        /// <param name="coinType">Allow to override the default BIP44 cointype.</param>
        /// <param name="purpose">An optional BIP44 purpose (also used in BIP84 and BIP49), this means specifying BIP84 to create a segwit wallet.</param>
        /// <returns>A mnemonic defining the wallet's seed used to generate addresses.</returns>
        Mnemonic CreateWallet(string password, string name, string passphrase = null, Mnemonic mnemonic = null, int? coinType = null, int? purpose = null);
 

        /// <summary>
        /// Gets the private key associated with an address in the wallet.
        /// </summary>
        /// <param name="password">The user's password.</param>
        /// <param name="walletName">The name of the wallet.</param>
        /// <param name="accountName">The name of the account.</param>
        /// <param name="address">Address to extract the private key of.</param>
        /// <returns>The private key associated with the given address, in WIF representation.</returns>
        string RetrievePrivateKey(string password, string walletName, string accountName, string address);

        /// <summary>
        /// Signs a string message.
        /// </summary>
        /// <param name="password">The user's password.</param>
        /// <param name="walletName">The name of the wallet.</param>
        /// <param name="accountName">The name of the account.</param>
        /// <param name="externalAddress">Address to use to sign.</param>
        /// <param name="message">Message to sign.</param>
        /// <returns>The generated signature.</returns>
        SignMessageResult SignMessage(string password, string walletName, string accountName, string externalAddress, string message);

        /// <summary>
        /// Verifies the signed message.
        /// </summary>
        /// <param name="externalAddress">Address used to sign.</param>
        /// <param name="message">Message to verify.</param>
        /// <param name="signature">Message signature.</param>
        /// <returns>True if the signature is valid, false if it is invalid.</returns>
        bool VerifySignedMessage(string externalAddress, string message, string signature);

        /// <summary>
        /// Loads a wallet from a file.
        /// </summary>
        /// <param name="password">The user's password.</param>
        /// <param name="name">The name of the wallet.</param>
        /// <returns>The wallet.</returns>
        Types.Wallet LoadWallet(string password, string name);

        /// <summary>
        /// Unlocks a wallet for the specified time.
        /// </summary>
        /// <param name="password">The wallet password.</param>
        /// <param name="name">The name of the wallet.</param>
        /// <param name="timeout">The timeout in seconds.</param>
        void UnlockWallet(string password, string name, int timeout);

        /// <summary>
        /// Locks the wallet.
        /// </summary>
        /// <param name="name">The name of the wallet.</param>
        void LockWallet(string name);

        /// <summary>
        /// Recovers a wallet using mnemonic and password.
        /// </summary>
        /// <param name="password">The user's password.</param>
        /// <param name="name">The name of the wallet.</param>
        /// <param name="mnemonic">The user's mnemonic for the wallet.</param>
        /// <param name="passphrase">The passphrase used in the seed.</param>
        /// <param name="creationTime">The date and time this wallet was created.</param>
        /// <param name="purpose">An optional BIP44 purpose (also used in BIP84 and BIP49), this means specifying BIP84 to create a segwit wallet.</param>
        /// <param name="coinType">Allow to override the default BIP44 cointype.</param>
        /// <returns>The recovered wallet.</returns>
        Types.Wallet RecoverWallet(string password, string name, string mnemonic, DateTime creationTime, string passphrase = null, int? purpose = null, int? coinType = null, bool? isColdStakingWallet = false);

        /// <summary>
        /// Recovers a wallet using extended public key and account index.
        /// </summary>
        /// <param name="name">The name of the wallet.</param>
        /// <param name="extPubKey">The extended public key.</param>
        /// <param name="accountIndex">The account number.</param>
        /// <param name="creationTime">The date and time this wallet was created.</param>
        /// <param name="purpose">An optional BIP44 purpose (also used in BIP84 and BIP49), this means specifying BIP84 to create a segwit wallet.</param>
        /// <returns></returns>
        Types.Wallet RecoverWallet(string name, ExtPubKey extPubKey, int accountIndex, DateTime creationTime, int? purpose = null);

        /// <summary>
        /// Deletes a wallet.
        /// </summary>
        void DeleteWallet();

        /// <summary>
        /// Gets an account that contains no transactions.
        /// </summary>
        /// <param name="walletName">The name of the wallet from which to get an account.</param>
        /// <param name="password">The password used to decrypt the private key.</param>
        /// <param name="purpose">An optional BIP44 purpose (also used in BIP84 and BIP49), this means specifying BIP84 to create a segwit wallet.</param>
        /// <remarks>
        /// According to BIP44, an account at index (i) can only be created when the account
        /// at index (i - 1) contains transactions.
        /// </remarks>
        /// <returns>An unused account.</returns>
        HdAccount GetUnusedAccount(string walletName, string password, int? purpose = null);

        /// <summary>
        /// Gets an account that contains no transactions.
        /// </summary>
        /// <param name="wallet">The wallet from which to get an account.</param>
        /// <param name="password">The password used to decrypt the private key.</param>
        /// <param name="purpose">An optional BIP44 purpose (also used in BIP84 and BIP49), this means specifying BIP84 to create a segwit wallet.</param>
        /// <remarks>
        /// According to BIP44, an account at index (i) can only be created when the account
        /// at index (i - 1) contains transactions.
        /// </remarks>
        /// <returns>An unused account.</returns>
        HdAccount GetUnusedAccount(Types.Wallet wallet, string password, int? purpose = null);

        /// <summary>
        /// Gets an address that contains no transaction.
        /// </summary>
        /// <param name="accountReference">The name of the wallet and account</param>
        /// <returns>An unused address or a newly created address, in Base58 format.</returns>
        HdAddress GetUnusedAddress(WalletAccountReference accountReference);

        /// <summary>
        /// Gets the first change address that contains no transaction.
        /// </summary>
        /// <param name="accountReference">The name of the wallet and account.</param>
        /// <returns>An unused change address or a newly created change address, in Base58 format.</returns>
        HdAddress GetUnusedChangeAddress(WalletAccountReference accountReference);

        /// <summary>
        /// Gets a collection of unused receiving or change addresses.
        /// </summary>
        /// <param name="accountReference">The name of the wallet and account.</param>
        /// <param name="count">The number of addresses to create.</param>
        /// <param name="isChange">A value indicating whether or not the addresses to get should be receiving or change addresses.</param>
        /// <param name="alwaysnew">A value indicating whether or not the address to get should be a new or unused address.</param>
        /// <returns>A list of unused addresses. New addresses will be created as necessary.</returns>
        IEnumerable<HdAddress> GetUnusedAddresses(WalletAccountReference accountReference, int count, bool isChange = false, bool alwaysnew = false);

        /// <summary>
        /// Gets the history of transactions contained in an account.
        /// If no account name is specified, history will be returned for all accounts in the wallet.
        /// </summary>
        /// <param name="walletName">The wallet name.</param>
        /// <param name="accountName">The account name.</param>
        /// <returns>Collection of address history and transaction pairs.</returns>
        IEnumerable<AccountHistory> GetHistory(string walletName, string accountName = null);

        /// <summary>
        /// Gets the history of the transactions in addresses contained in this account.
        /// </summary>
        /// <param name="wallet">The wallet instance.</param>
        /// <param name="account">The account for which to get history.</param>
        /// <returns>The history for this account.</returns>
        AccountHistory GetHistory(Types.Wallet wallet, HdAccount account);

        /// <summary>
        /// Gets the history of transactions contained in an account.
        /// If no account name is specified, history will be returned for all accounts in the wallet.
        /// </summary>
        /// <param name="walletName">The wallet name.</param>
        /// <param name="accountName">The account name.</param>
        /// <param name="skip">Items to skip.</param>
        /// <param name="take">Items to take.</param>
        /// <returns>Collection of address history and transaction pairs.</returns>
        IEnumerable<AccountHistorySlim> GetHistorySlim(string walletName, string accountName = null, int skip = 0, int take = 100);

        /// <summary>
        /// Gets the history of the transactions in addresses contained in this account.
        /// </summary>
        /// <param name="wallet">The wallet instance.</param>
        /// <param name="account">The account for which to get history.</param>
        /// <param name="skip">Items to skip.</param>
        /// <param name="take">Items to take.</param>
        /// <returns>The history for this account.</returns>
        AccountHistorySlim GetHistorySlim(Types.Wallet wallet, HdAccount account, int skip = 0, int take = 100);

        /// <summary>
        /// Gets the balance of transactions contained in an account.
        /// If no account name is specified, balances will be returned for all accounts in the wallet.
        /// </summary>
        /// <param name="walletName">The wallet name.</param>
        /// <param name="accountName">The account name.</param>
        /// <param name="calculatSpendable">Whether to calculate also the spendable balance.</param>
        /// <returns>Collection of account balances.</returns>
        IEnumerable<AccountBalance> GetBalances(string walletName, string accountName = null, bool calculatSpendable = false);

        /// <summary>
        /// Gets the balance of transactions for this specific address.
        /// </summary>
        /// <param name="address">The address to get the balance from.</param>
        /// <returns>The address balance for an address.</returns>
        AddressBalance GetAddressBalance(string address);

        /// <summary>
        /// Gets some general information about a wallet.
        /// </summary>
        /// <param name="walletName">The name of the wallet.</param>
        /// <returns></returns>
        Types.Wallet GetWallet(string walletName);

        /// <summary>
        /// Gets a list of accounts.
        /// </summary>
        /// <param name="walletName">The name of the wallet to look into.</param>
        /// <returns></returns>
        IEnumerable<HdAccount> GetAccounts(string walletName);

        /// <summary>
        /// Gets the last block height.
        /// </summary>
        /// <returns></returns>
        int LastBlockHeight();

        /// <summary>
        /// Remove all the transactions in the wallet that are above this block height
        /// </summary>
        void RemoveBlocks(ChainedHeader fork);

        /// <summary>
        /// Processes a block received from the network.
        /// </summary>
        /// <param name="block">The block.</param>
        /// <param name="chainedHeader">The blocks chain of headers.</param>
        void ProcessBlock(Block block, ChainedHeader chainedHeader);

        /// <summary>
        /// Processes a transaction received from the network.
        /// </summary>
        /// <param name="transaction">The transaction.</param>
        /// <param name="blockHeight">The height of the block this transaction came from. Null if it was not a transaction included in a block.</param>
        /// <param name="block">The block in which this transaction was included.</param>
        /// <param name="isPropagated">Transaction propagation state.</param>
        /// <returns>A value indicating whether this transaction affects the wallet.</returns>
        bool ProcessTransaction(Transaction transaction, int? blockHeight = null, Block block = null, bool isPropagated = true);

        /// <summary>
        /// Saves the wallet into the file system.
        /// </summary>
        /// <param name="wallet">The wallet to save.</param>
        void SaveWallet(Types.Wallet wallet);

        /// <summary>
        /// Saves all the loaded wallets into the file system.
        /// </summary>
        void SaveWallets();

        /// <summary>
        /// Gets the extension of the wallet files.
        /// </summary>
        /// <returns></returns>
        string GetWalletFileExtension();

        /// <summary>
        /// Gets all the wallets' names.
        /// </summary>
        /// <returns>A collection of the wallets' names.</returns>
        IEnumerable<string> GetWalletsNames();

        /// <summary>
        /// Updates the wallet with the height of the last block synced.
        /// </summary>
        /// <param name="wallet">The wallet to update.</param>
        /// <param name="chainedHeader">The height of the last block synced.</param>
        void UpdateLastBlockSyncedHeight(Types.Wallet wallet, ChainedHeader chainedHeader);

        /// <summary>
        /// Updates all the loaded wallets with the height of the last block synced.
        /// </summary>
        /// <param name="chainedHeader">The height of the last block synced.</param>
        void UpdateLastBlockSyncedHeight(ChainedHeader chainedHeader);

        /// <summary>
        /// Gets a wallet given its name.
        /// </summary>
        /// <param name="walletName">The name of the wallet to get.</param>
        /// <returns>A wallet or null if it doesn't exist</returns>
        Types.Wallet GetWalletByName(string walletName);

        /// <summary>
        /// Gets the block locator of the first loaded wallet.
        /// </summary>
        /// <returns></returns>
        ICollection<uint256> GetFirstWalletBlockLocator();

        /// <summary>
        /// Gets the list of the wallet filenames, along with the folder in which they're contained.
        /// </summary>
        /// <returns>The wallet filenames, along with the folder in which they're contained.</returns>
        (string folderPath, IEnumerable<string>) GetWalletsFiles();

        /// <summary>
        /// Gets whether there are any wallet files loaded or not.
        /// </summary>
        /// <returns>Whether any wallet files are loaded.</returns>
        bool ContainsWallets { get; }

        /// <summary>
        /// Gets the extended public key of an account.
        /// </summary>
        /// <param name="accountReference">The account.</param>
        /// <returns>The extended public key.</returns>
        string GetExtPubKey(WalletAccountReference accountReference);

        /// <summary>
        /// Gets the extended private key of an account.
        /// </summary>
        /// <param name="accountReference">The account.</param>
        /// <param name="password">The password used to decrypt the encrypted seed.</param>
        /// <param name="cache">whether to cache the private key for future use.</param>
        /// <returns>The private key.</returns>
        ExtKey GetExtKey(WalletAccountReference accountReference, string password = "", bool cache = false);

        /// <summary>
        /// Gets the lowest LastBlockSyncedHeight of all loaded wallet account roots.
        /// </summary>
        /// <returns>The lowest LastBlockSyncedHeight or null if there are no account roots yet.</returns>
        int? GetEarliestWalletHeight();

        /// <summary>
        /// Gets the oldest wallet creation time.
        /// </summary>
        /// <returns></returns>
        DateTimeOffset GetOldestWalletCreationTime();

        /// <summary>
        /// Removes the specified transactions from the wallet and persist it.
        /// </summary>
        /// <param name="walletName">The name of the wallet to remove transactions from.</param>
        /// <param name="transactionsIds">The IDs of transactions to remove.</param>
        /// <returns>A list of objects made up of a transactions ID along with the time at which they were created.</returns>
        HashSet<(uint256, DateTimeOffset)> RemoveTransactionsByIds(string walletName, IEnumerable<uint256> transactionsIds);

        /// <summary>
        /// Removes all the transactions from the wallet and persist it.
        /// </summary>
        /// <param name="walletName">The name of the wallet to remove transactions from.</param>
        /// <returns>A list of objects made up of a transactions ID along with the time at which they were created.</returns>
        HashSet<(uint256, DateTimeOffset)> RemoveAllTransactions(string walletName);

        /// <summary>
        /// Removes all the transactions that occurred after a certain date.
        /// </summary>
        /// <param name="walletName">The name of the wallet to remove transactions from.</param>
        /// <param name="fromDate">The date after which the transactions should be removed.</param>
        /// <returns>A list of objects made up of a transactions ID along with the time at which they were created.</returns>
        HashSet<(uint256, DateTimeOffset)> RemoveTransactionsFromDate(string walletName, DateTimeOffset fromDate);

        /// <summary>
        /// Sweeps the funds from the private keys to the destination address.
        /// </summary>
        /// <param name="privateKeys">Private keys to sweep funds from in wif format.</param>
        /// <param name="destAddress">Destination address to sweep funds to.</param>
        /// <param name="broadcast">Broadcast the transaction to the network.</param>
        /// <returns>List of sweep transactions.</returns>
        IEnumerable<string> Sweep(IEnumerable<string> privateKeys, string destAddress, bool broadcast);
    }
}