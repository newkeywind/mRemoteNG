using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using mRemoteNG.App;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Connection.Protocol.Http;
using mRemoteNG.Connection.Protocol.RDP;
using mRemoteNG.Connection.Protocol.VNC;
using mRemoteNG.Container;
using mRemoteNG.Messages;
using mRemoteNG.Plugins;
using mRemoteNG.Security;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using mRemoteNG.UI.Forms;
using mRemoteNG.UI.TaskDialog;
using mRemoteNG.Resources.Language;
using System.Runtime.Versioning;

namespace mRemoteNG.Config.Serializers.ConnectionSerializers.Xml
{
    [SupportedOSPlatform("windows")]
    public class XmlConnectionsDeserializer(Func<Optional<SecureString>>? authenticationRequestor = null) : IDeserializer<string, ConnectionTreeModel>
    {
        private XmlDocument _xmlDocument;
        private double _confVersion;
        private XmlConnectionsDecryptor _decryptor;
        private readonly string ConnectionFileName = "";
        private const double MaxSupportedConfVersion = 2.8;
        private readonly RootNodeInfo _rootNodeInfo = new(RootNodeType.Connection);
        private readonly Stopwatch _swAttrs = new();
        private readonly Stopwatch _swGetInfo = new();
        private readonly Stopwatch _swCtor = new();
        private readonly Stopwatch _swDecrypt = new();
        private readonly Stopwatch _swCopyFrom = new();
        private readonly Stopwatch _swAddChild = new();
        private int _nodeConnectionCount;
        private int _nodeContainerCount;
        private readonly List<(string Field, string Cipher, ConnectionInfo Target)> _pendingDecrypts = new();
        private ThreadLocal<XmlConnectionsDecryptor>? _perThreadDecryptor;
        private BlockCipherEngines _cryptoEngine;
        private BlockCipherModes _cryptoMode;
        private int _keyDerivationIterations;

        public Func<Optional<SecureString>>? AuthenticationRequestor { get; set; } = authenticationRequestor;

        public ConnectionTreeModel Deserialize(string xml)
        {
            Stopwatch sw = PerfLogger.Start();
            ConnectionTreeModel model = Deserialize(xml, false);
            PerfLogger.Log("Load: XML parse (XmlConnectionsDeserializer)", sw);
            return model;
        }

        public ConnectionTreeModel Deserialize(string xml, bool import)
        {
            if (string.IsNullOrEmpty(xml)) return null;
            try
            {
                Stopwatch swDocumentLoad = PerfLogger.Start();
                LoadXmlConnectionData(xml);
                PerfLogger.Log("Load: XML parse: document load", swDocumentLoad);
                ValidateConnectionFileVersion();

                XmlElement rootXmlElement = _xmlDocument.DocumentElement;
                InitializeRootNode(rootXmlElement);
                CreateDecryptor(_rootNodeInfo, rootXmlElement);
                ConnectionTreeModel connectionTreeModel = new();
                connectionTreeModel.AddRootNode(_rootNodeInfo);


                if (_confVersion > 1.3)
                {
                    string protectedString = _xmlDocument.DocumentElement?.Attributes["Protected"]?.Value;
                    if (!_decryptor.ConnectionsFileIsAuthentic(protectedString, _rootNodeInfo.PasswordString.ConvertToSecureString()))
                    {
                        return null;
                    }
                }

                if (_confVersion >= 2.6)
                {
                    bool fullFileEncryptionValue = rootXmlElement.GetAttributeAsBool("FullFileEncryption");
                    if (fullFileEncryptionValue)
                    {
                        string decryptedContent = _decryptor.Decrypt(rootXmlElement.InnerText);
                        rootXmlElement.InnerXml = decryptedContent;
                    }
                }

                ResetNodeWalkAccumulators();
                Stopwatch swNodeWalk = PerfLogger.Start();
                AddNodesFromXmlRecursive(_xmlDocument.DocumentElement, _rootNodeInfo);
                FlushPendingDecrypts();
                PerfLogger.Log("Load: XML parse: node walk", swNodeWalk);
                LogNodeWalkBreakdown(swNodeWalk);

                if (!import)
                    Runtime.ConnectionsService.IsConnectionsFileLoaded = true;

                return connectionTreeModel;
            }
            catch (Exception ex)
            {
                Runtime.ConnectionsService.IsConnectionsFileLoaded = false;
                Runtime.MessageCollector.AddExceptionStackTrace(Language.LoadFromXmlFailed, ex);
                throw;
            }
        }

        private void LoadXmlConnectionData(string connections)
        {
            CreateDecryptor(new RootNodeInfo(RootNodeType.Connection));
            connections = _decryptor.LegacyFullFileDecrypt(connections);
            if (connections != "")
            {
                _xmlDocument = SecureXmlHelper.LoadXmlFromString(connections);
            }
        }

        private void ValidateConnectionFileVersion()
        {
            if (_xmlDocument.DocumentElement != null && _xmlDocument.DocumentElement.HasAttribute("ConfVersion"))
                _confVersion = Convert.ToDouble(_xmlDocument.DocumentElement.Attributes["ConfVersion"]?.Value.Replace(",", "."), CultureInfo.InvariantCulture);
            else
                Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg, Language.OldConffile);

