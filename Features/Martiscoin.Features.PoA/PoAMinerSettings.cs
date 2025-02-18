﻿using Martiscoin.Configuration;
using Martiscoin.Utilities;

namespace Martiscoin.Features.PoA
{
    public class PoAMinerSettings
    {
        /// <summary>Allows mining in case node is in IBD and not connected to anyone.</summary>
        public bool BootstrappingMode { get; private set; }

        public PoAMinerSettings(NodeSettings nodeSettings)
        {
            Guard.NotNull(nodeSettings, nameof(nodeSettings));

            TextFileConfiguration config = nodeSettings.ConfigReader;

            this.BootstrappingMode = config.GetOrDefault<bool>("bootstrap", false);
        }
    }
}
