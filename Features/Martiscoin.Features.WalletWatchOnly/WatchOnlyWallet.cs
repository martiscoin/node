﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Martiscoin.Consensus.ScriptInfo;
using Martiscoin.NBitcoin;
using Martiscoin.Networks;
using Newtonsoft.Json;
using Martiscoin.Utilities.JsonConverters;

namespace Martiscoin.Features.WalletWatchOnly
{
    /// <summary>
    /// Represents a watch-only wallet.
    /// </summary>
    public class WatchOnlyWallet
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WatchOnlyWallet"/> class.
        /// </summary>
        public WatchOnlyWallet()
        {
            this.WatchedAddresses = new ConcurrentDictionary<string, WatchedAddress>();
            this.WatchedTransactions = new ConcurrentDictionary<string, WatchTransactionData>();
        }

        /// <summary>
        /// The network this wallet is for.
        /// </summary>
        [JsonProperty(PropertyName = "network")]
        [JsonConverter(typeof(NetworkConverter))]
        public Network Network { get; set; }

        /// <summary>
        /// The type of coin, Bitcoin or Stratis.
        /// </summary>
        [JsonProperty(PropertyName = "coinType")]
        public int CoinType { get; set; }

        /// <summary>
        /// The time this wallet was created.
        /// </summary>
        [JsonProperty(PropertyName = "creationTime")]
        [JsonConverter(typeof(DateTimeOffsetConverter))]
        public DateTimeOffset CreationTime { get; set; }

        /// <summary>
        /// The list of addresses being watched.
        /// </summary>
        [JsonProperty(PropertyName = "watchedAddresses")]
        [JsonConverter(typeof(WatchedAddressesConcurrentDictionaryConverter))]
        public ConcurrentDictionary<string, WatchedAddress> WatchedAddresses { get; set; }

        /// <summary>
        /// The list of transactions being watched.
        /// </summary>
        [JsonProperty(PropertyName = "watchedTransactions")]
        [JsonConverter(typeof(TransactionDataConcurrentDictionaryConverter))]
        public ConcurrentDictionary<string, WatchTransactionData> WatchedTransactions { get; set; }

