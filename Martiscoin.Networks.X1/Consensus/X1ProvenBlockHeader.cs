using Martiscoin.Consensus.BlockInfo;

namespace Martiscoin.Networks.X1.Consensus
{
    public class X1ProvenBlockHeader : ProvenBlockHeader
    {
        public X1ProvenBlockHeader()
        {
        }

        public X1ProvenBlockHeader(PosBlock block, X1BlockHeader x1BlockHeader) : base(block, x1BlockHeader)
        {
        }
    }
}