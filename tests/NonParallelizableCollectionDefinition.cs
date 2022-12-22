using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace BTCPayServer.Lightning.Tests
{
    [CollectionDefinition(nameof(NonParallelizableCollectionDefinition), DisableParallelization = true)]
    internal class NonParallelizableCollectionDefinition
    {
    }
}
