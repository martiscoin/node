﻿using System.Collections.Generic;
using Martiscoin.NBitcoin;
using Newtonsoft.Json;

namespace Martiscoin.Features.Miner.Api.Models
{
    /// <summary>
    /// Represents a list of blocks generated through mining, as an API return object.
    /// </summary>
    public class GenerateBlocksModel
    {
        /// <summary>
        /// The list of blocks mined.
        /// </summary>
        [JsonProperty(PropertyName = "blocks")]
        public IList<uint256> Blocks { get; set; }
    }
}