            if (!(_confVersion > MaxSupportedConfVersion)) return;
            ShowIncompatibleVersionDialogBox();
            throw new Exception($"Incompatible connection file format (file format version {_confVersion}).");
        }

        private void ShowIncompatibleVersionDialogBox()
        {
            CTaskDialog.ShowTaskDialogBox(FrmMain.Default, Application.ProductName, "Incompatible connection file format", $"The format of this connection file is not supported. Please upgrade to a newer version of {Application.ProductName}.",
                                          string .Format("{1}{0}File Format Version: {2}{0}Highest Supported Version: {3}", Environment.NewLine, ConnectionFileName, _confVersion, MaxSupportedConfVersion),
                                          "", "", "", "", ETaskDialogButtons.Ok, ESysIcons.Error, ESysIcons.Error);
        }

        private void InitializeRootNode(XmlElement connectionsRootElement)
        {
            _rootNodeInfo.Name = connectionsRootElement?.Attributes["Name"]?.Value.Trim();
        }

        private void CreateDecryptor(RootNodeInfo rootNodeInfo, XmlElement? connectionsRootElement = null)
        {
            if (_confVersion >= 2.6)
            {
                BlockCipherEngines engine = connectionsRootElement.GetAttributeAsEnum<BlockCipherEngines>("EncryptionEngine");
                BlockCipherModes mode = connectionsRootElement.GetAttributeAsEnum<BlockCipherModes>("BlockCipherMode");
                int keyDerivationIterations = connectionsRootElement.GetAttributeAsInt("KdfIterations");
                _cryptoEngine = engine;
                _cryptoMode = mode;
                _keyDerivationIterations = keyDerivationIterations;

                _decryptor = new XmlConnectionsDecryptor(engine, mode, rootNodeInfo)
                {
                    AuthenticationRequestor = AuthenticationRequestor,
                    KeyDerivationIterations = keyDerivationIterations
                };
            }
            else
            {
                _decryptor = new XmlConnectionsDecryptor(_rootNodeInfo)
                {
                    AuthenticationRequestor = AuthenticationRequestor
                };
            }
        }

