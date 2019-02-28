using System.IO;
using BTCPayServer.Lightning.TestFramework;
using Xunit;

namespace tests
{
    public class TestFrameworkUnitTest
    {
        
        [Fact]
        public void CanMergeComposeContent()
        {
            var path = FlexibleTesterBuilder.FindFragmentsLocation();
            var def = new DockerComposeDefinition("test1");
            def.AddFragmentFile(Path.Combine(path, "main-fragment.yml"), "fragment_number1");
            def.Build();
        }
    }
}