        /// <summary>
        /// Returns a dictionary of all the transactions being watched (both under addresses
        /// and standalone).
        /// </summary>
        public ConcurrentDictionary<uint256, WatchTransactionData> GetWatchedTransactions()
        {
            var txDict = new ConcurrentDictionary<uint256, WatchTransactionData>();

            foreach (WatchedAddress address in this.WatchedAddresses.Values)
            {
                foreach (WatchTransactionData transaction in address.Transactions.Values)
                {
                    txDict.TryAdd(transaction.Id, transaction);
                }
            }

            foreach (WatchTransactionData transaction in this.WatchedTransactions.Values)
            {
                // It is conceivable that a transaction could be both watched
                // in isolation and watched as a transaction under one or
                // more watched addresses.
                if (!txDict.TryAdd(transaction.Id, transaction))
                {
                    // Check to see if there is better information in
                    // the watched transaction than the watched address.
                    // If there is, use the watched transaction info instead.

                    WatchTransactionData existingTx = txDict[transaction.Id];

                    if (existingTx.MerkleProof == null)
                    {
                        existingTx.MerkleProof = transaction.MerkleProof;
                    }

                    if (existingTx.BlockHash == null)
                    {
                        existingTx.BlockHash = transaction.BlockHash;
                    }

                    // At this stage the transaction info in txDict should
                    // include the best available information from both
                    // sources. There is therefore no need to explicitly
                    // update txDict.
                }
            }

            return txDict;
        }
    }

    /// <summary>
    /// An object containing a <see cref="Script"/> being watched along with any transactions affecting it.
    /// </summary>
    public class WatchedAddress
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WatchedAddress"/> class.
        /// </summary>
        public WatchedAddress()
        {
            this.Transactions = new ConcurrentDictionary<string, WatchTransactionData>();
        }

        /// <summary>
        /// A <see cref="Script"/> being watched for transactions affecting it.
        /// </summary>
        [JsonProperty(PropertyName = "script")]
        [JsonConverter(typeof(ScriptJsonConverter))]
        public Script Script { get; set; }

        /// <summary>
        /// A base58 address being watched for transactions affecting it.
        /// </summary>
        /// <remarks>
        /// This is a convenience property whose intrinsic value is equal to <see cref="WatchedAddress.Script"/>.
        /// </remarks>
        [JsonProperty(PropertyName = "address")]
        public string Address { get; set; }

        /// <summary>
        /// The list of transactions affecting the <see cref="Script"/> being watched.
        /// </summary>
        [JsonProperty(PropertyName = "transactions")]
        [JsonConverter(typeof(TransactionDataConcurrentDictionaryConverter))]
        public ConcurrentDictionary<string, WatchTransactionData> Transactions { get; set; }
    }

    /// <summary>
    /// An object containing the details of a transaction affecting a <see cref="Script"/> being watched.
    /// </summary>
    public class WatchTransactionData
    {
        /// <summary>
        /// Transaction id.
        /// </summary>
        [JsonProperty(PropertyName = "id")]
        [JsonConverter(typeof(UInt256JsonConverter))]
        public uint256 Id { get; set; }

        /// <summary>
        /// Hexadecimal representation of a transaction affecting a script being watched.
        /// </summary>
        [JsonProperty(PropertyName = "hex")]
        public string Hex { get; set; }

        /// <summary>
        /// The hash of the block including this transaction.
        /// </summary>
        [JsonProperty(PropertyName = "blockHash", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(UInt256JsonConverter))]
        public uint256 BlockHash { get; set; }

        /// <summary>
        /// Gets or sets the Merkle proof for this transaction.
        /// </summary>
        [JsonProperty(PropertyName = "merkleProof", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(BitcoinSerializableJsonConverter))]
        public PartialMerkleTree MerkleProof { get; set; }
    }

    /// <summary>
    /// Converter used to convert a <see cref="ConcurrentDictionary{TKey,TValue}"/> (where TKey is <see cref="string"/> and TValue is <see cref="WatchedAddress"/>) to and from a collection of its values.
    /// </summary>
    /// <seealso cref="Newtonsoft.Json.JsonConverter" />
    public class WatchedAddressesConcurrentDictionaryConverter : JsonConverter
    {
        /// <inheritdoc />
        public override bool CanConvert(Type objectType)
        {
            // Check this is a ConcurrentDictionary with the right argument types.
            return objectType == typeof(ConcurrentDictionary<string, WatchedAddress>);
        }

        /// <inheritdoc />
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var watchedAddesses = serializer.Deserialize<IEnumerable<WatchedAddress>>(reader);

            var watchedAddressesDictionary = new ConcurrentDictionary<string, WatchedAddress>();
            foreach (WatchedAddress watchedAddress in watchedAddesses)
            {
                watchedAddressesDictionary.TryAdd(watchedAddress.Script.ToString(), watchedAddress);
            }

            return watchedAddressesDictionary;
        }

        /// <inheritdoc />
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var watchedAddressesDictionary = (ConcurrentDictionary<string, WatchedAddress>)value;
            serializer.Serialize(writer, watchedAddressesDictionary.Values);
        }
    }

    /// <summary>
    /// Converter used to convert a <see cref="ConcurrentDictionary{TKey,TValue}"/> (where TKey is <see cref="string"/> and TValue is <see cref="WatchTransactionData"/>) to and from a collection of its values.
    /// </summary>
    /// <seealso cref="Newtonsoft.Json.JsonConverter" />
    public class TransactionDataConcurrentDictionaryConverter : JsonConverter
    {
        /// <inheritdoc />
        public override bool CanConvert(Type objectType)
        {
            // Check this is a ConcurrentDictionary with the right argument types.
            return objectType == typeof(ConcurrentDictionary<string, WatchTransactionData>);
        }

        /// <inheritdoc />
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var transactions = serializer.Deserialize<IEnumerable<WatchTransactionData>>(reader);

            var transactionsDictionary = new ConcurrentDictionary<string, WatchTransactionData>();
            foreach (WatchTransactionData transactionData in transactions)
            {
                transactionsDictionary.TryAdd(transactionData.Id.ToString(), transactionData);
            }

            return transactionsDictionary;
        }

        /// <inheritdoc />
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var transactionsDictionary = (ConcurrentDictionary<string, WatchTransactionData>)value;
            serializer.Serialize(writer, transactionsDictionary.Values);
        }
    }
}