        private void AddNodesFromXmlRecursive(XmlNode parentXmlNode, ContainerInfo parentContainer)
        {
            try
            {
                if (!parentXmlNode.HasChildNodes) return;
                foreach (XmlNode xmlNode in parentXmlNode.ChildNodes)
                {
                    TreeNodeType nodeType = xmlNode.GetAttributeAsEnum("Type", TreeNodeType.Connection);

                    // ReSharper disable once SwitchStatementMissingSomeCases
                    switch (nodeType)
                    {
                        case TreeNodeType.Connection:
                            _nodeConnectionCount++;
                            _swGetInfo.Start();
                            ConnectionInfo connectionInfo = GetConnectionInfoFromXml(xmlNode);
                            _swGetInfo.Stop();
                            _swAddChild.Start();
                            parentContainer.AddChild(connectionInfo);
                            _swAddChild.Stop();
                            break;
                        case TreeNodeType.Container:
                            _nodeContainerCount++;
                            _swCtor.Start();
                            ContainerInfo containerInfo = new();
                            _swCtor.Stop();

                            if (_confVersion >= 0.9)
                            {
                                _swCopyFrom.Start();
                                ConnectionInfo tempInfo = GetConnectionInfoFromXml(xmlNode);
                                containerInfo.CopyFrom(tempInfo);
                                _swCopyFrom.Stop();
                                CarryOverDecryptedFields(tempInfo, containerInfo);
                            }
                            if (_confVersion >= 0.8)
                            {
                                containerInfo.IsExpanded = xmlNode.GetAttributeAsBool("Expanded");
                                containerInfo.IsRootGroup = xmlNode.GetAttributeAsBool("IsRootGroup");
                            }

                            _swAddChild.Start();
                            parentContainer.AddChild(containerInfo);
                            _swAddChild.Stop();
                            AddNodesFromXmlRecursive(xmlNode, containerInfo);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.AddNodeFromXmlFailed, ex);
                throw;
            }
        }

        private ConnectionInfo GetConnectionInfoFromXml(XmlNode xmlnode)
        {
            if (xmlnode?.Attributes == null)
                return null;

            _swAttrs.Start();
            XmlNodeAttributeReader attrs = new(xmlnode);
            _swAttrs.Stop();
            string connectionId = attrs.GetAttributeAsString("Id");
            if (string.IsNullOrWhiteSpace(connectionId))
                connectionId = Guid.NewGuid().ToString();
            _swCtor.Start();
            ConnectionInfo connectionInfo = new(connectionId);
            _swCtor.Stop();

            try
            {
                if (_confVersion >= 0.2)
                {
                    connectionInfo.Name = attrs.GetAttributeAsString("Name");
                    connectionInfo.Description = attrs.GetAttributeAsString("Descr");
                    connectionInfo.Hostname = attrs.GetAttributeAsString("Hostname");
                    connectionInfo.DisplayWallpaper = attrs.GetAttributeAsBool("DisplayWallpaper");
                    connectionInfo.DisplayThemes = attrs.GetAttributeAsBool("DisplayThemes");
                    connectionInfo.CacheBitmaps = attrs.GetAttributeAsBool("CacheBitmaps");

                    if (_confVersion < 1.1) //1.0 - 0.1
                    {
                        connectionInfo.Resolution = attrs.GetAttributeAsBool("Fullscreen")
                            ? RDPResolutions.Fullscreen
                            : RDPResolutions.SmartSize;
                    }

                    if (!Runtime.UseCredentialManager || _confVersion <= 2.6) // 0.2 - 2.6
                    {
                        connectionInfo.Username = attrs.GetAttributeAsString("Username");
                        DeferDecrypt(connectionInfo, "Password", attrs.GetAttributeAsString("Password"));
                        connectionInfo.Domain = attrs.GetAttributeAsString("Domain");
                    }
                }

                if (_confVersion >= 0.3)
                {
                    if (_confVersion < 0.7)
                    {
                        if (attrs.GetAttributeAsBool("UseVNC"))
                        {
                            connectionInfo.Protocol = ProtocolType.VNC;
                            connectionInfo.Port = attrs.GetAttributeAsInt("VNCPort");
                        }
                        else
                        {
                            connectionInfo.Protocol = ProtocolType.RDP;
                        }
                    }
                }
                else
                {
                    connectionInfo.Port = (int)RdpProtocol.Defaults.Port;
                    connectionInfo.Protocol = ProtocolType.RDP;
                }

                if (_confVersion >= 0.4)
                {
                    if (_confVersion < 0.7)
                    {
                        connectionInfo.Port = attrs.GetAttributeAsBool("UseVNC")
                            ? attrs.GetAttributeAsInt("VNCPort")
                            : attrs.GetAttributeAsInt("RDPPort");
                    }

                    connectionInfo.UseConsoleSession = attrs.GetAttributeAsBool("ConnectToConsole");
                }
                else
                {
                    if (_confVersion < 0.7)
                    {
                        if (attrs.GetAttributeAsBool("UseVNC"))
                            connectionInfo.Port = (int)ProtocolVNC.Defaults.Port;
                        else
                            connectionInfo.Port = (int)RdpProtocol.Defaults.Port;
                    }

                    connectionInfo.UseConsoleSession = false;
                }

                if (_confVersion >= 0.5)
                {
                    connectionInfo.RedirectPrinters = attrs.GetAttributeAsBool("RedirectPrinters");
                    connectionInfo.RedirectPorts = attrs.GetAttributeAsBool("RedirectPorts");
                    connectionInfo.RedirectSmartCards = attrs.GetAttributeAsBool("RedirectSmartCards");
                }
                else
                {
                    connectionInfo.RedirectDiskDrives = RDPDiskDrives.None;
                    connectionInfo.RedirectPrinters = false;
                    connectionInfo.RedirectPorts = false;
                    connectionInfo.RedirectSmartCards = false;
                }

                if (_confVersion >= 0.7)
                {
                    connectionInfo.Protocol = attrs.GetAttributeAsEnum<ProtocolType>("Protocol");
                    connectionInfo.Port = attrs.GetAttributeAsInt("Port");
                }

                if (_confVersion >= 1.0)
                {
                    connectionInfo.RedirectKeys = attrs.GetAttributeAsBool("RedirectKeys");
                }

                if (_confVersion >= 1.2)
                {
                    connectionInfo.PuttySession = attrs.GetAttributeAsString("PuttySession");
                }

                if (_confVersion >= 1.3)
                {
                    connectionInfo.Colors = attrs.GetAttributeAsEnum<RDPColors>("Colors");
                    connectionInfo.Resolution = attrs.GetAttributeAsEnum<RDPResolutions>("Resolution");
                    connectionInfo.RedirectSound = attrs.GetAttributeAsEnum<RDPSounds>("RedirectSound");
                    connectionInfo.RedirectAudioCapture = attrs.GetAttributeAsBool("RedirectAudioCapture");
                }
                else
                {
                    connectionInfo.Colors = attrs.GetAttributeAsInt("Colors") switch
                    {
                        0 => RDPColors.Colors256,
                        1 => RDPColors.Colors16Bit,
                        2 => RDPColors.Colors24Bit,
                        3 => RDPColors.Colors32Bit,
                        // ReSharper disable once RedundantCaseLabel
                        _ => RDPColors.Colors15Bit,
                    };
                    connectionInfo.RedirectSound = attrs.GetAttributeAsEnum<RDPSounds>("RedirectSound");
                    connectionInfo.RedirectAudioCapture = attrs.GetAttributeAsBool("RedirectAudioCapture");
                }

                if (_confVersion >= 1.3)
                {
                    connectionInfo.Inheritance.CacheBitmaps = attrs.GetAttributeAsBool("InheritCacheBitmaps");
                    connectionInfo.Inheritance.Colors = attrs.GetAttributeAsBool("InheritColors");
                    connectionInfo.Inheritance.Description = attrs.GetAttributeAsBool("InheritDescription");
                    connectionInfo.Inheritance.DisplayThemes = attrs.GetAttributeAsBool("InheritDisplayThemes");
                    connectionInfo.Inheritance.DisplayWallpaper = attrs.GetAttributeAsBool("InheritDisplayWallpaper");
                    connectionInfo.Inheritance.Icon = attrs.GetAttributeAsBool("InheritIcon");
                    connectionInfo.Inheritance.Panel = attrs.GetAttributeAsBool("InheritPanel");
                    connectionInfo.Inheritance.TabColor = attrs.GetAttributeAsBool("InheritTabColor");
                    connectionInfo.Inheritance.ConnectionFrameColor = attrs.GetAttributeAsBool("InheritConnectionFrameColor");
                    connectionInfo.Inheritance.Port = attrs.GetAttributeAsBool("InheritPort");
                    connectionInfo.Inheritance.Protocol = attrs.GetAttributeAsBool("InheritProtocol");
                    connectionInfo.Inheritance.PuttySession = attrs.GetAttributeAsBool("InheritPuttySession");
                    connectionInfo.Inheritance.RedirectDiskDrives = attrs.GetAttributeAsBool("InheritRedirectDiskDrives");
                    connectionInfo.Inheritance.RedirectKeys = attrs.GetAttributeAsBool("InheritRedirectKeys");
                    connectionInfo.Inheritance.RedirectPorts = attrs.GetAttributeAsBool("InheritRedirectPorts");
                    connectionInfo.Inheritance.RedirectPrinters = attrs.GetAttributeAsBool("InheritRedirectPrinters");
                    connectionInfo.Inheritance.RedirectSmartCards = attrs.GetAttributeAsBool("InheritRedirectSmartCards");
                    connectionInfo.Inheritance.RedirectSound = attrs.GetAttributeAsBool("InheritRedirectSound");
                    connectionInfo.Inheritance.RedirectAudioCapture = attrs.GetAttributeAsBool("InheritRedirectAudioCapture");
                    connectionInfo.Inheritance.Resolution = attrs.GetAttributeAsBool("InheritResolution");
                    connectionInfo.Inheritance.UseConsoleSession = attrs.GetAttributeAsBool("InheritUseConsoleSession");

                    if (!Runtime.UseCredentialManager || _confVersion <= 2.6) // 1.3 - 2.6
                    {
                        connectionInfo.Inheritance.Domain = attrs.GetAttributeAsBool("InheritDomain");
                        connectionInfo.Inheritance.Password = attrs.GetAttributeAsBool("InheritPassword");
                        connectionInfo.Inheritance.Username = attrs.GetAttributeAsBool("InheritUsername");
                    }

                    connectionInfo.Icon = attrs.GetAttributeAsString("Icon");
                    connectionInfo.Panel = attrs.GetAttributeAsString("Panel");
                    connectionInfo.TabColor = attrs.GetAttributeAsString("TabColor");
                    connectionInfo.ConnectionFrameColor = attrs.GetAttributeAsEnum<ConnectionFrameColor>("ConnectionFrameColor");
                }
                else
                {
                    if (attrs.GetAttributeAsBool("Inherit"))
                        connectionInfo.Inheritance.TurnOnInheritanceCompletely();
                    connectionInfo.Icon = attrs.GetAttributeAsString("Icon").Replace(".ico", "");
                    connectionInfo.Panel = Language.General;
                }

                if (_confVersion >= 1.5)
                {
                    connectionInfo.PleaseConnect = attrs.GetAttributeAsBool("Connected");
                }

                if (_confVersion >= 1.6)
                {
                    connectionInfo.PreExtApp = attrs.GetAttributeAsString("PreExtApp");
                    connectionInfo.PostExtApp = attrs.GetAttributeAsString("PostExtApp");
                    connectionInfo.Inheritance.PreExtApp = attrs.GetAttributeAsBool("InheritPreExtApp");
                    connectionInfo.Inheritance.PostExtApp = attrs.GetAttributeAsBool("InheritPostExtApp");
                }

                if (_confVersion >= 1.7)
                {
                    connectionInfo.VNCCompression = attrs.GetAttributeAsEnum<ProtocolVNC.Compression>("VNCCompression");
                    connectionInfo.VNCEncoding = attrs.GetAttributeAsEnum<ProtocolVNC.Encoding>("VNCEncoding");
                    connectionInfo.VNCAuthMode = attrs.GetAttributeAsEnum<ProtocolVNC.AuthMode>("VNCAuthMode");
                    connectionInfo.VNCProxyType = attrs.GetAttributeAsEnum<ProtocolVNC.ProxyType>("VNCProxyType");
                    connectionInfo.VNCProxyIP = attrs.GetAttributeAsString("VNCProxyIP");
                    connectionInfo.VNCProxyPort = attrs.GetAttributeAsInt("VNCProxyPort");
                    connectionInfo.VNCProxyUsername = attrs.GetAttributeAsString("VNCProxyUsername");
                    DeferDecrypt(connectionInfo, "VNCProxyPassword", attrs.GetAttributeAsString("VNCProxyPassword"));
                    connectionInfo.VNCColors = attrs.GetAttributeAsEnum<ProtocolVNC.Colors>("VNCColors");
                    connectionInfo.VNCSmartSizeMode = attrs.GetAttributeAsEnum<ProtocolVNC.SmartSizeMode>("VNCSmartSizeMode");
                    connectionInfo.VNCViewOnly = attrs.GetAttributeAsBool("VNCViewOnly");
                    connectionInfo.Inheritance.VNCCompression = attrs.GetAttributeAsBool("InheritVNCCompression");
                    connectionInfo.Inheritance.VNCEncoding = attrs.GetAttributeAsBool("InheritVNCEncoding");
                    connectionInfo.Inheritance.VNCAuthMode = attrs.GetAttributeAsBool("InheritVNCAuthMode");
                    connectionInfo.Inheritance.VNCProxyType = attrs.GetAttributeAsBool("InheritVNCProxyType");
                    connectionInfo.Inheritance.VNCProxyIP = attrs.GetAttributeAsBool("InheritVNCProxyIP");
                    connectionInfo.Inheritance.VNCProxyPort = attrs.GetAttributeAsBool("InheritVNCProxyPort");
                    connectionInfo.Inheritance.VNCProxyUsername = attrs.GetAttributeAsBool("InheritVNCProxyUsername");
                    connectionInfo.Inheritance.VNCProxyPassword = attrs.GetAttributeAsBool("InheritVNCProxyPassword");
                    connectionInfo.Inheritance.VNCColors = attrs.GetAttributeAsBool("InheritVNCColors");
                    connectionInfo.Inheritance.VNCSmartSizeMode = attrs.GetAttributeAsBool("InheritVNCSmartSizeMode");
                    connectionInfo.Inheritance.VNCViewOnly = attrs.GetAttributeAsBool("InheritVNCViewOnly");
                }

                if (_confVersion >= 1.8)
                {
                    connectionInfo.RDPAuthenticationLevel = attrs.GetAttributeAsEnum<AuthenticationLevel>("RDPAuthenticationLevel");
                    connectionInfo.Inheritance.RDPAuthenticationLevel = attrs.GetAttributeAsBool("InheritRDPAuthenticationLevel");
                }

                if (_confVersion >= 1.9)
                {
                    connectionInfo.RenderingEngine = attrs.GetAttributeAsEnum<HTTPBase.RenderingEngine>("RenderingEngine");
                    connectionInfo.MacAddress = attrs.GetAttributeAsString("MacAddress");
                    connectionInfo.Inheritance.RenderingEngine = attrs.GetAttributeAsBool("InheritRenderingEngine");
                    connectionInfo.Inheritance.MacAddress = attrs.GetAttributeAsBool("InheritMacAddress");
                }

                if (_confVersion >= 2.0)
                {
                    connectionInfo.UserField = attrs.GetAttributeAsString("UserField");
                    connectionInfo.Inheritance.UserField = attrs.GetAttributeAsBool("InheritUserField");
                }

                if (_confVersion >= 2.1)
                {
                    connectionInfo.ExtApp = attrs.GetAttributeAsString("ExtApp");
                    connectionInfo.Inheritance.ExtApp = attrs.GetAttributeAsBool("InheritExtApp");
                }

                if (_confVersion >= 2.2)
                {
                    // Get settings
                    connectionInfo.RDGatewayUsageMethod = attrs.GetAttributeAsEnum<RDGatewayUsageMethod>("RDGatewayUsageMethod");
                    connectionInfo.RDGatewayHostname = attrs.GetAttributeAsString("RDGatewayHostname");
                    connectionInfo.RDGatewayUseConnectionCredentials = attrs.GetAttributeAsEnum<RDGatewayUseConnectionCredentials>("RDGatewayUseConnectionCredentials");
                    connectionInfo.RDGatewayUsername = attrs.GetAttributeAsString("RDGatewayUsername");
                    DeferDecrypt(connectionInfo, "RDGatewayPassword", attrs.GetAttributeAsString("RDGatewayPassword"));
                    DeferDecrypt(connectionInfo, "RDGatewayAccessToken", attrs.GetAttributeAsString("RDGatewayAccessToken"));
                    connectionInfo.RDGatewayDomain = attrs.GetAttributeAsString("RDGatewayDomain");

                    // Get inheritance settings
                    connectionInfo.Inheritance.RDGatewayUsageMethod = attrs.GetAttributeAsBool("InheritRDGatewayUsageMethod");
                    connectionInfo.Inheritance.RDGatewayHostname = attrs.GetAttributeAsBool("InheritRDGatewayHostname");
                    connectionInfo.Inheritance.RDGatewayUseConnectionCredentials = attrs.GetAttributeAsBool("InheritRDGatewayUseConnectionCredentials");
                    connectionInfo.Inheritance.RDGatewayUsername = attrs.GetAttributeAsBool("InheritRDGatewayUsername");
                    connectionInfo.Inheritance.RDGatewayPassword = attrs.GetAttributeAsBool("InheritRDGatewayPassword");
                    connectionInfo.Inheritance.RDGatewayAccessToken = attrs.GetAttributeAsBool("InheritRDGatewayAccessToken");
                    connectionInfo.Inheritance.RDGatewayDomain = attrs.GetAttributeAsBool("InheritRDGatewayDomain");
                }

                if (_confVersion >= 2.3)
                {
                    // Get settings
                    connectionInfo.EnableFontSmoothing = attrs.GetAttributeAsBool("EnableFontSmoothing");
                    connectionInfo.EnableDesktopComposition = attrs.GetAttributeAsBool("EnableDesktopComposition");

                    // Get inheritance settings
                    connectionInfo.Inheritance.EnableFontSmoothing = attrs.GetAttributeAsBool("InheritEnableFontSmoothing");
                    connectionInfo.Inheritance.EnableDesktopComposition = attrs.GetAttributeAsBool("InheritEnableDesktopComposition");
                }

                if (_confVersion >= 2.4)
                {
                    connectionInfo.UseCredSsp = attrs.GetAttributeAsBool("UseCredSsp");
                    connectionInfo.Inheritance.UseCredSsp = attrs.GetAttributeAsBool("InheritUseCredSsp");
                }

                if (_confVersion >= 2.5)
                {
                    connectionInfo.LoadBalanceInfo = attrs.GetAttributeAsString("LoadBalanceInfo");
                    connectionInfo.AutomaticResize = attrs.GetAttributeAsBool("AutomaticResize");
                    connectionInfo.Inheritance.LoadBalanceInfo = attrs.GetAttributeAsBool("InheritLoadBalanceInfo");
                    connectionInfo.Inheritance.AutomaticResize = attrs.GetAttributeAsBool("InheritAutomaticResize");
                }

                if (_confVersion >= 2.6)
                {
                    connectionInfo.SoundQuality = attrs.GetAttributeAsEnum<RDPSoundQuality>("SoundQuality");
                    connectionInfo.Inheritance.SoundQuality = attrs.GetAttributeAsBool("InheritSoundQuality");
                    connectionInfo.RDPMinutesToIdleTimeout = attrs.GetAttributeAsInt("RDPMinutesToIdleTimeout");
                    connectionInfo.Inheritance.RDPMinutesToIdleTimeout = attrs.GetAttributeAsBool("InheritRDPMinutesToIdleTimeout");
                    connectionInfo.RDPAlertIdleTimeout = attrs.GetAttributeAsBool("RDPAlertIdleTimeout");
                    connectionInfo.Inheritance.RDPAlertIdleTimeout = attrs.GetAttributeAsBool("InheritRDPAlertIdleTimeout");
                }

                if (_confVersion >= 2.7)
                {
                    connectionInfo.RedirectClipboard = attrs.GetAttributeAsBool("RedirectClipboard");
                    connectionInfo.Favorite = attrs.GetAttributeAsBool("Favorite");
                    connectionInfo.UseVmId = attrs.GetAttributeAsBool("UseVmId");
                    connectionInfo.VmId = attrs.GetAttributeAsString("VmId");
                    connectionInfo.UseEnhancedMode = attrs.GetAttributeAsBool("UseEnhancedMode");
                    connectionInfo.RdpVersion = attrs.GetAttributeAsEnum("RdpVersion", RdpVersion.Highest);
                    connectionInfo.SSHTunnelConnectionName = attrs.GetAttributeAsString("SSHTunnelConnectionName");
                    connectionInfo.OpeningCommand = attrs.GetAttributeAsString("OpeningCommand");
                    connectionInfo.SSHOptions = attrs.GetAttributeAsString("SSHOptions");
                    connectionInfo.RDPStartProgram = attrs.GetAttributeAsString("StartProgram");
                    connectionInfo.RDPStartProgramWorkDir = attrs.GetAttributeAsString("StartProgramWorkDir");
                    connectionInfo.DisableFullWindowDrag = attrs.GetAttributeAsBool("DisableFullWindowDrag");
                    connectionInfo.DisableMenuAnimations = attrs.GetAttributeAsBool("DisableMenuAnimations");
                    connectionInfo.DisableCursorShadow = attrs.GetAttributeAsBool("DisableCursorShadow");
                    connectionInfo.DisableCursorBlinking = attrs.GetAttributeAsBool("DisableCursorBlinking");
                    connectionInfo.RDPStartProgram = attrs.GetAttributeAsString("StartProgram");
                    connectionInfo.RDPStartProgramWorkDir = attrs.GetAttributeAsString("StartProgramWorkDir");
                    connectionInfo.Inheritance.RedirectClipboard = attrs.GetAttributeAsBool("InheritRedirectClipboard");
                    connectionInfo.Inheritance.Favorite = attrs.GetAttributeAsBool("InheritFavorite");
                    connectionInfo.Inheritance.RdpVersion = attrs.GetAttributeAsBool("InheritRdpVersion");
                    connectionInfo.Inheritance.UseVmId = attrs.GetAttributeAsBool("InheritUseVmId");
                    connectionInfo.Inheritance.VmId = attrs.GetAttributeAsBool("InheritVmId");
                    connectionInfo.Inheritance.UseEnhancedMode = attrs.GetAttributeAsBool("InheritUseEnhancedMode");
                    connectionInfo.Inheritance.SSHTunnelConnectionName = attrs.GetAttributeAsBool("InheritSSHTunnelConnectionName");
                    connectionInfo.Inheritance.OpeningCommand = attrs.GetAttributeAsBool("InheritOpeningCommand");
                    connectionInfo.Inheritance.SSHOptions = attrs.GetAttributeAsBool("InheritSSHOptions");
                    connectionInfo.Inheritance.DisableFullWindowDrag = attrs.GetAttributeAsBool("InheritDisableFullWindowDrag");
                    connectionInfo.Inheritance.DisableMenuAnimations = attrs.GetAttributeAsBool("InheritDisableMenuAnimations");
                    connectionInfo.Inheritance.DisableCursorShadow = attrs.GetAttributeAsBool("InheritDisableCursorShadow");
                    connectionInfo.Inheritance.DisableCursorBlinking = attrs.GetAttributeAsBool("InheritDisableCursorBlinking");
                    connectionInfo.ExternalCredentialProvider = attrs.GetAttributeAsEnum("ExternalCredentialProvider", ExternalCredentialProvider.None);
                    connectionInfo.Inheritance.ExternalCredentialProvider = attrs.GetAttributeAsBool("InheritExternalCredentialProvider");
                    connectionInfo.UserViaAPI = attrs.GetAttributeAsString("UserViaAPI");
                    connectionInfo.Inheritance.UserViaAPI = attrs.GetAttributeAsBool("InheritUserViaAPI");
                    connectionInfo.ExternalAddressProvider = attrs.GetAttributeAsEnum("ExternalAddressProvider", ExternalAddressProvider.None);
                    connectionInfo.VaultOpenbaoMount = attrs.GetAttributeAsString("VaultOpenbaoMount");
                    connectionInfo.VaultOpenbaoRole = attrs.GetAttributeAsString("VaultOpenbaoRole");
                    connectionInfo.VaultOpenbaoSecretEngine = attrs.GetAttributeAsEnum("VaultOpenbaoSecretEngine", VaultOpenbaoSecretEngine.Kv);
                    connectionInfo.EC2InstanceId = attrs.GetAttributeAsString("EC2InstanceId");
                    connectionInfo.EC2Region = attrs.GetAttributeAsString("EC2Region");
                    connectionInfo.ReplacePluginProperties(PluginConnectionDataSerializer.Deserialize((xmlnode as XmlElement)?["PluginData"]?.InnerText ?? string.Empty));
                    LegacyPluginDataMigrator.Migrate(connectionInfo);
                    connectionInfo.UseRestrictedAdmin = attrs.GetAttributeAsBool("UseRestrictedAdmin");
                    connectionInfo.Inheritance.UseRestrictedAdmin = attrs.GetAttributeAsBool("InheritUseRestrictedAdmin");
                    connectionInfo.UseRCG = attrs.GetAttributeAsBool("UseRCG");
                    connectionInfo.Inheritance.UseRCG = attrs.GetAttributeAsBool("InheritUseRCG");
                    connectionInfo.UseRedirectionServerName = attrs.GetAttributeAsBool("UseRedirectionServerName");
                    connectionInfo.Inheritance.UseRedirectionServerName = attrs.GetAttributeAsBool("InheritUseRedirectionServerName");
                    connectionInfo.RDGatewayExternalCredentialProvider = attrs.GetAttributeAsEnum("RDGatewayExternalCredentialProvider", ExternalCredentialProvider.None);
                    connectionInfo.RDGatewayUserViaAPI = attrs.GetAttributeAsString("RDGatewayUserViaAPI");
                    connectionInfo.Inheritance.RDGatewayExternalCredentialProvider = attrs.GetAttributeAsBool("InheritRDGatewayExternalCredentialProvider");
                    connectionInfo.Inheritance.RDGatewayUserViaAPI = attrs.GetAttributeAsBool("InheritRDGatewayUserViaAPI");
                }

                switch (_confVersion)
                {
                    case >= 2.8:
                        connectionInfo.RedirectDiskDrives = attrs.GetAttributeAsEnum<RDPDiskDrives>("RedirectDiskDrives");
                        connectionInfo.RedirectDiskDrivesCustom = attrs.GetAttributeAsString("RedirectDiskDrivesCustom");
                        connectionInfo.Inheritance.RedirectDiskDrivesCustom = attrs.GetAttributeAsBool("InheritRedirectDiskDrivesCustom");
                        connectionInfo.EnvironmentTags = attrs.GetAttributeAsString("EnvironmentTags");
                        connectionInfo.Inheritance.EnvironmentTags = attrs.GetAttributeAsBool("InheritEnvironmentTags");
                        break;

                    case >= 0.5:
                    {
                        // used to be boolean
                        bool tmpRedirect = attrs.GetAttributeAsBool("RedirectDiskDrives");
                        connectionInfo.RedirectDiskDrives = tmpRedirect ? RDPDiskDrives.Local : RDPDiskDrives.None;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddMessage(MessageClass.ErrorMsg, string.Format(Language.GetConnectionInfoFromXmlFailed, connectionInfo.Name, ConnectionFileName, ex.Message));
            }

            return connectionInfo;
        }

        private void DeferDecrypt(ConnectionInfo target, string fieldName, string ciphertext)
        {
            _pendingDecrypts.Add((fieldName, ciphertext, target));
        }

        private void CarryOverDecryptedFields(ConnectionInfo source, ConnectionInfo target)
        {
            for (int i = 0; i < _pendingDecrypts.Count; i++)
            {
                if (ReferenceEquals(_pendingDecrypts[i].Target, source))
                    _pendingDecrypts[i] = (_pendingDecrypts[i].Field, _pendingDecrypts[i].Cipher, target);
            }
        }

        private void FlushPendingDecrypts()
        {
            if (_pendingDecrypts.Count == 0) return;
            _swDecrypt.Start();
            try
            {
                _perThreadDecryptor = new ThreadLocal<XmlConnectionsDecryptor>(CreatePerThreadDecryptor);
                ParallelOptions parallelOptions = new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
                Parallel.ForEach(_pendingDecrypts, parallelOptions, item =>
                {
                    try
                    {
                        XmlConnectionsDecryptor decryptor = _perThreadDecryptor!.Value;
                        string plaintext = decryptor.Decrypt(item.Cipher);
                        ApplyDecryptedField(item.Target, item.Field, plaintext);
                    }
                    catch (Exception ex)
                    {
                        Runtime.MessageCollector.AddMessage(MessageClass.ErrorMsg, string.Format(Language.GetConnectionInfoFromXmlFailed, item.Target.Name, ConnectionFileName, ex.Message));
                    }
                });
            }
            finally
            {
                _swDecrypt.Stop();
                _perThreadDecryptor?.Dispose();
                _perThreadDecryptor = null;
                _pendingDecrypts.Clear();
            }
        }

        private void ApplyDecryptedField(ConnectionInfo target, string fieldName, string plaintext)
        {
            switch (fieldName)
            {
                case "Password":
                    target.Password = plaintext;
                    break;
                case "VNCProxyPassword":
                    target.VNCProxyPassword = plaintext;
                    break;
                case "RDGatewayPassword":
                    target.RDGatewayPassword = plaintext;
                    break;
                case "RDGatewayAccessToken":
                    target.RDGatewayAccessToken = plaintext;
                    break;
            }
        }

        private XmlConnectionsDecryptor CreatePerThreadDecryptor()
        {
            if (_confVersion >= 2.6)
                return new XmlConnectionsDecryptor(_cryptoEngine, _cryptoMode, _rootNodeInfo)
                {
                    AuthenticationRequestor = AuthenticationRequestor,
                    KeyDerivationIterations = _keyDerivationIterations
                };
            return new XmlConnectionsDecryptor(_rootNodeInfo)
            {
                AuthenticationRequestor = AuthenticationRequestor
            };
        }

        private void ResetNodeWalkAccumulators()
        {
            _swAttrs.Reset();
            _swGetInfo.Reset();
            _swCtor.Reset();
            _swDecrypt.Reset();
            _swCopyFrom.Reset();
            _swAddChild.Reset();
            _pendingDecrypts.Clear();
            _nodeConnectionCount = 0;
            _nodeContainerCount = 0;
        }

        private void LogNodeWalkBreakdown(Stopwatch total)
        {
            PerfLogger.Log($"Load: XML parse: node walk breakdown nodes: connections={_nodeConnectionCount} containers={_nodeContainerCount} total={total.ElapsedMilliseconds}ms");
            PerfLogger.Log("Load: XML parse: node walk: attr reader build", _swAttrs);
            PerfLogger.Log("Load: XML parse: node walk: ConnectionInfo ctor", _swCtor);
            PerfLogger.Log("Load: XML parse: node walk: decrypt fields", _swDecrypt);
            PerfLogger.Log("Load: XML parse: node walk: CopyFrom (containers)", _swCopyFrom);
            PerfLogger.Log("Load: XML parse: node walk: AddChild", _swAddChild);
            PerfLogger.Log("Load: XML parse: node walk: GetConnectionInfoFromXml (total, incl above)", _swGetInfo);
            long remaining = total.ElapsedMilliseconds -
                             _swAttrs.ElapsedMilliseconds -
                             _swCtor.ElapsedMilliseconds -
                             _swDecrypt.ElapsedMilliseconds -
                             _swCopyFrom.ElapsedMilliseconds -
                             _swAddChild.ElapsedMilliseconds;
            PerfLogger.Log($"Load: XML parse: node walk: remaining (loop+property sets)={remaining}ms");
        }
    }
}