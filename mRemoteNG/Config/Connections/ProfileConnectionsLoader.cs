using System.Diagnostics;
using System.Runtime.Versioning;
using mRemoteNG.App;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Config.UserProfiles;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Config.Connections
{
    [SupportedOSPlatform("windows")]
    internal sealed class ProfileConnectionsLoader(IConnectionProfileDataProvider dataProvider) : IConnectionsLoader
    {
        public ConnectionTreeModel Load()
        {
            Stopwatch blobSw = PerfLogger.Start();
            string serializedConnections = dataProvider.Load();
            PerfLogger.Log("Load: profile blob read+decrypt", blobSw);
            if (string.IsNullOrEmpty(serializedConnections))
            {
                ConnectionTreeModel model = new();
                model.AddRootNode(new RootNodeInfo(RootNodeType.Connection));
                return model;
            }
            return new XmlConnectionsDeserializer().Deserialize(serializedConnections);
        }
    }
}
