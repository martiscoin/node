﻿using System.Collections.Generic;
using Martiscoin.NBitcoin;

namespace Martiscoin.Features.RPC
{
    public class AddressGrouping
    {
        public AddressGrouping()
        {
            this.ChangeAddresses = new List<ChangeAddress>();
        }

        public BitcoinAddress PublicAddress { get; set; }
        public Money Amount { get; set; }
        public string Account { get; set; }
        public List<ChangeAddress> ChangeAddresses { get; set; }
    }
}