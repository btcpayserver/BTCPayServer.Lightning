using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace BTCPayServer.Lightning.TestFramework
{
    public class DockerComposeDefinition
    {
        public class FragmentData
        {
            public string Content { get; set;}
            public string ID { get; set; }
        }

        public DockerComposeDefinition(string name)
        {
            Name = name;
        }

        public string SubstitutionBlob { get; set; } = @"###";
        public string Name { get; }
        public List<FragmentData> Fragments { get; } = new List<FragmentData>();

        public string BuildOutput { get; set; }

        public void AddFragmentFile(string file, string id)
        {
            Fragments.Add(new FragmentData {Content = File.ReadAllText(file), ID = id});
        }

        public string GetFilePath() => BuildOutput;

        private IEnumerable<KeyValuePair<YamlNode, YamlNode>> GetChildrenOf(IEnumerable<YamlMappingNode> origin, string topic)
            => origin
                .Where(n => n.Children.ContainsKey(topic) && n.Children[topic] is YamlMappingNode)
                .SelectMany(n => ((YamlMappingNode)n.Children[topic]).Children.ToList());

        /// <summary>
        /// filter child for YamlMappingNode with predicate.
        /// This function is recursive and it applies to all children if the node is mapping.
        /// </summary>
        /// <param name="predicate"></param>
        /// <param name="node"></param>
        /// <returns></returns>
        private YamlMappingNode PruneMappingNode(Func<KeyValuePair<YamlNode, YamlNode>, bool> predicate, YamlMappingNode node)
            => new YamlMappingNode(node.Children
                .Where(predicate)
                .Select(c =>
                    (c.Value is YamlMappingNode cMapping) ?
                    (new KeyValuePair<YamlNode, YamlNode>(c.Key, PruneMappingNode(predicate, cMapping))) :
                    c));

        public void Build(Func<KeyValuePair<YamlNode, YamlNode>, bool> fragmentFilterPredicate)
        {
            var deserializer = new DeserializerBuilder().Build();
            var serializer = new SerializerBuilder().Build();
            var contents = Fragments
                .Select(f => ParseDocument(f.Content, f.ID))
                .Select(c => PruneMappingNode(fragmentFilterPredicate, c));
            var services = GetChildrenOf(contents, "services");
            var volumes = GetChildrenOf(contents, "volumes");

            YamlMappingNode output = new YamlMappingNode();
            output.Add("version", new YamlScalarNode("3") { Style = YamlDotNet.Core.ScalarStyle.DoubleQuoted });
            output.Add("services", new YamlMappingNode(services));
            output.Add("volumes", new YamlMappingNode(volumes));
            var result = serializer.Serialize(output);
            var outputFile = GetFilePath();
            File.WriteAllText(outputFile, result.Replace("''", ""));
        }
        public void Build() => Build(_ => true);

        private string PreProcessText(string input, string id)
        {
            return Regex.Replace(input, SubstitutionBlob, id);
        }
        private YamlMappingNode ParseDocument(string fragment, string identifier)
        {
            var input = new StringReader(PreProcessText(fragment, identifier));
            YamlStream stream = new YamlStream();
            stream.Load(input);
            return (YamlMappingNode)stream.Documents[0].RootNode;
        }

    }
}