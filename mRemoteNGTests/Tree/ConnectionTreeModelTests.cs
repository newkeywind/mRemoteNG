using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NUnit.Framework;


namespace mRemoteNGTests.Tree
{
    public class ConnectionTreeModelTests
    {
        private ConnectionTreeModel _connectionTreeModel;

        [SetUp]
        public void Setup()
        {
            _connectionTreeModel = new ConnectionTreeModel();
        }

        [TearDown]
        public void Teardown()
        {
            _connectionTreeModel = null;
        }

        [Test]
        public void GetChildListProvidesAllChildren()
        {
            var root = new ContainerInfo();
            var folder1 = new ContainerInfo();
            var folder2 = new ContainerInfo();
            var con1 = new ConnectionInfo();
            root.AddChild(folder1);
            folder1.AddChild(folder2);
            root.AddChild(con1);
            _connectionTreeModel.AddRootNode(root);
            var connectionList = _connectionTreeModel.GetRecursiveChildList(root);
            Assert.That(connectionList, Is.EquivalentTo(new[] {folder1,folder2,con1}));
        }

        [Test]
        public void RenamingRootNodeRaisesModelPropertyChanged()
        {
            var root = new RootNodeInfo(RootNodeType.Connection);
            _connectionTreeModel.AddRootNode(root);
            string changedProperty = null;
            _connectionTreeModel.PropertyChanged += (_, e) => changedProperty = e.PropertyName;
            _connectionTreeModel.RenameNode(root, "Renamed Root");
            Assert.That(changedProperty, Is.EqualTo("Name"));
        }
    }
}