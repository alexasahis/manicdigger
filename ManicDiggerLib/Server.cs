using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using GameModeFortress;
using ManicDigger;
using ManicDigger.MapTools;
using OpenTK;
using ProtoBuf;
using System.Xml.Serialization;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading;
using Jint.Delegates;
using Lidgren.Network;
using System.Diagnostics;

namespace ManicDiggerServer
{
    public class ClientException : Exception
    {
        public ClientException(Exception innerException, int clientid)
            : base("Client exception", innerException)
        {
            this.clientid = clientid;
        }
        public int clientid;
    }
    [ProtoContract]
    public class ManicDiggerSave
    {
        [ProtoMember(1, IsRequired = false)]
        public int MapSizeX;
        [ProtoMember(2, IsRequired = false)]
        public int MapSizeY;
        [ProtoMember(3, IsRequired = false)]
        public int MapSizeZ;
        [ProtoMember(4, IsRequired = false)]
        public Dictionary<string, PacketServerInventory> Inventory;
        [ProtoMember(7, IsRequired = false)]
        public int Seed;
        [ProtoMember(8, IsRequired = false)]
        public long SimulationCurrentFrame;
        [ProtoMember(9, IsRequired = false)]
        public Dictionary<string, PacketServerPlayerStats> PlayerStats;
        [ProtoMember(10, IsRequired = false)]
        public int LastMonsterId;
        [ProtoMember(11, IsRequired = false)]
        public Dictionary<string, byte[]> moddata;
    }
    public partial class Server : ICurrentTime, IDropItem
    {
        public IGameExit exit;
        [Inject]
        public ServerMap d_Map;
        [Inject]
        public GameData d_Data;
        [Inject]
        public CraftingTableTool d_CraftingTableTool;
        [Inject]
        public IGetFileStream d_GetFile;
        [Inject]
        public IChunkDb d_ChunkDb;
        [Inject]
        public ICompression d_NetworkCompression;
        [Inject]
        public INetServer d_MainSocket;
        [Inject]
        public IServerHeartbeat d_Heartbeat;

        public bool LocalConnectionsOnly { get; set; }
        public string[] PublicDataPaths = new string[0];
        public int singleplayerport = 25570;
        public Random rnd = new Random();
        public int SpawnPositionRandomizationRange = 96;
        public bool IsMono = Type.GetType("Mono.Runtime") != null;

        public void Exit()
        {
            exit.exit = true;
            System.Diagnostics.Process.GetCurrentProcess().Kill(); //stops Console.ReadLine() in ServerConsole thread.
        }

        public string serverpathlogs = Path.Combine(GameStorePath.GetStorePath(), "Logs");
        private void BuildLog(string p)
        {
            if (!config.BuildLogging)
            {
                return;
            }
            if (!Directory.Exists(serverpathlogs))
            {
                Directory.CreateDirectory(serverpathlogs);
            }
            string filename = Path.Combine(serverpathlogs, "BuildLog.txt");
            try
            {
                File.AppendAllText(filename, string.Format("{0} {1}\n", DateTime.Now, p));
            }
            catch
            {
                Console.WriteLine("Cannot write to server log file {0}.", filename);
            }
        }
        public void ServerEventLog(string p)
        {
            if (!config.ServerEventLogging)
            {
                return;
            }
            if (!Directory.Exists(serverpathlogs))
            {
                Directory.CreateDirectory(serverpathlogs);
            }
            string filename = Path.Combine(serverpathlogs, "ServerEventLog.txt");
            try
            {
                File.AppendAllText(filename, string.Format("{0} {1}\n", DateTime.Now, p));
            }
            catch
            {
                Console.WriteLine("Cannot write to server log file {0}.", filename);
            }
        }
        public void ChatLog(string p)
        {
            if (!config.ChatLogging)
            {
                return;
            }
            if (!Directory.Exists(serverpathlogs))
            {
                Directory.CreateDirectory(serverpathlogs);
            }
            string filename = Path.Combine(serverpathlogs, "ChatLog.txt");
            try
            {
                File.AppendAllText(filename, string.Format("{0} {1}\n", DateTime.Now, p));
            }
            catch
            {
                Console.WriteLine("Cannot write to server log file {0}.", filename);
            }
        }

        public bool Public;

        public bool enableshadows = true;

        public void Start()
        {
            Server server = this;
            server.LoadConfig();
            var map = new ManicDiggerServer.ServerMap();
            map.server = this;
            map.d_CurrentTime = server;
            map.chunksize = 32;
            for (int i = 0; i < BlockTypes.Length; i++)
            {
                BlockTypes[i] = new BlockType() { };
            }

            map.d_Heightmap = new InfiniteMapChunked2d() { chunksize = Server.chunksize, d_Map = map };
            map.Reset(server.config.MapSizeX, server.config.MapSizeY, server.config.MapSizeZ);
            server.d_Map = map;
            string[] datapaths = new[] { Path.Combine(Path.Combine(Path.Combine("..", ".."), ".."), "data"), "data" };
            string[] datapathspublic = new[] { Path.Combine(datapaths[0], "public"), Path.Combine(datapaths[1], "public") };
            server.PublicDataPaths = datapathspublic;
            var getfile = new GetFileStream(datapaths);
            var data = new GameData();
            data.Start();
            //data.Load(MyStream.ReadAllLines(getfile.GetFile("blocks.csv")),
            //    MyStream.ReadAllLines(getfile.GetFile("defaultmaterialslots.csv")),
            //    MyStream.ReadAllLines(getfile.GetFile("lightlevels.csv")));
            //var craftingrecipes = new CraftingRecipes();
            //craftingrecipes.data = data;
            //craftingrecipes.Load(MyStream.ReadAllLines(getfile.GetFile("craftingrecipes.csv")));
            server.d_Data = data;
            server.d_CraftingTableTool = new CraftingTableTool() { d_Map = map, d_Data = data };
            server.LocalConnectionsOnly = !Public;
            server.d_GetFile = getfile;
            var networkcompression = new CompressionGzip();
            var diskcompression = new CompressionGzip();
            var chunkdb = new ChunkDbCompressed() { d_ChunkDb = new ChunkDbSqlite(), d_Compression = diskcompression };
            server.d_ChunkDb = chunkdb;
            map.d_ChunkDb = chunkdb;
            server.d_NetworkCompression = networkcompression;
            map.d_Data = server.d_Data;
            server.d_DataItems = new GameDataItemsBlocks() { d_Data = data };
            server.SaveFilenameWithoutExtension = SaveFilenameWithoutExtension;
            if (d_MainSocket == null)
            {
                //NetPeerConfiguration serverConfig = new NetPeerConfiguration("ManicDigger");
                //server.d_MainSocket = new MyNetServer() { server = new NetServer(serverConfig) };
                //server.d_MainSocket = new TcpNetServer() { };
                server.d_MainSocket = new EnetNetServer() { };
            }
            server.d_Heartbeat = new ServerHeartbeat();
            if ((Public) && (server.config.Public))
            {
                new Thread((a) => { for (; ; ) { server.SendHeartbeat(); Thread.Sleep(TimeSpan.FromMinutes(1)); } }).Start();
            }

            all_privileges.AddRange(ServerClientMisc.Privilege.All());
            LoadMods(false);

            {
                if (!Directory.Exists(GameStorePath.gamepathsaves))
                {
                    Directory.CreateDirectory(GameStorePath.gamepathsaves);
                }
                Console.WriteLine("Loading savegame...");
                if (!File.Exists(GetSaveFilename()))
                {
                    Console.WriteLine("Creating new savegame file.");
                }
                LoadGame(GetSaveFilename());
                Console.WriteLine("Savegame loaded: " + GetSaveFilename());
            }
            if (LocalConnectionsOnly)
            {
                config.Port = singleplayerport;
            }
            Start(config.Port);

            LoadServerClient();

            // server monitor
            if (config.ServerMonitor)
            {
                this.serverMonitor = new ServerMonitor(this, exit);
                this.serverMonitor.Start();
            }

            // set up server console interpreter
            this.serverConsoleClient = new Client()
            {
                Id = this.serverConsoleId,
                playername = "Server"
            };
            ManicDigger.Group serverGroup = new ManicDigger.Group();
            serverGroup.Name = "Server";
            serverGroup.Level = 255;
            serverGroup.GroupPrivileges = new List<string>();
            serverGroup.GroupPrivileges = all_privileges;
            serverGroup.GroupColor = ServerClientMisc.ClientColor.Red;
            this.serverConsoleClient.AssignGroup(serverGroup);
            this.serverConsole = new ServerConsole(this, exit);
        }

        List<string> all_privileges = new List<string>();

        ModLoader modloader = new ModLoader();
        public List<string> ModPaths = new List<string>();
        ModManager1 modManager;
        private void LoadMods(bool restart)
        {
            modManager = new ModManager1();
            var m = modManager;
            m.Start(this);
            /*
            {
                //debug war mod
                new ManicDigger.Mods.DefaultWar().Start(m);
                new ManicDigger.Mods.Noise2DWorldGeneratorWar().Start(m);
                new ManicDigger.Mods.TreeGeneratorWar().Start(m);
                new ManicDigger.Mods.War().Start(m);
                return;
            }
            */
            var scritps = GetScriptSources();
            modloader.CompileScripts(scritps, restart);
            modloader.Start(m, m.required);
        }

        Dictionary<string, string> GetScriptSources()
        {
            string[] modpaths = new[] { Path.Combine(Path.Combine(Path.Combine(Path.Combine("..", ".."), ".."), "ManicDiggerLib"), "Mods"), "Mods" };

            for (int i = 0; i < modpaths.Length; i++)
            {
                string game = "Fortress";
                if (File.Exists(Path.Combine(modpaths[i], "current.txt")))
                {
                    game = File.ReadAllText(Path.Combine(modpaths[i], "current.txt")).Trim();
                }
                else if (Directory.Exists(modpaths[i]))
                {
                    try
                    {
                        File.WriteAllText(Path.Combine(modpaths[i], "current.txt"), game);
                    }
                    catch
                    {
                    }
                }
                modpaths[i] = Path.Combine(modpaths[i], game);
                d_Heartbeat.GameMode = System.Web.HttpUtility.UrlEncode(game);
            }
            Dictionary<string, string> scripts = new Dictionary<string, string>();
            foreach (string modpath in modpaths)
            {
                if (!Directory.Exists(modpath))
                {
                    continue;
                }
                ModPaths.Add(modpath);
                string[] files = Directory.GetFiles(modpath);
                foreach (string s in files)
                {
                    if (!GameStorePath.IsValidName(Path.GetFileNameWithoutExtension(s)))
                    {
                        continue;
                    }
                    if (!(Path.GetExtension(s).Equals(".cs", StringComparison.InvariantCultureIgnoreCase)
                        || Path.GetExtension(s).Equals(".js", StringComparison.InvariantCultureIgnoreCase)))
                    {
                        continue;
                    }
                    string scripttext = File.ReadAllText(s);
                    string filename = new FileInfo(s).Name;
                    scripts[filename] = scripttext;
                }
            }
            return scripts;
        }

        private ServerMonitor serverMonitor;
        private ServerConsole serverConsole;
        private int serverConsoleId = -1; // make sure that not a regular client is assigned this ID
        public int ServerConsoleId {get { return serverConsoleId; } }
        private Client serverConsoleClient;
        public void ReceiveServerConsole(string message)
        {
            if (message == null)
            {
                return;
            }
            // server command
            if (message.StartsWith("/"))
            {
                string[] ss = message.Split(new[] { ' ' });
                string command = ss[0].Replace("/", "");
                string argument = message.IndexOf(" ") < 0 ? "" : message.Substring(message.IndexOf(" ") + 1);
                this.CommandInterpreter(serverConsoleId, command, argument);
                return;
            }
            // client command
            if (message.StartsWith("."))
            {
                return;
            }
            // chat message
            SendMessageToAll(string.Format("{0}: {1}", serverConsoleClient.ColoredPlayername(colorNormal), message));
            ChatLog(string.Format("{0}: {1}", serverConsoleClient.playername, message));
        }

        public int Seed;
        private void LoadGame(string filename)
        {
            d_ChunkDb.Open(filename);
            byte[] globaldata = d_ChunkDb.GetGlobalData();
            if (globaldata == null)
            {
                //no savegame
                //d_Generator.treeCount = config.Generator.TreeCount;
                if (config.RandomSeed)
                {
                    Seed = new Random().Next();
                }
                else
                {
                    Seed = config.Seed;
                }
                //d_Generator.SetSeed(Seed);
                MemoryStream ms = new MemoryStream();
                SaveGame(ms);
                d_ChunkDb.SetGlobalData(ms.ToArray());
                return;
            }
            ManicDiggerSave save = Serializer.Deserialize<ManicDiggerSave>(new MemoryStream(globaldata));
            //d_Generator.SetSeed(save.Seed);
            Seed = save.Seed;
            d_Map.Reset(d_Map.MapSizeX, d_Map.MapSizeY, d_Map.MapSizeZ);
            if (config.IsCreative) this.Inventory = Inventory = new Dictionary<string, PacketServerInventory>(StringComparer.InvariantCultureIgnoreCase);
            else this.Inventory = save.Inventory;
            this.PlayerStats = save.PlayerStats;
            this.simulationcurrentframe = save.SimulationCurrentFrame;
            this.LastMonsterId = save.LastMonsterId;
            this.moddata = save.moddata;
            if (moddata == null) { moddata = new Dictionary<string, byte[]>(); }
            for (int i = 0; i < onload.Count; i++)
            {
                onload[i]();
            }
        }
        public List<ManicDigger.Action> onload = new List<ManicDigger.Action>();
        public List<ManicDigger.Action> onsave = new List<ManicDigger.Action>();
        public int LastMonsterId;
        public Dictionary<string, PacketServerInventory> Inventory = new Dictionary<string, PacketServerInventory>(StringComparer.InvariantCultureIgnoreCase);
        public Dictionary<string, PacketServerPlayerStats> PlayerStats = new Dictionary<string, PacketServerPlayerStats>(StringComparer.InvariantCultureIgnoreCase);
        public void SaveGame(Stream s)
        {
            for (int i = 0; i < onsave.Count; i++)
            {
                onsave[i]();
            }
            ManicDiggerSave save = new ManicDiggerSave();
            SaveAllLoadedChunks();
            if (!config.IsCreative)
            {
                save.Inventory = Inventory;
            }
            save.PlayerStats = PlayerStats;
            save.Seed = Seed;
            save.SimulationCurrentFrame = simulationcurrentframe;
            save.LastMonsterId = LastMonsterId;
            save.moddata = moddata;
            Serializer.Serialize(s, save);
        }
        public Dictionary<string, byte[]> moddata = new Dictionary<string, byte[]>();
        public bool BackupDatabase(string backupFilename)
        {
            if (!GameStorePath.IsValidName(backupFilename))
            {
                Console.WriteLine("Invalid backup filename: " + backupFilename);
                return false;
            }
            if (!Directory.Exists(GameStorePath.gamepathbackup))
            {
                Directory.CreateDirectory(GameStorePath.gamepathbackup);
            }
            string finalFilename = Path.Combine(GameStorePath.gamepathbackup, backupFilename + MapManipulator.BinSaveExtension);
            d_ChunkDb.Backup(finalFilename);
            return true;
        }
        public bool LoadDatabase(string filename)
        {
            d_Map.d_ChunkDb = d_ChunkDb;
            SaveAll();
            if (filename != GetSaveFilename())
            {
                //todo load
            }
            var dbcompressed = (ChunkDbCompressed)d_Map.d_ChunkDb;
            var db = (ChunkDbSqlite)dbcompressed.d_ChunkDb;
            db.temporaryChunks = new Dictionary<ulong, byte[]>();
            Array.Clear(d_Map.chunks, 0, d_Map.chunks.Length);
            LoadGame(filename);
            foreach (var k in clients)
            {
                //SendLevelInitialize(k.Key);
                Array.Clear(k.Value.chunksseen, 0, k.Value.chunksseen.Length);
                k.Value.chunksseenTime.Clear();
            }
            return true;
        }
        private void SaveAllLoadedChunks()
        {
            List<DbChunk> tosave = new List<DbChunk>();
            for (int cx = 0; cx < d_Map.MapSizeX / chunksize; cx++)
            {
                for (int cy = 0; cy < d_Map.MapSizeY / chunksize; cy++)
                {
                    for (int cz = 0; cz < d_Map.MapSizeZ / chunksize; cz++)
                    {
                        Chunk c = d_Map.chunks[cx, cy, cz];
                        if (c == null)
                        {
                            continue;
                        }
                        if (!c.DirtyForSaving)
                        {
                            continue;
                        }
                        c.DirtyForSaving = false;
                        MemoryStream ms = new MemoryStream();
                        Serializer.Serialize(ms, c);
                        tosave.Add(new DbChunk() { Position = new Xyz() { X = cx, Y = cy, Z = cz }, Chunk = ms.ToArray() });
                        if (tosave.Count > 200)
                        {
                            d_ChunkDb.SetChunks(tosave);
                            tosave.Clear();
                        }
                    }
                }
            }
            d_ChunkDb.SetChunks(tosave);
        }

        public string SaveFilenameWithoutExtension = "default";
        public string SaveFilenameOverride;
        public string GetSaveFilename()
        {
            if (SaveFilenameOverride != null)
            {
                return SaveFilenameOverride;
            }
            return Path.Combine(GameStorePath.gamepathsaves, SaveFilenameWithoutExtension + MapManipulator.BinSaveExtension);
        }
        public void Process11()
        {
            if ((DateTime.Now - lastsave).TotalMinutes > 2)
            {
                DateTime start = DateTime.UtcNow;
                SaveGlobalData();
                Console.WriteLine("Game saved. ({0} seconds)", (DateTime.UtcNow - start));
                lastsave = DateTime.Now;
            }
        }

        private void SaveGlobalData()
        {
            MemoryStream ms = new MemoryStream();
            SaveGame(ms);
            d_ChunkDb.SetGlobalData(ms.ToArray());
        }
        DateTime lastsave = DateTime.Now;

        public void LoadConfig()
        {
            string filename = "ServerConfig.txt";
            if (!File.Exists(Path.Combine(GameStorePath.gamepathconfig, filename)))
            {
                Console.WriteLine("Server configuration file not found, creating new.");
                SaveConfig();
                return;
            }
            try
            {
                using (TextReader textReader = new StreamReader(Path.Combine(GameStorePath.gamepathconfig, filename)))
                {
                    XmlSerializer deserializer = new XmlSerializer(typeof(ServerConfig));
                    config = (ServerConfig)deserializer.Deserialize(textReader);
                    textReader.Close();
                }
            }
            catch //This if for the original format
            {
                using (Stream s = new MemoryStream(File.ReadAllBytes(Path.Combine(GameStorePath.gamepathconfig, filename))))
                {
                    config = new ServerConfig();
                    StreamReader sr = new StreamReader(s);
                    XmlDocument d = new XmlDocument();
                    d.Load(sr);
                    config.Format = int.Parse(XmlTool.XmlVal(d, "/ManicDiggerServerConfig/Format"));
                    config.Name = XmlTool.XmlVal(d, "/ManicDiggerServerConfig/Name");
                    config.Motd = XmlTool.XmlVal(d, "/ManicDiggerServerConfig/Motd");
                    config.Port = int.Parse(XmlTool.XmlVal(d, "/ManicDiggerServerConfig/Port"));
                    string maxclients = XmlTool.XmlVal(d, "/ManicDiggerServerConfig/MaxClients");
                    if (maxclients != null)
                    {
                        config.MaxClients = int.Parse(maxclients);
                    }
                    string key = XmlTool.XmlVal(d, "/ManicDiggerServerConfig/Key");
                    if (key != null)
                    {
                        config.Key = key;
                    }
                    config.IsCreative = Misc.ReadBool(XmlTool.XmlVal(d, "/ManicDiggerServerConfig/Creative"));
                    config.Public = Misc.ReadBool(XmlTool.XmlVal(d, "/ManicDiggerServerConfig/Public"));
                    config.AllowGuests = Misc.ReadBool(XmlTool.XmlVal(d, "/ManicDiggerServerConfig/AllowGuests"));
                    if (XmlTool.XmlVal(d, "/ManicDiggerServerConfig/MapSizeX") != null)
                    {
                        config.MapSizeX = int.Parse(XmlTool.XmlVal(d, "/ManicDiggerServerConfig/MapSizeX"));
                        config.MapSizeY = int.Parse(XmlTool.XmlVal(d, "/ManicDiggerServerConfig/MapSizeY"));
                        config.MapSizeZ = int.Parse(XmlTool.XmlVal(d, "/ManicDiggerServerConfig/MapSizeZ"));
                    }
                    config.BuildLogging = bool.Parse(XmlTool.XmlVal(d, "/ManicDiggerServerConfig/BuildLogging"));
                    config.ServerEventLogging = bool.Parse(XmlTool.XmlVal(d, "/ManicDiggerServerConfig/ServerEventLogging"));
                    config.ChatLogging = bool.Parse(XmlTool.XmlVal(d, "/ManicDiggerServerConfig/ChatLogging"));
                    config.AllowScripting = bool.Parse(XmlTool.XmlVal(d, "/ManicDiggerServerConfig/AllowScripting"));
                    config.ServerMonitor = bool.Parse(XmlTool.XmlVal(d, "/ManicDiggerServerConfig/ServerMonitor"));
                    config.ClientConnectionTimeout = int.Parse(XmlTool.XmlVal(d, "/ManicDiggerServerConfig/ClientConnectionTimeout"));
                    config.ClientPlayingTimeout = int.Parse(XmlTool.XmlVal(d, "/ManicDiggerServerConfig/ClientPlayingTimeout"));
                }
                //Save with new version.
                SaveConfig();
            }
            Console.WriteLine("Server configuration loaded.");
        }

        public ServerConfig config;

        public void SaveConfig()
        {
            //Verify that we have a directory to place the file into.
            if (!Directory.Exists(GameStorePath.gamepathconfig))
            {
                Directory.CreateDirectory(GameStorePath.gamepathconfig);
            }

            XmlSerializer serializer = new XmlSerializer(typeof(ServerConfig));
            TextWriter textWriter = new StreamWriter(Path.Combine(GameStorePath.gamepathconfig, "ServerConfig.txt"));

            //Check to see if config has been initialized
            if (config == null)
            {
                config = new ServerConfig();
            }
            if (config.Areas.Count == 0)
            {
                config.Areas = ServerConfigMisc.getDefaultAreas();
            }
            //Serialize the ServerConfig class to XML
            serializer.Serialize(textWriter, config);
            textWriter.Close();
        }

        public void SendHeartbeat()
        {
            if (config.Key == null)
            {
                return;
            }
            d_Heartbeat.Name = config.Name;
            d_Heartbeat.MaxClients = config.MaxClients;
            d_Heartbeat.PasswordProtected = config.IsPasswordProtected();
            d_Heartbeat.AllowGuests = config.AllowGuests;
            d_Heartbeat.Port = config.Port;
            d_Heartbeat.Version = GameVersion.Version;
            d_Heartbeat.Key = config.Key;
            d_Heartbeat.UsersCount = clients.Count;
            d_Heartbeat.Motd = config.Motd;
            List<string> playernames = new List<string>();
            lock (clients)
            {
                foreach (var k in clients)
                {
                    playernames.Add(k.Value.playername);
                }
            }
            d_Heartbeat.Players = playernames;
            try
            {
                d_Heartbeat.SendHeartbeat();
                if (!writtenServerKey)
                {
                    Console.WriteLine("hash: " + GetHash(d_Heartbeat.ReceivedKey));
                    writtenServerKey = true;
                }
                Console.WriteLine("Heartbeat sent.");
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                Console.WriteLine("Unable to send heartbeat.");
            }
        }

        IPEndPoint iep;
        bool writtenServerKey = false;
        public string hashPrefix = "server=";
        string GetHash(string hash)
        {
            try
            {
                if (hash.Contains(hashPrefix))
                {
                    hash = hash.Substring(hash.IndexOf(hashPrefix) + hashPrefix.Length);
                }
            }
            catch
            {
                return "";
            }
            return hash;
        }
        void Start(int port)
        {
            /*
            if (LocalConnectionsOnly)
            {
                iep = new IPEndPoint(IPAddress.Loopback, port);
            }
            else
            {
                iep = new IPEndPoint(IPAddress.Any, port);
            }
            */
            d_MainSocket.Configuration.Port = port;
            d_MainSocket.Start();
            try
            {
                httpServer = new FragLabs.HTTP.HttpServer(new IPEndPoint(IPAddress.Any, port));
                var m = new MainHttpModule();
                m.server = this;
                httpServer.Install(m);
                foreach (var module in httpModules)
                {
                    httpServer.Install(module.module);
                }
                httpServer.Start();
            }
            catch
            {
                Console.WriteLine("Cannot start HTTP server on TCP port {0}.", port);
            }
        }

        class MainHttpModule : FragLabs.HTTP.IHttpModule
        {
            public Server server;
            public void Installed(FragLabs.HTTP.HttpServer server)
            {
            }

            public void Uninstalled(FragLabs.HTTP.HttpServer server)
            {
            }

            public bool ResponsibleForRequest(FragLabs.HTTP.HttpRequest request)
            {
                if (request.Uri.AbsolutePath.ToLower() == "/")
                {
                    return true;
                }
                return false;
            }

            public bool ProcessAsync(FragLabs.HTTP.ProcessRequestEventArgs args)
            {
                string html = "<html>";
                List<string> modules = new List<string>();
                foreach (var m in server.httpModules)
                {
                    modules.Add(m.name);
                }
                modules.Sort();
                foreach (string s in modules)
                {
                    foreach (var m in server.httpModules)
                    {
                        if (m.name == s)
                        {
                            html += string.Format("<a href='{0}'>{0}</a> - {1}", m.name, m.description());
                        }
                    }
                }
                html += "</html>";
                args.Response.Producer = new FragLabs.HTTP.BufferedProducer(html);
                return false;
            }
        }

        internal FragLabs.HTTP.HttpServer httpServer;

        public void Process()
        {
            try
            {
                Process11();
                Process1();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
        public void Dispose()
        {
            if (!disposed)
            {
                //d_MainSocket.Disconnect(false);
            }
            disposed = true;
        }
        bool disposed = false;
        double starttime = gettime();
        static double gettime()
        {
            return (double)DateTime.Now.Ticks / (10 * 1000 * 1000);
        }
        long simulationcurrentframe;
        public int SimulationCurrentFrame { get { return (int)simulationcurrentframe; } }
        double oldtime;
        double accumulator;

        private int lastClientId;
        public int GenerateClientId()
        {
            int i = 0;
            while (clients.ContainsKey(i))
            {
                i++;
            }
            return i;
        }

        public void Process1()
        {
            if (d_MainSocket == null)
            {
                return;
            }
            {
                double currenttime = gettime() - starttime;
                double deltaTime = currenttime - oldtime;
                accumulator += deltaTime;
                double dt = SIMULATION_STEP_LENGTH;
                while (accumulator > dt)
                {
                    simulationcurrentframe++;
                    /*
                    gameworld.Tick();
                    if (simulationcurrentframe % SIMULATION_KEYFRAME_EVERY == 0)
                    {
                        foreach (var k in clients)
                        {
                            SendTick(k.Key);
                        }
                    }
                    */
                    if (//(GetSeason(simulationcurrentframe) != GetSeason(simulationcurrentframe - 1))
                        //||
                        GetHour(simulationcurrentframe) != GetHour(simulationcurrentframe - 1))
                    {
                        foreach (var c in clients)
                        {
                            NotifySeason(c.Key);
                        }
                    }
                    accumulator -= dt;
                }
                oldtime = currenttime;
            }

            INetIncomingMessage msg;
            Stopwatch s = new Stopwatch();
            s.Start();
            while ((msg = d_MainSocket.ReadMessage()) != null)
            {
                if (msg.SenderConnection == null)
                {
                    continue;
                }
                int clientid = -1;
                foreach (var k in clients)
                {
                    if (k.Value.socket.Equals(msg.SenderConnection))
                    {
                        clientid = k.Key;
                    }
                }
                switch (msg.Type)
                {
                    case ManicDigger.MessageType.Connect:
                        //new connection
                        //ISocket client1 = d_MainSocket.Accept();
                        INetConnection client1 = msg.SenderConnection;
                        IPEndPoint iep1 = (IPEndPoint)client1.RemoteEndPoint;

                        Client c = new Client();
                        c.socket = client1;
                        c.Ping.TimeoutValue = config.ClientConnectionTimeout;
                        c.chunksseen = new bool[d_Map.MapSizeX / chunksize * d_Map.MapSizeY / chunksize * d_Map.MapSizeZ / chunksize];
                        lock (clients)
                        {
                            this.lastClientId = this.GenerateClientId();
                            c.Id = lastClientId;
                            clients[lastClientId] = c;
                        }
                        //clientid = c.Id;
                        c.notifyMapTimer = new ManicDigger.Timer()
                        {
                            INTERVAL = 1.0 / SEND_CHUNKS_PER_SECOND,
                        };
                        c.notifyMonstersTimer = new ManicDigger.Timer()
                        {
                            INTERVAL = 1.0 / SEND_MONSTER_UDAPTES_PER_SECOND,
                        };
                        if (clients.Count > config.MaxClients)
                        {
                            SendDisconnectPlayer(this.lastClientId, "Too many players! Try to connect later.");
                            KillPlayer(this.lastClientId);
                        }
                        else if (config.IsIPBanned(iep1.Address.ToString()))
                        {
                            SendDisconnectPlayer(this.lastClientId, "Your IP has been banned from this server.");
                            ServerEventLog(string.Format("Banned IP {0} tries to connect.", iep1.Address.ToString()));
                            KillPlayer(this.lastClientId);
                        }
                        break;
                    case ManicDigger.MessageType.Data:
                        if (clientid == -1)
                        {
                            break;
                        }

                        // process packet
                        try
                        {
                            TryReadPacket(clientid, msg.ReadBytes(msg.LengthBytes));
                            d_MainSocket.Recycle(msg);
                        }
                        catch (Exception e)
                        {
                            //client problem. disconnect client.
                            Console.WriteLine("Exception at client " + clientid + ". Disconnecting client.");
                            SendDisconnectPlayer(clientid, "Your client threw an exception at server.");
                            KillPlayer(clientid);
                            Console.WriteLine(e.ToString());
                        }
                        if (s.Elapsed.TotalMilliseconds > 15)
                        {
                            break;
                        }
                        break;
                    case ManicDigger.MessageType.Disconnect:
                        KillPlayer(clientid);
                        break;
                }
            }
            foreach (var k in clients)
            {
                k.Value.socket.Update();
            }
            NotifyMap();
            foreach (var k in clients)
            {
                //k.Value.notifyMapTimer.Update(delegate { NotifyMapChunks(k.Key, 1); });
                NotifyInventory(k.Key);
                NotifyPlayerStats(k.Key);
                if (config.Monsters)
                {
                    k.Value.notifyMonstersTimer.Update(delegate { NotifyMonsters(k.Key); });
                }
            }
            pingtimer.Update(
            delegate
            {
                List<int> keysToDelete = new List<int>();
                foreach (var k in clients)
                {
                    // Check if client is alive. Detect half-dropped connections.
                    if (!k.Value.Ping.Send()/*&& k.Value.state == ClientStateOnServer.Playing*/)
                    {
                        if (k.Value.Ping.Timeout())
                        {
                            Console.WriteLine(k.Key + ": ping timeout. Disconnecting...");
                            keysToDelete.Add(k.Key);
                        }
                    }
                    else
                    {
                        SendPing(k.Key);
                    }
                }

                foreach (int key in keysToDelete)
                {
                    KillPlayer(key);
                }
            }
            );
            UnloadUnusedChunks();
            for (int i = 0; i < ChunksSimulated; i++)
            {
                ChunkSimulation();
            }
            foreach (var k in timers)
            {
                k.Key.Update(k.Value);
            }
            if ((DateTime.UtcNow - statsupdate).TotalSeconds >= 2)
            {
                statsupdate = DateTime.UtcNow;
                StatTotalPackets = 0;
                StatTotalPacketsLength = 0;
            }
            if ((DateTime.UtcNow - botpositionupdate).TotalSeconds >= 0.1)
            {
                foreach (var a in clients)
                {
                    if (!a.Value.IsBot)
                    {
                        continue;
                    }
                    foreach (var b in clients)
                    {
                        if (b.Key != a.Key)
                        {
                            if (DistanceSquared(PlayerBlockPosition(clients[b.Key]), PlayerBlockPosition(clients[a.Key])) <= PlayerDrawDistance * PlayerDrawDistance)
                            {
                                SendPlayerTeleport(b.Key, a.Key,
                                    a.Value.PositionMul32GlX,
                                    a.Value.PositionMul32GlY,
                                    a.Value.PositionMul32GlZ,
                                    (byte)a.Value.positionheading,
                                    (byte)a.Value.positionpitch);
                            }
                        }
                    }
                }
                botpositionupdate = DateTime.UtcNow;
            }
        }
        DateTime statsupdate;
        DateTime botpositionupdate = DateTime.UtcNow;

        public Dictionary<ManicDigger.Timer, ManicDigger.Timer.Tick> timers = new Dictionary<ManicDigger.Timer, ManicDigger.Timer.Tick>();

        private void SendPing(int clientid)
        {
            PacketServerPing p = new PacketServerPing()
            {
            };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.Ping, Ping = p }));
        }
        private void NotifyPing(int targetClientId, int ping)
        {
            foreach (var k in clients)
            {
                SendPlayerPing(k.Key, targetClientId, ping);
            }
        }
        private void SendPlayerPing(int recipientClientId, int targetClientId, int ping)
        {
            PacketServerPlayerPing p = new PacketServerPlayerPing()
            {
                ClientId = targetClientId,
                Ping = ping
            };
            SendPacket(recipientClientId, Serialize(new PacketServer() { PacketId = ServerPacketId.PlayerPing, PlayerPing = p }));
        }

        ManicDigger.Timer pingtimer = new ManicDigger.Timer() { INTERVAL = 1, MaxDeltaTime = 5 };
        private void NotifySeason(int clientid)
        {
            if (clients[clientid].state == ClientStateOnServer.Connecting)
            {
                return;
            }
            PacketServerSeason p = new PacketServerSeason()
            {
                //Season = GetSeason(simulationcurrentframe),
                Hour = GetHour(simulationcurrentframe) + 1,
                DayNightCycleSpeedup = (60 * 60 * 24) / DAY_EVERY_SECONDS,
                //Moon = GetMoon(simulationcurrentframe),
                Moon = 0,
            };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.Season, Season = p }));
        }

        int DAY_EVERY_SECONDS = 60 * 60;//1 hour
        public int HourDetail = 4;
        public int GameStartHour = 9; //9 am
        int GetHour(long frame)
        {
            long everyframes = (int)(1 / SIMULATION_STEP_LENGTH) * DAY_EVERY_SECONDS / (24 * HourDetail);
            long startframe = (everyframes * HourDetail * GameStartHour);
            return (int)(((frame + startframe) / everyframes) % (24 * HourDetail));
        }
        /*
        int GetMoon(long frame)
        {
            long everyframes = (int)(1 / SIMULATION_STEP_LENGTH) * DAY_EVERY_SECONDS * 4;
            return (int)((frame / everyframes) % 2);
        }
        */
        int ChunksSimulated = 1;
        int chunksimulation_every { get { return (int)(1 / SIMULATION_STEP_LENGTH) * 60 * 10; } }//10 minutes

        void ChunkSimulation()
        {
            foreach (var k in clients)
            {
                var pos = PlayerBlockPosition(k.Value);

                long oldesttime = long.MaxValue;
                Vector3i oldestpos = new Vector3i();

                foreach (var p in ChunksAroundPlayer(pos))
                {
                    if (!MapUtil.IsValidPos(d_Map, p.x, p.y, p.z)) { continue; }
                    Chunk c = d_Map.chunks[p.x / chunksize, p.y / chunksize, p.z / chunksize];
                    if (c == null) { continue; }
                    if (c.data == null) { continue; }
                    if (c.LastUpdate > simulationcurrentframe) { c.LastUpdate = simulationcurrentframe; }
                    if (c.LastUpdate < oldesttime)
                    {
                        oldesttime = c.LastUpdate;
                        oldestpos = p;
                    }
                    if (!c.IsPopulated)
                    {
                        PopulateChunk(p);
                        c.IsPopulated = true;
                    }
                }
                if (simulationcurrentframe - oldesttime > chunksimulation_every)
                {
                    ChunkUpdate(oldestpos, oldesttime);
                    Chunk c = d_Map.chunks[oldestpos.x / chunksize, oldestpos.y / chunksize, oldestpos.z / chunksize];
                    c.LastUpdate = (int)simulationcurrentframe;
                    return;
                }
            }
        }

        private void PopulateChunk(Vector3i p)
        {
            for (int i = 0; i < modEventHandlers.populatechunk.Count; i++)
            {
                modEventHandlers.populatechunk[i](p.x / chunksize, p.y / chunksize, p.z / chunksize);
            }
            //d_Generator.PopulateChunk(d_Map, p.x / chunksize, p.y / chunksize, p.z / chunksize);
        }

        private void ChunkUpdate(Vector3i p, long lastupdate)
        {          
            if (config.Monsters)
            {
                AddMonsters(p);
            }
            ushort[] chunk = d_Map.GetChunk(p.x, p.y, p.z);
            for (int xx = 0; xx < chunksize; xx++)
            {
                for (int yy = 0; yy < chunksize; yy++)
                {
                    for (int zz = 0; zz < chunksize; zz++)
                    {
                        int block = chunk[MapUtil.Index3d(xx, yy, zz, chunksize, chunksize)];

                        for (int i = 0; i < modEventHandlers.blockticks.Count; i++)
                        {
                            modEventHandlers.blockticks[i](p.x + xx, p.y + yy, p.z + zz);
                        }
                    }
                }
            }
        }

        public int[] MonsterTypesUnderground = new int[] { 1, 2 };
        public int[] MonsterTypesOnGround = new int[] { 0, 3, 4 };

        private void AddMonsters(Vector3i p)
        {
            Chunk chunk = d_Map.chunks[p.x / chunksize, p.y / chunksize, p.z / chunksize];
            int tries = 0;
            while (chunk.Monsters.Count < 1)
            {
                int xx = rnd.Next(chunksize);
                int yy = rnd.Next(chunksize);
                int zz = rnd.Next(chunksize);
                int px = p.x + xx;
                int py = p.y + yy;
                int pz = p.z + zz;
                if ((!MapUtil.IsValidPos(d_Map, px, py, pz))
                    || (!MapUtil.IsValidPos(d_Map, px, py, pz + 1))
                    || (!MapUtil.IsValidPos(d_Map, px, py, pz - 1)))
                {
                    continue;
                }
                int type;
                int height = MapUtil.blockheight(d_Map, 0, px, py);
                if (pz >= height)
                {
                    type = MonsterTypesOnGround[rnd.Next(MonsterTypesOnGround.Length)];
                }
                else
                {
                    type = MonsterTypesUnderground[rnd.Next(MonsterTypesUnderground.Length)];
                }
                if (d_Map.GetBlock(px, py, pz) == 0
                    && d_Map.GetBlock(px, py, pz + 1) == 0
                    && d_Map.GetBlock(px, py, pz - 1) != 0
                    && (!BlockTypes[d_Map.GetBlock(px, py, pz - 1)].IsFluid()))
                {
                    chunk.Monsters.Add(new Monster() { X = px, Y = py, Z = pz, Id = NewMonsterId(), Health = 20, MonsterType = type });
                }
                if (tries++ > 500)
                {
                    break;
                }
            }
        }
        public int NewMonsterId()
        {
            return LastMonsterId++;
        }

        int CompressUnusedIteration = 0;
        private void UnloadUnusedChunks()
        {
            int sizex = d_Map.chunks.GetUpperBound(0) + 1;
            int sizey = d_Map.chunks.GetUpperBound(1) + 1;
            int sizez = d_Map.chunks.GetUpperBound(2) + 1;

            for (int i = 0; i < 100; i++)
            {
                var v = MapUtil.Pos(CompressUnusedIteration, d_Map.MapSizeX / chunksize, d_Map.MapSizeY / chunksize);
                Chunk c = d_Map.chunks[v.x, v.y, v.z];
                var vg = new Vector3i(v.x * chunksize, v.y * chunksize, v.z * chunksize);
                bool stop = false;
                if (c != null)
                {
                    bool unload = true;
                    foreach (var k in clients)
                    {
                        int viewdist = (int)(chunkdrawdistance * chunksize * 1.5f);
                        if (DistanceSquared(PlayerBlockPosition(k.Value), vg) <= viewdist * viewdist)
                        {
                            unload = false;
                        }
                    }
                    if (unload)
                    {
                        if (c.DirtyForSaving)
                        {
                            DoSaveChunk(v.x, v.y, v.z, c);
                        }
                        d_Map.chunks[v.x, v.y, v.z] = null;
                        stop = true;
                    }
                }
                CompressUnusedIteration++;
                if (CompressUnusedIteration >= sizex * sizey * sizez)
                {
                    CompressUnusedIteration = 0;
                }
                if (stop)
                {
                    return;
                }
            }
        }

        //on exit
        public void SaveAll()
        {
            for (int x = 0; x < d_Map.MapSizeX / chunksize; x++)
            {
                for (int y = 0; y < d_Map.MapSizeY / chunksize; y++)
                {
                    for (int z = 0; z < d_Map.MapSizeZ / chunksize; z++)
                    {
                        if (d_Map.chunks[x, y, z] != null)
                        {
                            DoSaveChunk(x, y, z, d_Map.chunks[x, y, z]);
                        }
                    }
                }
            }
            SaveGlobalData();
        }

        private void DoSaveChunk(int x, int y, int z, Chunk c)
        {
            MemoryStream ms = new MemoryStream();
            Serializer.Serialize(ms, c);
            ChunkDb.SetChunk(d_ChunkDb, x, y, z, ms.ToArray());
        }
        int SEND_CHUNKS_PER_SECOND = 10;
        int SEND_MONSTER_UDAPTES_PER_SECOND = 3;
        /*
        private List<Vector3i> UnknownChunksAroundPlayer(int clientid)
        {
            Client c = clients[clientid];
            List<Vector3i> tosend = new List<Vector3i>();
            Vector3i playerpos = PlayerBlockPosition(c);
            foreach (var v in ChunksAroundPlayer(playerpos))
            {
                Chunk chunk = d_Map.chunks[v.x / chunksize, v.y / chunksize, v.z / chunksize];
                if (chunk == null)
                {
                    LoadChunk(v);
                    chunk = d_Map.chunks[v.x / chunksize, v.y / chunksize, v.z / chunksize];
                }
                int chunkupdatetime = chunk.LastChange;
                if (!c.chunksseen.ContainsKey(v) || c.chunksseen[v] < chunkupdatetime)
                {
                    if (MapUtil.IsValidPos(d_Map, v.x, v.y, v.z))
                    {
                        tosend.Add(v);
                    }
                }
            }
            return tosend;
        }
        */
        private void LoadChunk(int cx, int cy, int cz)
        {
            d_Map.LoadChunk(cx, cy, cz);
        }

        /*
        private int NotifyMapChunks(int clientid, int limit)
        {
            Client c = clients[clientid];
            Vector3i playerpos = PlayerBlockPosition(c);
            if (playerpos == new Vector3i())
            {
                return 0;
            }
            List<Vector3i> tosend = UnknownChunksAroundPlayer(clientid);
            tosend.Sort((a, b) => DistanceSquared(a, playerpos).CompareTo(DistanceSquared(b, playerpos)));
            int sent = 0;
            foreach (var v in tosend)
            {
                if (sent >= limit)
                {
                    break;
                }
                byte[] chunk = d_Map.GetChunk(v.x, v.y, v.z);
                c.chunksseen[v] = (int)simulationcurrentframe;
                sent++;
                if (MapUtil.IsSolidChunk(chunk) && chunk[0] == 0)
                {
                    //don't send empty chunk.
                    continue;
                }
                byte[] compressedchunk = CompressChunkNetwork(chunk);
                if (!c.heightmapchunksseen.ContainsKey(new Vector2i(v.x, v.y)))
                {
                    byte[] heightmapchunk = d_Map.GetHeightmapChunk(v.x, v.y);
                    byte[] compressedHeightmapChunk = d_NetworkCompression.Compress(heightmapchunk);
                    PacketServerHeightmapChunk p1 = new PacketServerHeightmapChunk()
                    {
                        X = v.x,
                        Y = v.y,
                        SizeX = chunksize,
                        SizeY = chunksize,
                        CompressedHeightmap = compressedHeightmapChunk,
                    };
                    SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.HeightmapChunk, HeightmapChunk = p1 }));
                    c.heightmapchunksseen.Add(new Vector2i(v.x, v.y), (int)simulationcurrentframe);
                }
                PacketServerChunk p = new PacketServerChunk()
                {
                    X = v.x,
                    Y = v.y,
                    Z = v.z,
                    SizeX = chunksize,
                    SizeY = chunksize,
                    SizeZ = chunksize,
                    CompressedChunk = compressedchunk,
                };
                SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.Chunk, Chunk = p }));
            }
        }
         
        /*
Vector3i playerpos = PlayerBlockPosition(clients[clientid]);
var around = new List<Vector3i>(ChunksAroundPlayer(playerpos));
for (int i = 0; i < around.Count; i++)
{
var v = around[i];
d_Map.GetBlock(v.x, v.y, v.z); //force load
if (i % 10 == 0)
{
    SendLevelProgress(clientid, (int)(((float)i / around.Count) * 100), "Generating world...");
}
}
ChunkSimulation();

List<Vector3i> unknown = UnknownChunksAroundPlayer(clientid);
int sent = 0;
for (int i = 0; i < unknown.Count; i++)
{
sent += NotifyMapChunks(clientid, 5);
SendLevelProgress(clientid, (int)(((float)sent / unknown.Count) * 100), "Downloading map...");
if (sent >= unknown.Count) { break; }
}
*/
        /*
            SendLevelFinalize(clientid);
            return sent;
        }
        */
        const string invalidplayername = "invalid";
        public void NotifyInventory(int clientid)
        {
            Client c = clients[clientid];
            if (c.IsInventoryDirty && c.playername != invalidplayername)
            {
                PacketServerInventory p;
                /*
                if (config.IsCreative)
                {
                    p = new PacketServerInventory()
                    {
                        BlockTypeAmount = StartInventory(),
                        IsFinite = false,
                    };
                }
                else
                */
                {
                    p = GetPlayerInventory(c.playername);
                }
                SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.FiniteInventory, Inventory = p }));
                c.IsInventoryDirty = false;
            }
        }
        public void NotifyPlayerStats(int clientid)
        {
            Client c = clients[clientid];
            if (c.IsPlayerStatsDirty && c.playername != invalidplayername)
            {
                PacketServerPlayerStats p = GetPlayerStats(c.playername);
                SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.PlayerStats, PlayerStats = p }));
                c.IsPlayerStatsDirty = false;
            }
        }
        private void HitMonsters(int clientid, int health)
        {
            Client c = clients[clientid];
            int mapx = c.PositionMul32GlX / 32;
            int mapy = c.PositionMul32GlZ / 32;
            int mapz = c.PositionMul32GlY / 32;
            //3x3x3 chunks
            List<PacketServerMonster> p = new List<PacketServerMonster>();
            for (int xx = -1; xx < 2; xx++)
            {
                for (int yy = -1; yy < 2; yy++)
                {
                    for (int zz = -1; zz < 2; zz++)
                    {
                        int cx = (mapx / chunksize) + xx;
                        int cy = (mapy / chunksize) + yy;
                        int cz = (mapz / chunksize) + zz;
                        if (!MapUtil.IsValidChunkPos(d_Map, cx, cy, cz, chunksize))
                        {
                            continue;
                        }
                        Chunk chunk = d_Map.chunks[cx, cy, cz];
                        if (chunk == null || chunk.Monsters == null)
                        {
                            continue;
                        }
                        foreach (Monster m in chunk.Monsters)
                        {
                            Vector3i mpos = new Vector3i { x = m.X, y = m.Y, z = m.Z };
                            Vector3i ppos = new Vector3i
                            {
                                x = clients[clientid].PositionMul32GlX / 32,
                                y = clients[clientid].PositionMul32GlZ / 32,
                                z = clients[clientid].PositionMul32GlY / 32
                            };
                            if (DistanceSquared(mpos, ppos) < 15)
                            {
                                m.Health -= health;
                                //Console.WriteLine("HIT! -2 = " + m.Health);
                                if (m.Health <= 0)
                                {
                                    chunk.Monsters.Remove(m);
                                    SendSound(clientid, "death.wav", m.X, m.Y, m.Z);
                                    break;
                                }
                                SendSound(clientid, "grunt2.wav", m.X, m.Y, m.Z);
                                break;
                            }
                        }
                    }
                }
            }
        }
        private void NotifyMonsters(int clientid)
        {
            Client c = clients[clientid];
            int mapx = c.PositionMul32GlX / 32;
            int mapy = c.PositionMul32GlZ / 32;
            int mapz = c.PositionMul32GlY / 32;
            //3x3x3 chunks
            List<PacketServerMonster> p = new List<PacketServerMonster>();
            for (int xx = -1; xx < 2; xx++)
            {
                for (int yy = -1; yy < 2; yy++)
                {
                    for (int zz = -1; zz < 2; zz++)
                    {
                        int cx = (mapx / chunksize) + xx;
                        int cy = (mapy / chunksize) + yy;
                        int cz = (mapz / chunksize) + zz;
                        if (!MapUtil.IsValidChunkPos(d_Map, cx, cy, cz, chunksize))
                        {
                            continue;
                        }
                        Chunk chunk = d_Map.chunks[cx, cy, cz];
                        if (chunk == null || chunk.Monsters == null)
                        {
                            continue;
                        }
                        foreach (Monster m in new List<Monster>(chunk.Monsters))
                        {
                            MonsterWalk(m);
                        }
                        foreach (Monster m in chunk.Monsters)
                        {
                            float progress = m.WalkProgress;
                            if (progress < 0) //delay
                            {
                                progress = 0;
                            }
                            byte heading = 0;
                            if (m.WalkDirection.x == -1 && m.WalkDirection.y == 0) { heading = (byte)(((int)byte.MaxValue * 3) / 4); }
                            if (m.WalkDirection.x == 1 && m.WalkDirection.y == 0) { heading = byte.MaxValue / 4; }
                            if (m.WalkDirection.x == 0 && m.WalkDirection.y == -1) { heading = 0; }
                            if (m.WalkDirection.x == 0 && m.WalkDirection.y == 1) { heading = byte.MaxValue / 2; }
                            var mm = new PacketServerMonster()
                            {
                                Id = m.Id,
                                MonsterType = m.MonsterType,
                                Health = m.Health,
                                PositionAndOrientation = new PositionAndOrientation()
                                {
                                    Heading = heading,
                                    Pitch = 0,
                                    X = (int)((m.X + progress * m.WalkDirection.x) * 32 + 16),
                                    Y = (int)((m.Z + progress * m.WalkDirection.z) * 32),
                                    Z = (int)((m.Y + progress * m.WalkDirection.y) * 32 + 16),
                                }
                            };
                            p.Add(mm);
                        }
                    }
                }
            }
            //send only nearest monsters
            p.Sort((a, b) =>
            {
                Vector3i posA = new Vector3i(a.PositionAndOrientation.X, a.PositionAndOrientation.Y, a.PositionAndOrientation.Z);
                Vector3i posB = new Vector3i(b.PositionAndOrientation.X, b.PositionAndOrientation.Y, b.PositionAndOrientation.Z);
                Client client = clients[clientid];
                Vector3i posPlayer = new Vector3i(client.PositionMul32GlX, client.PositionMul32GlY, client.PositionMul32GlZ);
                return DistanceSquared(posA, posPlayer).CompareTo(DistanceSquared(posB, posPlayer));
            }
            );
            if (p.Count > sendmaxmonsters)
            {
                p.RemoveRange(sendmaxmonsters, p.Count - sendmaxmonsters);
            }
            SendPacket(clientid, Serialize(new PacketServer()
            {
                PacketId = ServerPacketId.Monster,
                Monster = new PacketServerMonsters() { Monsters = p.ToArray() }
            }));
        }
        int sendmaxmonsters = 10;
        void MonsterWalk(Monster m)
        {
            m.WalkProgress += 0.3f;
            if (m.WalkProgress < 1)
            {
                return;
            }
            int oldcx = m.X / chunksize;
            int oldcy = m.Y / chunksize;
            int oldcz = m.Z / chunksize;
            d_Map.chunks[oldcx, oldcy, oldcz].Monsters.Remove(m);
            m.X += m.WalkDirection.x;
            m.Y += m.WalkDirection.y;
            m.Z += m.WalkDirection.z;
            int newcx = m.X / chunksize;
            int newcy = m.Y / chunksize;
            int newcz = m.Z / chunksize;
            if (d_Map.chunks[newcx, newcy, newcz].Monsters == null)
            {
                d_Map.chunks[newcx, newcy, newcz].Monsters = new List<Monster>();
            }
            d_Map.chunks[newcx, newcy, newcz].Monsters.Add(m);
            /*
            if (rnd.Next(3) == 0)
            {
                m.WalkDirection = new Vector3i();
                m.WalkProgress = -2;
                return;
            }
            */
            List<Vector3i> l = new List<Vector3i>();
            for (int zz = -1; zz < 2; zz++)
            {
                if (d_Map.GetBlock(m.X + 1, m.Y, m.Z + zz) == 0
                     && d_Map.GetBlock(m.X + 1, m.Y, m.Z + zz - 1) != 0)
                {
                    l.Add(new Vector3i(1, 0, zz));
                }
                if (d_Map.GetBlock(m.X - 1, m.Y, m.Z + zz) == 0
                    && d_Map.GetBlock(m.X - 1, m.Y, m.Z + zz - 1) != 0)
                {
                    l.Add(new Vector3i(-1, 0, zz));
                }
                if (d_Map.GetBlock(m.X, m.Y + 1, m.Z + zz) == 0
                    && d_Map.GetBlock(m.X, m.Y + 1, m.Z + zz - 1) != 0)
                {
                    l.Add(new Vector3i(0, 1, zz));
                }
                if (d_Map.GetBlock(m.X, m.Y - 1, m.Z + zz) == 0
                    && d_Map.GetBlock(m.X, m.Y - 1, m.Z + zz - 1) != 0)
                {
                    l.Add(new Vector3i(0, -1, zz));
                }
            }
            Vector3i dir;
            if (l.Count > 0)
            {
                dir = l[rnd.Next(l.Count)];
            }
            else
            {
                dir = new Vector3i();
            }
            m.WalkDirection = dir;
            m.WalkProgress = 0;
        }
        public PacketServerInventory GetPlayerInventory(string playername)
        {
            if (Inventory == null)
            {
                Inventory = new Dictionary<string, PacketServerInventory>(StringComparer.InvariantCultureIgnoreCase);
            }
            if (!Inventory.ContainsKey(playername))
            {
                Inventory[playername] = new PacketServerInventory()
                {
                    Inventory = StartInventory(),
                    /*
                    IsFinite = true,
                    Max = FiniteInventoryMax,
                    */
                };
            }
            return Inventory[playername];
        }
        public void ResetPlayerInventory(string playername)
        {
            if (Inventory == null)
            {
                Inventory = new Dictionary<string, PacketServerInventory>(StringComparer.InvariantCultureIgnoreCase);
            }
            this.Inventory[playername] = new PacketServerInventory()
            {
                Inventory = StartInventory(),
            };
        }

        public PacketServerPlayerStats GetPlayerStats(string playername)
        {
            if (PlayerStats == null)
            {
                PlayerStats = new Dictionary<string, PacketServerPlayerStats>(StringComparer.InvariantCultureIgnoreCase);
            }
            if (!PlayerStats.ContainsKey(playername))
            {
                PlayerStats[playername] = StartPlayerStats();
            }
            return PlayerStats[playername];
        }
        public int FiniteInventoryMax = 200;
        /*
        Dictionary<int, int> StartInventory()
        {
            Dictionary<int, int> d = new Dictionary<int, int>();
            d[(int)TileTypeManicDigger.CraftingTable] = 6;
            d[(int)TileTypeManicDigger.Crops1] = 1;
            return d;
        }
        */
        Inventory StartInventory()
        {
            Inventory inv = ManicDigger.Inventory.Create();
            int x = 0;
            int y = 0;
            for (int i = 0; i < d_Data.StartInventoryAmount.Length; i++)
            {
                int amount = d_Data.StartInventoryAmount[i];
                if (config.IsCreative)
                {
                    if (amount > 0 || BlockTypes[i].IsBuildable)
                    {
                        inv.Items.Add(new ProtoPoint(x, y), new Item() { ItemClass = ItemClass.Block, BlockId = i, BlockCount = 0 });
                        x++;
                        if (x >= GetInventoryUtil(inv).CellCount.X)
                        {
                            x = 0;
                            y++;
                        }
                    }
                }
                else if (amount > 0)
                {
                    inv.Items.Add(new ProtoPoint(x, y), new Item() { ItemClass = ItemClass.Block, BlockId = i, BlockCount = amount });
                    x++;
                    if (x >= GetInventoryUtil(inv).CellCount.X)
                    {
                        x = 0;
                        y++;
                    }
                }
            }
            return inv;
        }
        PacketServerPlayerStats StartPlayerStats()
        {
            var p = new PacketServerPlayerStats();
            p.CurrentHealth = 20;
            p.MaxHealth = 20;
            return p;
        }
        public Vector3i PlayerBlockPosition(Client c)
        {
            return new Vector3i(c.PositionMul32GlX / 32, c.PositionMul32GlZ / 32, c.PositionMul32GlY / 32);
        }
        public int DistanceSquared(Vector3i a, Vector3i b)
        {
            int dx = a.x - b.x;
            int dy = a.y - b.y;
            int dz = a.z - b.z;
            return dx * dx + dy * dy + dz * dz;
        }
        public void KillPlayer(int clientid)
        {
            if (!clients.ContainsKey(clientid))
            {
                return;
            }
            for (int i = 0; i < modEventHandlers.onplayerleave.Count; i++)
            {
                modEventHandlers.onplayerleave[i](clientid);
            }
            for (int i = 0; i < modEventHandlers.onplayerdisconnect.Count; i++)
            {
                modEventHandlers.onplayerdisconnect[i](clientid);
            }
            string coloredName = clients[clientid].ColoredPlayername(colorNormal);
            string name = clients[clientid].playername;
            clients.Remove(clientid);
            if (config.ServerMonitor)
            {
                this.serverMonitor.RemoveMonitorClient(clientid);
            }
            foreach (var kk in clients)
            {
                SendDespawnPlayer(kk.Key, clientid);
            }
            if (name != "invalid")
            {
                SendMessageToAll(string.Format("Player {0} disconnected.", coloredName));
                ServerEventLog(string.Format("{0} disconnects.", name));
            }
        }

        private void TryReadPacket(int clientid, byte[] data)
        {
            Client c = clients[clientid];
            PacketClient packet = Serializer.Deserialize<PacketClient>(new MemoryStream(data));
            if (config.ServerMonitor && !this.serverMonitor.CheckPacket(clientid, packet))
            {
                return;
            }
            switch (packet.PacketId)
            {
                case ClientPacketId.PingReply:
                    clients[clientid].Ping.Receive();
                    clients[clientid].LastPing = (float)clients[clientid].Ping.RoundtripTime.TotalSeconds;
                    this.NotifyPing(clientid, (int)clients[clientid].Ping.RoundtripTime.TotalMilliseconds);
                    break;
                case ClientPacketId.PlayerIdentification:
                    {
                        if (config.IsPasswordProtected() && packet.Identification.ServerPassword != config.Password)
                        {
                            Console.WriteLine(string.Format("{0} fails to join (invalid server password).", packet.Identification.Username));
                            ServerEventLog(string.Format("{0} fails to join (invalid server password).", packet.Identification.Username));
                            SendDisconnectPlayer(clientid, "Invalid server password.");
                            KillPlayer(clientid);
                            break;
                        }
                        SendServerIdentification(clientid);
                        string username = packet.Identification.Username;

                        // allowed characters in username: a-z,A-Z,0-9,-,_ length: 1-16
                        Regex allowedUsername = new Regex(@"^(\w|-){1,16}$");

                        if (string.IsNullOrEmpty(username) || !allowedUsername.IsMatch(username))
                        {
                            SendDisconnectPlayer(clientid, "Invalid username (allowed characters: a-z,A-Z,0-9,-,_; max. length: 16).");
                            ServerEventLog(string.Format("{0} can't join (invalid username: {1}).", ((IPEndPoint)c.socket.RemoteEndPoint).Address.ToString(), username));
                            KillPlayer(clientid);
                            break;
                        }

                        bool isClientLocalhost = (((IPEndPoint)c.socket.RemoteEndPoint).Address.ToString() == "127.0.0.1");
                        bool verificationFailed = false;

                        if ((ComputeMd5(config.Key.Replace("-", "") + username) != packet.Identification.VerificationKey)
                            && (!isClientLocalhost))
                        {
                            //Account verification failed.
                            username = "~" + username;
                            verificationFailed = true;
                        }

                        if (!config.AllowGuests && verificationFailed)
                        {
                            SendDisconnectPlayer(clientid, "Guests are not allowed on this server. Login or register an account.");
                            KillPlayer(clientid);
                            break;
                        }

                        if (config.IsUserBanned(username))
                        {
                            SendDisconnectPlayer(clientid, "Your username has been banned from this server.");
                            ServerEventLog(string.Format("{0} fails to join (banned username: {1}).", ((IPEndPoint)c.socket.RemoteEndPoint).Address.ToString(), username));
                            KillPlayer(clientid);
                            break;
                        }

                        //When a duplicate user connects, append a number to name.
                        foreach (var k in clients)
                        {
                            if (k.Value.playername.Equals(username, StringComparison.InvariantCultureIgnoreCase))
                            {
                                // If duplicate is a registered user, kick duplicate. It is likely that the user lost connection before.
                                if (!verificationFailed && !isClientLocalhost)
                                {
                                    KillPlayer(k.Key);
                                    break;
                                }

                                // Duplicates are handled as guests.
                                username = GenerateUsername(username);
                                if (!username.StartsWith("~")) { username = "~" + username; }
                                break;
                            }
                        }
                        clients[clientid].playername = username;

                        // Assign group to new client
                        //Check if client is in ServerClient.xml and assign corresponding group.
                        bool exists = false;
                        foreach (ManicDigger.Client client in serverClient.Clients)
                        {
                            if (client.Name.Equals(username, StringComparison.InvariantCultureIgnoreCase))
                            {
                                foreach (ManicDigger.Group clientGroup in serverClient.Groups)
                                {
                                    if (clientGroup.Name.Equals(client.Group))
                                    {
                                        exists = true;
                                        clients[clientid].AssignGroup(clientGroup);
                                        break;
                                    }
                                }
                                break;
                            }
                        }
                        if (!exists)
                        {
                            if (clients[clientid].playername.StartsWith("~"))
                            {
                                clients[clientid].AssignGroup(this.defaultGroupGuest);
                            }
                            else
                            {
                                clients[clientid].AssignGroup(this.defaultGroupRegistered);
                            }
                        }
                        if (isClientLocalhost)
                        {
                            clients[clientid].AssignGroup(serverClient.Groups.Find(v => v.Name == "Admin"));
                        }
                        this.SetFillAreaLimit(clientid);
                        this.SendFreemoveState(clientid, clients[clientid].privileges.Contains(ServerClientMisc.Privilege.freemove));
                        for (int i = 0; i < modEventHandlers.onplayerjoin.Count; i++)
                        {
                            modEventHandlers.onplayerjoin[i](clientid);
                        }
                    }
                    break;
                case ClientPacketId.RequestBlob:
                    {
                        // Set player's spawn position
                        Vector3i position = GetPlayerSpawnPositionMul32(clientid);

                        clients[clientid].PositionMul32GlX = position.x;
                        clients[clientid].PositionMul32GlY = position.y + (int)(0.5 * 32);
                        clients[clientid].PositionMul32GlZ = position.z;

                        string ip = ((IPEndPoint)clients[clientid].socket.RemoteEndPoint).Address.ToString();
                        SendMessageToAll(string.Format("Player {0} joins.", clients[clientid].ColoredPlayername(colorNormal)));
                        ServerEventLog(string.Format("{0} {1} joins.", clients[clientid].playername, ip));
                        SendMessage(clientid, colorSuccess + config.WelcomeMessage);
                        SendBlobs(clientid);
                        SendBlockTypes(clientid);
                        SendSunLevels(clientid);
                        SendLightLevels(clientid);
                        SendCraftingRecipes(clientid);

                        //notify all players about new player spawn
                        SendPlayerSpawnToAll(clientid);

                        //send all players spawn to new player
                        foreach (var k in clients)
                        {
                            if (k.Key != clientid)
                            {
                                SendPlayerSpawn(clientid, k.Key);
                            }
                        }
                        clients[clientid].state = ClientStateOnServer.LoadingGenerating;
                        NotifySeason(clientid);
                    }
                    break;
                case ClientPacketId.SetBlock:
                    {
                        int x = packet.SetBlock.X;
                        int y = packet.SetBlock.Y;
                        int z = packet.SetBlock.Z;
                        if (!PlayerHasPrivilege(clientid,ServerClientMisc.Privilege.build))
                        {
                            SendMessage(clientid, colorError + "Insufficient privileges to build.");
                            SendSetBlock(clientid, x, y, z, d_Map.GetBlock(x, y, z)); //revert
                            break;
                        }
                        if (clients[clientid].IsSpectator)
                        {
                            SendMessage(clientid, colorError + "Spectators are not allowed to build.");
                            SendSetBlock(clientid, x, y, z, d_Map.GetBlock(x, y, z)); //revert
                            break;
                        }
                        if (!config.CanUserBuild(clients[clientid], x, y, z) && (packet.SetBlock.Mode == BlockSetMode.Create || packet.SetBlock.Mode == BlockSetMode.Destroy)
                            && !extraPrivileges.ContainsKey(ServerClientMisc.Privilege.build))
                        {
                            SendMessage(clientid, colorError + "You need permission to build in this section of the world.");
                            SendSetBlock(clientid, x, y, z, d_Map.GetBlock(x, y, z)); //revert
                            break;
                        }
                        if (!DoCommandBuild(clientid, true, packet.SetBlock))
                        {
                            SendSetBlock(clientid, x, y, z, d_Map.GetBlock(x, y, z)); //revert
                        }
                        BuildLog(string.Format("{0} {1} {2} {3} {4} {5}", x, y, z, c.playername, ((IPEndPoint)c.socket.RemoteEndPoint).Address.ToString(), d_Map.GetBlock(x, y, z)));
                    }
                    break;
                case ClientPacketId.FillArea:
                    {
                        if (!clients[clientid].privileges.Contains(ServerClientMisc.Privilege.build))
                        {
                            SendMessage(clientid, colorError + "Insufficient privileges to build.");
                            break;
                        }
                        if (clients[clientid].IsSpectator)
                        {
                            SendMessage(clientid, colorError + "Spectators are not allowed to build.");
                            break;
                        }
                        Vector3i a = new Vector3i(packet.FillArea.X1, packet.FillArea.Y1, packet.FillArea.Z1);
                        Vector3i b = new Vector3i(packet.FillArea.X2, packet.FillArea.Y2, packet.FillArea.Z2);

                        int blockCount = (Math.Abs(a.x - b.x) + 1) * (Math.Abs(a.y - b.y) + 1) * (Math.Abs(a.z - b.z) + 1);

                        if (blockCount > clients[clientid].FillLimit)
                        {
                            SendMessage(clientid, colorError + "Fill area is too large.");
                            break;
                        }
                        if (!this.IsFillAreaValid(clients[clientid], a, b))
                        {
                            SendMessage(clientid, colorError + "Fillarea is invalid or contains blocks in an area you are not allowed to build in.");
                            break;
                        }
                        this.DoFillArea(clientid, packet.FillArea, blockCount);

                        BuildLog(string.Format("{0} {1} {2} - {3} {4} {5} {6} {7} {8}", a.x, a.y, a.z, b.x, b.y, b.z,
                            c.playername, ((IPEndPoint)c.socket.RemoteEndPoint).Address.ToString(),
                            d_Map.GetBlock(a.x, a.y, a.z)));
                    }
                    break;
                case ClientPacketId.PositionandOrientation:
                    {
                        var p = packet.PositionAndOrientation;
                        clients[clientid].PositionMul32GlX = p.X;
                        clients[clientid].PositionMul32GlY = p.Y;
                        clients[clientid].PositionMul32GlZ = p.Z;
                        clients[clientid].positionheading = p.Heading;
                        clients[clientid].positionpitch = p.Pitch;
                        foreach (var k in clients)
                        {
                            if (k.Key != clientid)
                            {
                                if (DistanceSquared(PlayerBlockPosition(clients[k.Key]), PlayerBlockPosition(clients[clientid])) <= PlayerDrawDistance * PlayerDrawDistance)
                                {
                                    SendPlayerTeleport(k.Key, clientid, p.X, p.Y, p.Z, p.Heading, p.Pitch);
                                }
                            }
                        }
                    }
                    break;
                case ClientPacketId.Message:
                    {
                        packet.Message.Message = packet.Message.Message.Trim();
                        // server command
                        if (packet.Message.Message.StartsWith("/"))
                        {
                            string[] ss = packet.Message.Message.Split(new[] { ' ' });
                            string command = ss[0].Replace("/", "");
                            string argument = packet.Message.Message.IndexOf(" ") < 0 ? "" : packet.Message.Message.Substring(packet.Message.Message.IndexOf(" ") + 1);
                            this.CommandInterpreter(clientid, command, argument);
                        }
                        // client command
                        else if (packet.Message.Message.StartsWith("."))
                        {
                            break;
                        }
                        // chat message
                        else
                        {
                            string message = packet.Message.Message;
                            for (int i = 0; i < modEventHandlers.onplayerchat.Count; i++)
                            {
                                message = modEventHandlers.onplayerchat[i](clientid, message, packet.Message.IsTeamchat);
                            }
                            if (clients[clientid].privileges.Contains(ServerClientMisc.Privilege.chat))
                            {
                                if (message == null)
                                {
                                    break;
                                }
                                SendMessageToAll(string.Format("{0}: {1}", clients[clientid].ColoredPlayername(colorNormal), message));
                                ChatLog(string.Format("{0}: {1}", clients[clientid].playername, message));
                            }
                            else
                            {
                                SendMessage(clientid, string.Format("{0}Insufficient privileges to chat.", colorError));
                            }
                        }
                    }
                    break;
                case ClientPacketId.Craft:
                    DoCommandCraft(true, packet.Craft);
                    break;
                case ClientPacketId.InventoryAction:
                    DoCommandInventory(clientid, packet.InventoryAction);
                    break;
                case ClientPacketId.Health:
                    {
                        //todo server side
                        var stats = GetPlayerStats(clients[clientid].playername);
                        stats.CurrentHealth = packet.Health.CurrentHealth;
                        if (stats.CurrentHealth < 1)
                        {
                            //death
                            //todo respawn
                            stats.CurrentHealth = stats.MaxHealth;
                        }
                        clients[clientid].IsPlayerStatsDirty = true;
                    }
                    break;
                case ClientPacketId.MonsterHit:
                    HitMonsters(clientid, packet.Health.CurrentHealth);
                    break;
                case ClientPacketId.DialogClick:
                    for (int i = 0; i < modEventHandlers.ondialogclick.Count; i++)
                    {
                        modEventHandlers.ondialogclick[i](clientid, packet.DialogClick.WidgetId);
                    }
                    break;
                case ClientPacketId.Shot:
                    PlaySoundAtExceptPlayer((int)packet.Shot.FromX, (int)packet.Shot.FromZ, (int)packet.Shot.FromY, (pistolcycle++ % 2 == 0) ? "M1GarandGun-SoundBible.com-1519788442.wav" : "M1GarandGun-SoundBible.com-15197884422.wav", clientid);
                    if (BlockTypes[packet.Shot.WeaponBlock].ProjectileSpeed == 0)
                    {
                        SendBullet(clientid, packet.Shot.FromX, packet.Shot.FromY, packet.Shot.FromZ,
                            packet.Shot.ToX, packet.Shot.ToY, packet.Shot.ToZ, 150);
                    }
                    else
                    {
                        Vector3 from = new Vector3(packet.Shot.FromX, packet.Shot.FromY, packet.Shot.FromZ);
                        Vector3 to = new Vector3(packet.Shot.ToX, packet.Shot.ToY, packet.Shot.ToZ);
                        Vector3 v = to - from;
                        v.Normalize();
                        v *= BlockTypes[packet.Shot.WeaponBlock].ProjectileSpeed;
                        SendProjectile(clientid, packet.Shot.FromX, packet.Shot.FromY, packet.Shot.FromZ,
                            v.X, v.Y, v.Z, packet.Shot.WeaponBlock, packet.Shot.ExplodesAfter);
                        return;
                    }
                    for (int i = 0; i < modEventHandlers.onweaponshot.Count; i++)
                    {
                        modEventHandlers.onweaponshot[i](clientid, packet.Shot.WeaponBlock);
                    }
                    if (clients[clientid].LastPing < 0.3)
                    {
                        if (packet.Shot.HitPlayer != -1)
                        {
                            //client-side shooting
                            for (int i = 0; i < modEventHandlers.onweaponhit.Count; i++)
                            {
                                modEventHandlers.onweaponhit[i](clientid, packet.Shot.HitPlayer, packet.Shot.WeaponBlock, packet.Shot.HitHead);
                            }
                        }
                        return;
                    }
                    foreach (var k in clients)
                    {
                        if (k.Key == clientid)
                        {
                            continue;
                        }
                        ManicDigger.Collisions.Line3D pick = new ManicDigger.Collisions.Line3D();
                        pick.Start = new Vector3(packet.Shot.FromX, packet.Shot.FromY, packet.Shot.FromZ);
                        pick.End = new Vector3(packet.Shot.ToX, packet.Shot.ToY, packet.Shot.ToZ);

                        Vector3 feetpos = new Vector3((float)k.Value.PositionMul32GlX / 32, (float)k.Value.PositionMul32GlY / 32, (float)k.Value.PositionMul32GlZ / 32);
                        //var p = PlayerPositionSpawn;
                        ManicDigger.Collisions.Box3D bodybox = new ManicDigger.Collisions.Box3D();
                        float headsize = (k.Value.ModelHeight - k.Value.EyeHeight) * 2; //0.4f;
                        float h = k.Value.ModelHeight - headsize;
                        float r = 0.35f;

                        bodybox.AddPoint(feetpos.X - r, feetpos.Y + 0, feetpos.Z - r);
                        bodybox.AddPoint(feetpos.X - r, feetpos.Y + 0, feetpos.Z + r);
                        bodybox.AddPoint(feetpos.X + r, feetpos.Y + 0, feetpos.Z - r);
                        bodybox.AddPoint(feetpos.X + r, feetpos.Y + 0, feetpos.Z + r);

                        bodybox.AddPoint(feetpos.X - r, feetpos.Y + h, feetpos.Z - r);
                        bodybox.AddPoint(feetpos.X - r, feetpos.Y + h, feetpos.Z + r);
                        bodybox.AddPoint(feetpos.X + r, feetpos.Y + h, feetpos.Z - r);
                        bodybox.AddPoint(feetpos.X + r, feetpos.Y + h, feetpos.Z + r);

                        ManicDigger.Collisions.Box3D headbox = new ManicDigger.Collisions.Box3D();

                        headbox.AddPoint(feetpos.X - r, feetpos.Y + h, feetpos.Z - r);
                        headbox.AddPoint(feetpos.X - r, feetpos.Y + h, feetpos.Z + r);
                        headbox.AddPoint(feetpos.X + r, feetpos.Y + h, feetpos.Z - r);
                        headbox.AddPoint(feetpos.X + r, feetpos.Y + h, feetpos.Z + r);

                        headbox.AddPoint(feetpos.X - r, feetpos.Y + h + headsize, feetpos.Z - r);
                        headbox.AddPoint(feetpos.X - r, feetpos.Y + h + headsize, feetpos.Z + r);
                        headbox.AddPoint(feetpos.X + r, feetpos.Y + h + headsize, feetpos.Z - r);
                        headbox.AddPoint(feetpos.X + r, feetpos.Y + h + headsize, feetpos.Z + r);

                        if (ManicDigger.Collisions.Intersection.CheckLineBoxExact(pick, headbox) != null)
                        {
                            for (int i = 0; i < modEventHandlers.onweaponhit.Count; i++)
                            {
                                modEventHandlers.onweaponhit[i](clientid, k.Key, packet.Shot.WeaponBlock, true);
                            }
                        }
                        else if (ManicDigger.Collisions.Intersection.CheckLineBoxExact(pick, bodybox) != null)
                        {
                            for (int i = 0; i < modEventHandlers.onweaponhit.Count; i++)
                            {
                                modEventHandlers.onweaponhit[i](clientid, k.Key, packet.Shot.WeaponBlock, false);
                            }
                        }
                    }
                    break;
                case ClientPacketId.SpecialKey:
                    for (int i = 0; i < modEventHandlers.onspecialkey.Count; i++)
                    {
                        modEventHandlers.onspecialkey[i](clientid, packet.SpecialKey.key);
                    }
                    break;
                case ClientPacketId.ActiveMaterialSlot:
                    clients[clientid].ActiveMaterialSlot = packet.ActiveMaterialSlot.ActiveMaterialSlot;
                    for (int i = 0; i < modEventHandlers.changedactivematerialslot.Count; i++)
                    {
                        modEventHandlers.changedactivematerialslot[i](clientid);
                    }
                    break;
                case ClientPacketId.Leave:
                    KillPlayer(clientid);
                    break;
                case ClientPacketId.Reload:
                    break;
                default:
                    Console.WriteLine("Invalid packet: {0}, clientid:{1}", packet.PacketId, clientid);
                    break;
            }
        }

        private void SendProjectile(int player, float fromx, float fromy, float fromz, float velocityx, float velocityy, float velocityz, int block, float explodesafter)
        {
            foreach (var k in clients)
            {
                if (k.Key == player)
                {
                    continue;
                }
                PacketServer p = new PacketServer();
                p.PacketId = ServerPacketId.Projectile;
                p.Projectile = new PacketServerProjectile()
                {
                    FromX = fromx,
                    FromY = fromy,
                    FromZ = fromz,
                    VelocityX = velocityx,
                    VelocityY = velocityy,
                    VelocityZ = velocityz,
                    BlockId = block,
                    ExplodesAfter = explodesafter,
                };
                SendPacket(k.Key, Serialize(p));
            }
        }

        private void SendBullet(int player, float fromx, float fromy, float fromz, float tox, float toy, float toz, float speed)
        {
            foreach (var k in clients)
            {
                if (k.Key == player)
                {
                    continue;
                }
                PacketServer p = new PacketServer();
                p.PacketId = ServerPacketId.Bullet;
                p.Bullet = new PacketServerBullet()
                {
                    FromX = fromx,
                    FromY = fromy,
                    FromZ = fromz,
                    ToX = tox,
                    ToY = toy,
                    ToZ = toz,
                    Speed = speed
                };
                SendPacket(k.Key, Serialize(p));
            }
        }
        int pistolcycle;
        public Vector3i GetPlayerSpawnPositionMul32(int clientid)
        {
            Vector3i position;
            ManicDigger.Spawn playerSpawn = null;
            // Check if there is a spawn entry for his assign group
            if (clients[clientid].clientGroup.Spawn != null)
            {
                playerSpawn = clients[clientid].clientGroup.Spawn;
            }
            // Check if there is an entry in clients with spawn member (overrides group spawn).
            foreach (ManicDigger.Client client in serverClient.Clients)
            {
                if (client.Name.Equals(clients[clientid].playername, StringComparison.InvariantCultureIgnoreCase))
                {
                    if (client.Spawn != null)
                    {
                        playerSpawn = client.Spawn;
                    }
                    break;
                }
            }

            if (playerSpawn == null)
            {
                position = new Vector3i(this.defaultPlayerSpawn.x * 32, this.defaultPlayerSpawn.z * 32, this.defaultPlayerSpawn.y * 32);
            }
            else
            {
                position = this.SpawnToVector3i(playerSpawn);
            }
            return position;
        }

        public void SendPlayerSpawnToAll(int clientid)
        {
            foreach (var k in clients)
            {
                SendPlayerSpawn(k.Key, clientid);
            }
        }

        public void SendPlayerSpawn(int clientid, int spawnedplayer)
        {
            Client c = clients[spawnedplayer];
            PacketServerSpawnPlayer p = new PacketServerSpawnPlayer()
            {
                PlayerId = spawnedplayer,
                PlayerName = c.playername,
                PositionAndOrientation = new PositionAndOrientation()
                {
                    X = c.PositionMul32GlX,
                    Y = c.PositionMul32GlY,
                    Z = c.PositionMul32GlZ,
                    Heading = (byte)c.positionheading,
                    Pitch = (byte)c.positionpitch,
                },
                Model = c.Model,
                Texture = c.Texture,
                EyeHeight = c.EyeHeight,
                ModelHeight = c.ModelHeight,
            };
            if (clients[spawnedplayer].IsSpectator && (!clients[clientid].IsSpectator))
            {
                p.PositionAndOrientation.X = -1000 * 32;
                p.PositionAndOrientation.Y = -1000 * 32;
                p.PositionAndOrientation.Z = 0;
            }
            PacketServer pp = new PacketServer() { PacketId = ServerPacketId.SpawnPlayer, SpawnPlayer = p };
            SendPacket(clientid, Serialize(pp));
        }


        public int PlayerDrawDistance = 128;

        private void RunInClientSandbox(string script, int clientid)
        {
            var client = GetClient(clientid);
            if (!config.AllowScripting)
            {
                SendMessage(clientid, "Server scripts disabled.", MessageType.Error);
                return;
            }
            if (!client.privileges.Contains(ServerClientMisc.Privilege.run))
            {
                SendMessage(clientid, "Insufficient privileges to access this command.", MessageType.Error);
                return;
            }
            ServerEventLog(string.Format("{0} runs script:\n{1}", client.playername, script));
            if (client.Interpreter == null)
            {
                client.Interpreter = new JavaScriptInterpreter();
                client.Console = new ScriptConsole(this, clientid);
                client.Console.InjectConsoleCommands(client.Interpreter);
                client.Interpreter.SetVariables(new Dictionary<string, object>() { { "client", client }, { "server", this }, });
                client.Interpreter.Execute("function inspect(obj) { for( property in obj) { out(property)}}");
            }
            var interpreter = client.Interpreter;
            object result;
            SendMessage(clientid, colorNormal + script);
            if (interpreter.Execute(script, out result))
            {
                try
                {
                    SendMessage(clientid, colorSuccess + " => " + result);
                }
                catch (FormatException e) // can happen
                {
                    SendMessage(clientid, colorError + "Error. " + e.Message);
                }
                return;
            }
            SendMessage(clientid, colorError + "Error.");
        }

        public string colorNormal = "&f"; //white
        public string colorHelp = "&4"; //red
        public string colorOpUsername = "&2"; //green
        public string colorSuccess = "&2"; //green
        public string colorError = "&4"; //red
        public string colorImportant = "&4"; // red
        public string colorAdmin = "&e"; //yellow
        public enum MessageType { Normal, Important, Help, OpUsername, Success, Error, Admin, White, Red, Green, Yellow }
        private string MessageTypeToString(MessageType type)
        {
            switch (type)
            {
                case MessageType.Normal:
                case MessageType.White:
                    return colorNormal;
                case MessageType.Important:
                    return colorImportant;
                case MessageType.Help:
                case MessageType.Red:
                    return colorHelp;
                case MessageType.OpUsername:
                case MessageType.Green:
                    return colorOpUsername;
                case MessageType.Error:
                    return colorError;
                case MessageType.Success:
                    return colorSuccess;
                case MessageType.Admin:
                case MessageType.Yellow:
                    return colorAdmin;
                default:
                    return colorNormal;
            }
        }
        bool CompareByteArray(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) { return false; }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) { return false; }
            }
            return true;
        }
        private void NotifyBlock(int x, int y, int z, int blocktype)
        {
            foreach (var k in clients)
            {
                SendSetBlock(k.Key, x, y, z, blocktype);
            }
        }
        bool ENABLE_FINITEINVENTORY { get { return !config.IsCreative; } }
        private bool DoCommandCraft(bool execute, PacketClientCraft cmd)
        {
            if (d_Map.GetBlock(cmd.X, cmd.Y, cmd.Z) != d_Data.BlockIdCraftingTable)
            {
                return false;
            }
            if (cmd.RecipeId < 0 || cmd.RecipeId >= craftingrecipes.Count)
            {
                return false;
            }
            List<Vector3i> table = d_CraftingTableTool.GetTable(new Vector3i(cmd.X, cmd.Y, cmd.Z));
            List<int> ontable = d_CraftingTableTool.GetOnTable(table);
            List<int> outputtoadd = new List<int>();
            //for (int i = 0; i < craftingrecipes.Count; i++)
            int i = cmd.RecipeId;
            {
                //try apply recipe. if success then try until fail.
                for (; ; )
                {
                    //check if ingredients available
                    foreach (Ingredient ingredient in craftingrecipes[i].ingredients)
                    {
                        if (ontable.FindAll(v => v == ingredient.Type).Count < ingredient.Amount)
                        {
                            goto nextrecipe;
                        }
                    }
                    //remove ingredients
                    foreach (Ingredient ingredient in craftingrecipes[i].ingredients)
                    {
                        for (int ii = 0; ii < ingredient.Amount; ii++)
                        {
                            //replace on table
                            ReplaceOne(ontable, ingredient.Type, d_Data.BlockIdEmpty);
                        }
                    }
                    //add output
                    for (int z = 0; z < craftingrecipes[i].output.Amount; z++)
                    {
                        outputtoadd.Add(craftingrecipes[i].output.Type);
                    }
                }
            nextrecipe:
                ;
            }
            foreach (var v in outputtoadd)
            {
                ReplaceOne(ontable, d_Data.BlockIdEmpty, v);
            }
            int zz = 0;
            if (execute)
            {
                foreach (var v in table)
                {
                    SetBlockAndNotify(v.x, v.y, v.z + 1, ontable[zz]);
                    zz++;
                }
            }
            return true;
        }
        private void ReplaceOne<T>(List<T> l, T from, T to)
        {
            for (int ii = 0; ii < l.Count; ii++)
            {
                if (l[ii].Equals(from))
                {
                    l[ii] = to;
                    break;
                }
            }
        }
        public IGameDataItems d_DataItems;
        public InventoryUtil GetInventoryUtil(Inventory inventory)
        {
            InventoryUtil util = new InventoryUtil();
            util.d_Inventory = inventory;
            util.d_Items = d_DataItems;
            return util;
        }
        private void DoCommandInventory(int player_id, PacketClientInventoryAction cmd)
        {
            Inventory inventory = GetPlayerInventory(clients[player_id].playername).Inventory;
            var s = new InventoryServer();
            s.d_Inventory = inventory;
            s.d_InventoryUtil = GetInventoryUtil(inventory);
            s.d_Items = d_DataItems;
            s.d_DropItem = this;

            switch (cmd.Action)
            {
                case InventoryActionType.Click:
                    s.InventoryClick(cmd.A);
                    break;
                case InventoryActionType.MoveToInventory:
                    s.MoveToInventory(cmd.A);
                    break;
                case InventoryActionType.WearItem:
                    s.WearItem(cmd.A, cmd.B);
                    break;
                default:
                    break;
            }
            clients[player_id].IsInventoryDirty = true;
            NotifyInventory(player_id);
        }

        private bool IsFillAreaValid(Client client, Vector3i a, Vector3i b)
        {
            if (!MapUtil.IsValidPos(this.d_Map, a.x, a.y, a.z) || !MapUtil.IsValidPos(this.d_Map, b.x, b.y, b.z))
            {
                return false;
            }

            // TODO: Is there a more efficient way?
            int startx = Math.Min(a.x, b.x);
            int endx = Math.Max(a.x, b.x);
            int starty = Math.Min(a.y, b.y);
            int endy = Math.Max(a.y, b.y);
            int startz = Math.Min(a.z, b.z);
            int endz = Math.Max(a.z, b.z);
            for (int x = startx; x <= endx; x++)
            {
                for (int y = starty; y <= endy; y++)
                {
                    for (int z = startz; z <= endz; z++)
                    {
                        if (!config.CanUserBuild(client, x, y, z))
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }
        private bool DoFillArea(int player_id, PacketClientFillArea fill, int blockCount)
        {
            Vector3i a = new Vector3i(fill.X1, fill.Y1, fill.Z1);
            Vector3i b = new Vector3i(fill.X2, fill.Y2, fill.Z2);

            int startx = Math.Min(a.x, b.x);
            int endx = Math.Max(a.x, b.x);
            int starty = Math.Min(a.y, b.y);
            int endy = Math.Max(a.y, b.y);
            int startz = Math.Min(a.z, b.z);
            int endz = Math.Max(a.z, b.z);

            int blockType = fill.BlockType;
            blockType = d_Data.WhenPlayerPlacesGetsConvertedTo[blockType];

            Inventory inventory = GetPlayerInventory(clients[player_id].playername).Inventory;
            var item = inventory.RightHand[fill.MaterialSlot];
            if (item == null)
            {
                return false;
            }
            for (int x = startx; x <= endx; ++x)
            {
                for (int y = starty; y <= endy; ++y)
                {
                    for (int z = startz; z <= endz; ++z)
                    {
                        PacketClientSetBlock cmd = new PacketClientSetBlock();
                        cmd.X = x;
                        cmd.Y = y;
                        cmd.Z = z;
                        cmd.MaterialSlot = fill.MaterialSlot;
                        if (GetBlock(x, y, z) != 0)
                        {
                            cmd.Mode = BlockSetMode.Destroy;
                            DoCommandBuild(player_id, true, cmd);
                        }
                        if (blockType != d_Data.BlockIdFillArea)
                        {
                            cmd.Mode = BlockSetMode.Create;
                            DoCommandBuild(player_id,true, cmd);
                        }
                    }
                }
            }
            return true;
        }
        bool ClientSeenChunk(int clientid, int vx, int vy, int vz)
        {
            int pos = MapUtil.Index3d(vx / chunksize, vy / chunksize, vz / chunksize, d_Map.MapSizeX / chunksize, d_Map.MapSizeY / chunksize);
            return clients[clientid].chunksseen[pos];
        }
        void ClientSeenChunkSet(int clientid, int vx, int vy, int vz, int time)
        {
            int pos = MapUtil.Index3d(vx / chunksize, vy / chunksize, vz / chunksize, d_Map.MapSizeX / chunksize, d_Map.MapSizeY / chunksize);
            clients[clientid].chunksseen[pos] = true;
            clients[clientid].chunksseenTime[pos] = time;
        }
        private void SendFillArea(int clientid, Vector3i a, Vector3i b, int blockType, int blockCount)
        {
            // TODO: better to send a chunk?

            Vector3i v = new Vector3i((a.x / chunksize) * chunksize,
                (a.y / chunksize) * chunksize, (a.z / chunksize) * chunksize);
            Vector3i w = new Vector3i((b.x / chunksize) * chunksize,
                (b.y / chunksize) * chunksize, (b.z / chunksize) * chunksize);

            // TODO: Is it sufficient to regard only start- and endpoint?
            if (!ClientSeenChunk(clientid, v.x, v.y, v.z) && !ClientSeenChunk(clientid, w.x, w.y, w.z))
            {
                return;
            }

            PacketServerFillArea p = new PacketServerFillArea()
            {
                X1 = a.x,
                Y1 = a.y,
                Z1 = a.z,
                X2 = b.x,
                Y2 = b.y,
                Z2 = b.z,
                BlockType = blockType,
                BlockCount = blockCount
            };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.FillArea, FillArea = p }));
        }
        private void SetFillAreaLimit(int clientid)
        {
            Client client = GetClient(clientid);
            if (client == null)
            {
                return;
            }

            int maxFill = 500;
            if (serverClient.DefaultFillLimit != null)
            {
                maxFill = serverClient.DefaultFillLimit.Value;
            }

            // Check if there is a fill-limit entry for his assigned group.
            if (client.clientGroup.FillLimit != null)
            {
                maxFill = client.clientGroup.FillLimit.Value;
            }

            // Check if there is an entry in clients with fill-limit member (overrides group fill-limit).
            foreach (ManicDigger.Client clientConfig in serverClient.Clients)
            {
                if (clientConfig.Name.Equals(client.playername, StringComparison.InvariantCultureIgnoreCase))
                {
                    if (clientConfig.FillLimit != null)
                    {
                        maxFill = clientConfig.FillLimit.Value;
                    }
                    break;
                }
            }
            client.FillLimit = maxFill;
            SendFillAreaLimit(clientid, maxFill);
        }



        private void SendFillAreaLimit(int clientid, int limit)
        {
            PacketServerFillAreaLimit p = new PacketServerFillAreaLimit()
            {
                Limit = limit
            };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.FillAreaLimit, FillAreaLimit = p }));
        }

        private bool DoCommandBuild(int player_id, bool execute, PacketClientSetBlock cmd)
        {
            Vector3 v = new Vector3(cmd.X, cmd.Y, cmd.Z);
            Inventory inventory = GetPlayerInventory(clients[player_id].playername).Inventory;
            if (cmd.Mode == BlockSetMode.Use)
            {
                for (int i = 0; i < modEventHandlers.onuse.Count; i++)
                {
                    modEventHandlers.onuse[i](player_id, cmd.X, cmd.Y, cmd.Z);
                }
                return true;
            }
            if (cmd.Mode == BlockSetMode.UseWithTool)
            {
                for (int i = 0; i < modEventHandlers.onusewithtool.Count; i++)
                {
                    modEventHandlers.onusewithtool[i](player_id, cmd.X, cmd.Y, cmd.Z, cmd.BlockType);
                }
                return true;
            }
            if (cmd.Mode == BlockSetMode.Create
                && d_Data.Rail[cmd.BlockType] != 0)
            {
                return DoCommandBuildRail(player_id, execute, cmd);
            }
            if (cmd.Mode == (int)BlockSetMode.Destroy
                && d_Data.Rail[d_Map.GetBlock(cmd.X, cmd.Y, cmd.Z)] != 0)
            {
                return DoCommandRemoveRail(player_id, execute, cmd);
            }
            if (cmd.Mode == BlockSetMode.Create)
            {
                int oldblock = d_Map.GetBlock(cmd.X, cmd.Y, cmd.Z);
                if (!(oldblock == 0 || BlockTypes[oldblock].IsFluid()))
                {
                    return false;
                }
                var item = inventory.RightHand[cmd.MaterialSlot];
                if (item == null)
                {
                    return false;
                }
                switch (item.ItemClass)
                {
                    case ItemClass.Block:
                        item.BlockCount--;
                        if (item.BlockCount == 0)
                        {
                            inventory.RightHand[cmd.MaterialSlot] = null;
                        }
                        if (d_Data.Rail[item.BlockId] != 0)
                        {
                        }
                        SetBlockAndNotify(cmd.X, cmd.Y, cmd.Z, item.BlockId);
                        for (int i = 0; i < modEventHandlers.onbuild.Count; i++)
                        {
                            modEventHandlers.onbuild[i](player_id, cmd.X, cmd.Y, cmd.Z);
                        }
                        break;
                    default:
                        //TODO
                        return false;
                }
            }
            else
            {
                var item = new Item();
                item.ItemClass = ItemClass.Block;
                int blockid = d_Map.GetBlock(cmd.X, cmd.Y, cmd.Z);
                item.BlockId = d_Data.WhenPlayerPlacesGetsConvertedTo[blockid];
                if (!config.IsCreative)
                {
                    GetInventoryUtil(inventory).GrabItem(item, cmd.MaterialSlot);
                }
                SetBlockAndNotify(cmd.X, cmd.Y, cmd.Z, SpecialBlockId.Empty);
                for (int i = 0; i < modEventHandlers.ondelete.Count; i++)
                {
                    modEventHandlers.ondelete[i](player_id, cmd.X, cmd.Y, cmd.Z, blockid);
                }
            }
            clients[player_id].IsInventoryDirty = true;
            NotifyInventory(player_id);
            return true;
        }

        private bool DoCommandBuildRail(int player_id, bool execute, PacketClientSetBlock cmd)
        {
            Inventory inventory = GetPlayerInventory(clients[player_id].playername).Inventory;
            int oldblock = d_Map.GetBlock(cmd.X, cmd.Y, cmd.Z);
            //int blockstoput = 1;
            if (!(oldblock == SpecialBlockId.Empty || d_Data.IsRailTile(oldblock)))
            {
                return false;
            }

            //count how many rails will be created
            int oldrailcount = 0;
            if (d_Data.IsRailTile(oldblock))
            {
                oldrailcount = MyLinq.Count(
                    DirectionUtils.ToRailDirections(
                    (RailDirectionFlags)(oldblock - d_Data.BlockIdRailstart)));
            }
            int newrailcount = MyLinq.Count(
                DirectionUtils.ToRailDirections(
                (RailDirectionFlags)(cmd.BlockType - d_Data.BlockIdRailstart)));
            int blockstoput = newrailcount - oldrailcount;

            Item item = inventory.RightHand[cmd.MaterialSlot];
            if (!(item.ItemClass == ItemClass.Block && d_Data.Rail[item.BlockId] != 0))
            {
                return false;
            }
            item.BlockCount -= blockstoput;
            if (item.BlockCount == 0)
            {
                inventory.RightHand[cmd.MaterialSlot] = null;
            }
            SetBlockAndNotify(cmd.X, cmd.Y, cmd.Z, cmd.BlockType);

            clients[player_id].IsInventoryDirty = true;
            NotifyInventory(player_id);
            return true;
        }

        private bool DoCommandRemoveRail(int player_id, bool execute, PacketClientSetBlock cmd)
        {
            Inventory inventory = GetPlayerInventory(clients[player_id].playername).Inventory;
            //add to inventory
            int blocktype = d_Map.GetBlock(cmd.X, cmd.Y, cmd.Z);
            blocktype = d_Data.WhenPlayerPlacesGetsConvertedTo[blocktype];
            if ((!d_Data.IsValid[blocktype])
                || blocktype == SpecialBlockId.Empty)
            {
                return false;
            }
            int blockstopick = 1;
            if (d_Data.IsRailTile(blocktype))
            {
                blockstopick = MyLinq.Count(
                    DirectionUtils.ToRailDirections(
                    (RailDirectionFlags)(blocktype - d_Data.BlockIdRailstart)));
            }

            var item = new Item();
            item.ItemClass = ItemClass.Block;
            item.BlockId = d_Data.WhenPlayerPlacesGetsConvertedTo[blocktype];
            item.BlockCount = blockstopick;
            if (!config.IsCreative)
            {
                GetInventoryUtil(inventory).GrabItem(item, cmd.MaterialSlot);
            }
            SetBlockAndNotify(cmd.X, cmd.Y, cmd.Z, SpecialBlockId.Empty);

            clients[player_id].IsInventoryDirty = true;
            NotifyInventory(player_id);
            return true;
        }
        public void SetBlockAndNotify(int x, int y, int z, int blocktype)
        {
            d_Map.SetBlockNotMakingDirty(x, y, z, blocktype);
            NotifyBlock(x, y, z, blocktype);
        }
        private int TotalAmount(Dictionary<int, int> inventory)
        {
            int sum = 0;
            foreach (var k in inventory)
            {
                sum += k.Value;
            }
            return sum;
        }
        private void RemoveEquivalent(Dictionary<int, int> inventory, int blocktype, int count)
        {
            int removed = 0;
            for (int i = 0; i < count; i++)
            {
                foreach (var k in new Dictionary<int, int>(inventory))
                {
                    if (EquivalentBlock(k.Key, blocktype)
                        && k.Value > 0)
                    {
                        inventory[k.Key]--;
                        removed++;
                        goto removenext;
                    }
                }
            removenext:
                ;
            }
            if (removed != count)
            {
                //throw new Exception();
            }
        }
        private int GetEquivalentCount(Dictionary<int, int> inventory, int blocktype)
        {
            int count = 0;
            foreach (var k in inventory)
            {
                if (EquivalentBlock(k.Key, blocktype))
                {
                    count += k.Value;
                }
            }
            return count;
        }
        private bool EquivalentBlock(int blocktypea, int blocktypeb)
        {
            if (d_Data.IsRailTile(blocktypea) && d_Data.IsRailTile(blocktypeb))
            {
                return true;
            }
            return blocktypea == blocktypeb;
        }
        public byte[] Serialize(PacketServer p)
        {
            MemoryStream ms = new MemoryStream();
            Serializer.Serialize(ms, p);
            return ms.ToArray();
        }
        private string GenerateUsername(string name)
        {
            string defaultname = name;
            int appendNumber = 1;
            bool exists;

            do
            {
                exists = false;
                foreach (var k in clients)
                {
                    if (k.Value.playername.Equals(defaultname + appendNumber))
                    {
                        exists = true;
                        appendNumber++;
                        break;
                    }
                }
            } while (exists);

            return defaultname + appendNumber;
        }
        public void ServerMessageToAll(string message, MessageType color)
        {
            this.SendMessageToAll(MessageTypeToString(color) + message);
            ServerEventLog(string.Format("SERVER MESSAGE: {0}.", message));
        }
        public void SendMessageToAll(string message)
        {
            Console.WriteLine("Message to all: " + message);
            foreach (var k in clients)
            {
                SendMessage(k.Key, message);
            }
        }
        private void SendSetBlock(int clientid, int x, int y, int z, int blocktype)
        {
            if (!ClientSeenChunk(clientid, (x / chunksize) * chunksize,
                (y / chunksize) * chunksize, (z / chunksize) * chunksize))
            {
                return;
            }
            PacketServerSetBlock p = new PacketServerSetBlock() { X = x, Y = y, Z = z, BlockType = blocktype };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.SetBlock, SetBlock = p }));
        }
        public void SendSound(int clientid, string name, int x, int y, int z)
        {
            PacketServerSound p = new PacketServerSound() { Name = name, X = x, Y = y, Z = z };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.Sound, Sound = p }));
        }
        private void SendPlayerSpawnPosition(int clientid, int x, int y, int z)
        {
            PacketServerPlayerSpawnPosition p = new PacketServerPlayerSpawnPosition()
            {
                X = x,
                Y = y,
                Z = z
            };
            SendPacket(clientid, Serialize(new PacketServer()
            {
                PacketId = ServerPacketId.PlayerSpawnPosition,
                PlayerSpawnPosition = p,
            }));
        }
        public void SendPlayerTeleport(int clientid, int playerid, int x, int y, int z, byte heading, byte pitch)
        {
            //spectators invisible to players
            if (clients[playerid].IsSpectator && (!clients[clientid].IsSpectator))
            {
                return;
            }
            PacketServerPositionAndOrientation p = new PacketServerPositionAndOrientation()
            {
                PlayerId = playerid,
                PositionAndOrientation = new PositionAndOrientation()
                {
                    X = x,
                    Y = y,
                    Z = z,
                    Heading = heading,
                    Pitch = pitch,
                }
            };
            SendPacket(clientid, Serialize(new PacketServer()
            {
                PacketId = ServerPacketId.PlayerPositionAndOrientation,
                PositionAndOrientation = p,
            }));
        }
        //SendPositionAndOrientationUpdate //delta
        //SendPositionUpdate //delta
        //SendOrientationUpdate
        private void SendDespawnPlayer(int clientid, int playerid)
        {
            PacketServerDespawnPlayer p = new PacketServerDespawnPlayer() { PlayerId = playerid };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.DespawnPlayer, DespawnPlayer = p }));
        }

        public void SendMessage(int clientid, string message, MessageType color)
        {
            SendMessage(clientid, MessageTypeToString(color) + message);
        }

        public void SendMessage(int clientid, string message)
        {
            if (clientid == this.serverConsoleId)
            {
                this.serverConsole.Receive(message);
                return;
            }

            string truncated = message; //.Substring(0, Math.Min(64, message.Length));

            PacketServerMessage p = new PacketServerMessage();
            p.PlayerId = clientid;
            p.Message = truncated;
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.Message, Message = p }));
        }
        private void SendDisconnectPlayer(int clientid, string disconnectReason)
        {
            PacketServerDisconnectPlayer p = new PacketServerDisconnectPlayer() { DisconnectReason = disconnectReason };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.DisconnectPlayer, DisconnectPlayer = p }));
        }
        int StatTotalPackets = 0;
        int StatTotalPacketsLength = 0;
        public void SendPacket(int clientid, byte[] packet)
        {
            if (clients[clientid].IsBot)
            {
                return;
            }
            StatTotalPackets++;
            StatTotalPacketsLength += packet.Length;
            try
            {
                INetOutgoingMessage msg = d_MainSocket.CreateMessage();
                msg.Write(packet);
                clients[clientid].socket.SendMessage(msg, MyNetDeliveryMethod.ReliableOrdered, 0);
            }
            catch (Exception)
            {
                KillPlayer(clientid);
            }
        }
        void EmptyCallback(IAsyncResult result)
        {
        }
        public int drawdistance = 128;
        public const int chunksize = 32;
        int chunkdrawdistance { get { return drawdistance / chunksize; } }
        IEnumerable<Vector3i> ChunksAroundPlayer(Vector3i playerpos)
        {
            playerpos.x = (playerpos.x / chunksize) * chunksize;
            playerpos.y = (playerpos.y / chunksize) * chunksize;
            for (int x = -chunkdrawdistance; x <= chunkdrawdistance; x++)
            {
                for (int y = -chunkdrawdistance; y <= chunkdrawdistance; y++)
                {
                    for (int z = 0; z < d_Map.MapSizeZ / chunksize; z++)
                    {
                        var p = new Vector3i(playerpos.x + x * chunksize, playerpos.y + y * chunksize, z * chunksize);
                        if (MapUtil.IsValidPos(d_Map, p.x, p.y, p.z))
                        {
                            yield return p;
                        }
                    }
                }
            }
        }
        byte[] CompressChunkNetwork(ushort[] chunk)
        {
            return d_NetworkCompression.Compress(Misc.UshortArrayToByteArray(chunk));
        }
        byte[] CompressChunkNetwork(byte[, ,] chunk)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(ms);
            for (int z = 0; z <= chunk.GetUpperBound(2); z++)
            {
                for (int y = 0; y <= chunk.GetUpperBound(1); y++)
                {
                    for (int x = 0; x <= chunk.GetUpperBound(0); x++)
                    {
                        bw.Write((byte)chunk[x, y, z]);
                    }
                }
            }
            byte[] compressedchunk = d_NetworkCompression.Compress(ms.ToArray());
            return compressedchunk;
        }
        struct PublicFile
        {
            public string Name;
            public byte[] Data;
        }
        List<PublicFile> PublicFiles()
        {
            List<PublicFile> files = new List<PublicFile>();
            foreach (string path in PublicDataPaths)
            {
                try
                {
                    if (!Directory.Exists(path))
                    {
                        continue;
                    }
                    foreach (string s in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            FileInfo f = new FileInfo(s);
                            if ((f.Attributes & FileAttributes.Hidden) != 0)
                            {
                                continue;
                            }
                            //cache[f.Name] = File.ReadAllBytes(s);
                            files.Add(new PublicFile()
                            {
                                Name = f.Name,
                                Data = File.ReadAllBytes(s),
                            });
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            }
            return files;
        }
        int BlobPartLength = 1024 * 1;
        private void SendBlobs(int clientid)
        {
            SendLevelInitialize(clientid);

            List<PublicFile> files = PublicFiles();
            for (int i = 0; i < files.Count; i++)
            {
                PublicFile f = files[i];
                SendBlobInitialize(clientid, f.Name == "terrain.png" ? terrainTextureMd5 : new byte[16], f.Name);
                byte[] blob = f.Data;
                int totalsent = 0;
                foreach (byte[] part in Parts(blob, BlobPartLength))
                {
                    SendLevelProgress(clientid,
                        (int)(((float)i / files.Count
                        + ((float)totalsent / blob.Length) / files.Count) * 100), "Downloading data...");
                    SendBlobPart(clientid, part);
                    totalsent += part.Length;
                }
                SendBlobFinalize(clientid);
            }
            SendLevelProgress(clientid, 0, "Generating world...");
        }
        static IEnumerable<byte[]> Parts(byte[] blob, int partsize)
        {
            int i = 0;
            for (; ; )
            {
                if (i >= blob.Length) { break; }
                int curpartsize = blob.Length - i;
                if (curpartsize > partsize) { curpartsize = partsize; }
                byte[] part = new byte[curpartsize];
                for (int ii = 0; ii < curpartsize; ii++)
                {
                    part[ii] = blob[i + ii];
                }
                yield return part;
                i += curpartsize;
            }
        }
        private void SendBlobInitialize(int clientid, byte[] hash, string name)
        {
            PacketServerBlobInitialize p = new PacketServerBlobInitialize() { hash = hash, name = name };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.BlobInitialize, BlobInitialize = p }));
        }
        private void SendBlobPart(int clientid, byte[] data)
        {
            PacketServerBlobPart p = new PacketServerBlobPart() { data = data };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.BlobPart, BlobPart = p }));
        }
        private void SendBlobFinalize(int clientid)
        {
            PacketServerBlobFinalize p = new PacketServerBlobFinalize() { };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.BlobFinalize, BlobFinalize = p }));
        }
        public BlockType[] BlockTypes = new BlockType[GlobalVar.MAX_BLOCKTYPES];
        public void SendBlockTypes(int clientid)
        {
            for (int i = 0; i < BlockTypes.Length; i++)
            {
                PacketServerBlockType p1 = new PacketServerBlockType() { Id = i, blocktype = BlockTypes[i] };
                SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.BlockType, BlockType = p1 }));
            }
            PacketServerBlockTypes p = new PacketServerBlockTypes() { };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.BlockTypes, BlockTypes = p }));
        }
        private void SendSunLevels(int clientid)
        {
            PacketServerSunLevels p = new PacketServerSunLevels() { sunlevels = sunlevels };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.SunLevels, SunLevels = p }));
        }
        private void SendLightLevels(int clientid)
        {
            PacketServerLightLevels p = new PacketServerLightLevels() { lightlevels = lightlevels };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.LightLevels, LightLevels = p }));
        }
        private void SendCraftingRecipes(int clientid)
        {
            PacketServerCraftingRecipes p = new PacketServerCraftingRecipes() { CraftingRecipes = craftingrecipes.ToArray() };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.CraftingRecipes, CraftingRecipes = p }));
        }
        private void SendLevelInitialize(int clientid)
        {
            PacketServerLevelInitialize p = new PacketServerLevelInitialize() { };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.LevelInitialize, LevelInitialize = p }));
        }
        private void SendLevelProgress(int clientid, int percentcomplete, string status)
        {
            PacketServerLevelProgress p = new PacketServerLevelProgress() { PercentComplete = percentcomplete, Status = status };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.LevelDataChunk, LevelDataChunk = p }));
        }
        private void SendLevelFinalize(int clientid)
        {
            PacketServerLevelFinalize p = new PacketServerLevelFinalize() { };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.LevelFinalize, LevelFinalize = p }));
        }
        public RenderHint RenderHint = RenderHint.Fast;
        private void SendServerIdentification(int clientid)
        {
            PacketServerIdentification p = new PacketServerIdentification()
            {
                MdProtocolVersion = GameVersion.Version,
                AssignedClientId = clientid,
                ServerName = config.Name,
                ServerMotd = config.Motd,
                UsedBlobsMd5 = new List<byte[]>(new[] { terrainTextureMd5 }),
                TerrainTextureMd5 = terrainTextureMd5,
                MapSizeX = d_Map.MapSizeX,
                MapSizeY = d_Map.MapSizeY,
                MapSizeZ = d_Map.MapSizeZ,
                DisableShadows = enableshadows ? 0 : 1,
                PlayerAreaSize = playerareasize,
                RenderHint = (int)RenderHint,
            };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.ServerIdentification, Identification = p }));
        }
        public void SendFreemoveState(int clientid, bool isEnabled)
        {
            PacketServerFreemove p = new PacketServerFreemove()
            {
                IsEnabled = isEnabled
            };
            SendPacket(clientid, Serialize(new PacketServer() { PacketId = ServerPacketId.Freemove, Freemove = p }));
        }

        byte[] terrainTextureMd5 { get { byte[] b = new byte[16]; b[0] = 1; return b; } }
        MD5 md5 = System.Security.Cryptography.MD5.Create();
        byte[] ComputeMd5(byte[] b)
        {
            return md5.ComputeHash(b);
        }
        string ComputeMd5(string input)
        {
            // step 1, calculate MD5 hash from input
            byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
            byte[] hash = md5.ComputeHash(inputBytes);

            // step 2, convert byte array to hex string
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("X2"));
            }
            return sb.ToString().ToLower();
        }
        public int SIMULATION_KEYFRAME_EVERY = 4;
        public float SIMULATION_STEP_LENGTH = 1f / 64f;
        public struct BlobToSend
        {
            public string Name;
            public byte[] Data;
        }
        public enum ClientStateOnServer
        {
            Connecting,
            LoadingGenerating,
            LoadingSending,
            Playing,
        }
        public class Client
        {
            public int Id = -1;
            public ClientStateOnServer state = ClientStateOnServer.Connecting;
            public int maploadingsentchunks = 0;
            public INetConnection socket;
            public List<byte> received = new List<byte>();
            public Ping Ping = new Ping();
            public float LastPing;
            public string playername = invalidplayername;
            public int PositionMul32GlX;
            public int PositionMul32GlY;
            public int PositionMul32GlZ;
            public int positionheading;
            public int positionpitch;
            public string Model = "player.txt";
            public string Texture;
            public Dictionary<int, int> chunksseenTime = new Dictionary<int, int>();
            public bool[] chunksseen;
            public Dictionary<Vector2i, int> heightmapchunksseen = new Dictionary<Vector2i, int>();
            public ManicDigger.Timer notifyMapTimer;
            public bool IsInventoryDirty = true;
            public bool IsPlayerStatsDirty = true;
            public int FillLimit = 500;
            //public List<byte[]> blobstosend = new List<byte[]>();
            public ManicDigger.Group clientGroup;
            public bool IsBot;
            public void AssignGroup(ManicDigger.Group newGroup)
            {
                this.clientGroup = newGroup;
                this.privileges.Clear();
                this.privileges.AddRange(newGroup.GroupPrivileges);
                this.color = newGroup.GroupColorString();
            }
            public List<string> privileges = new List<string>();
            public string color;
            public string ColoredPlayername(string subsequentColor)
            {
                return this.color + this.playername + subsequentColor;
            }
            public ManicDigger.Timer notifyMonstersTimer;
            public IScriptInterpreter Interpreter;
            public ScriptConsole Console;

            public override string ToString()
            {
                string ip = "";
                if (this.socket != null)
                {
                    ip = ((IPEndPoint)this.socket.RemoteEndPoint).Address.ToString();
                }
                // Format: Playername:Group:Privileges IP
                return string.Format("{0}:{1}:{2} {3}", this.playername, this.clientGroup.Name,
                    ServerClientMisc.PrivilegesString(this.privileges), ip);
            }
            public float EyeHeight = 1.5f;
            public float ModelHeight = 1.7f;
            public float generatingworldprogress;
            public int ActiveMaterialSlot;
            public bool IsSpectator;
        }
        public Dictionary<int, Client> clients = new Dictionary<int, Client>();
        public Dictionary<string, bool> disabledprivileges = new Dictionary<string, bool>();
        public Dictionary<string, bool> extraPrivileges = new Dictionary<string, bool>();
        public Client GetClient(int id)
        {
            if (id == this.serverConsoleId)
            {
                return this.serverConsoleClient;
            }
            if (!clients.ContainsKey(id))
                return null;
            return clients[id];
        }
        public Client GetClient(string name)
        {
            if (serverConsoleClient.playername.Equals(name, StringComparison.InvariantCultureIgnoreCase))
            {
                return serverConsoleClient;
            }
            foreach (var k in clients)
            {
                if (k.Value.playername.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                {
                    return k.Value;
                }
            }
            return null;
        }

        public ServerClient serverClient;
        public ManicDigger.Group defaultGroupGuest;
        public ManicDigger.Group defaultGroupRegistered;
        public Vector3i defaultPlayerSpawn;

        private Vector3i SpawnToVector3i(ManicDigger.Spawn spawn)
        {
            int x = spawn.x;
            int y = spawn.y;
            int z;
            if (!MapUtil.IsValidPos(d_Map, x, y))
            {
                throw new Exception("Invalid default spawn coordinates!");
            }

            if (spawn.z == null)
            {
                z = MapUtil.blockheight(d_Map, 0, x, y);
            }
            else
            {
                z = spawn.z.Value;
                if (!MapUtil.IsValidPos(d_Map, x, y, z))
                {
                    throw new Exception("Invalid default spawn coordinates!");
                }
            }
            return new Vector3i(x * 32, z * 32, y * 32);
        }

        public void LoadServerClient()
        {
            string filename = "ServerClient.txt";
            if (!File.Exists(Path.Combine(GameStorePath.gamepathconfig, filename)))
            {
                Console.WriteLine("Server client configuration file not found, creating new.");
                SaveServerClient();
            }
            else
            {
                try
                {
                    using (TextReader textReader = new StreamReader(Path.Combine(GameStorePath.gamepathconfig, filename)))
                    {
                        XmlSerializer deserializer = new XmlSerializer(typeof(ServerClient));
                        serverClient = (ServerClient)deserializer.Deserialize(textReader);
                        textReader.Close();
                        serverClient.Groups.Sort();
                        SaveServerClient();
                    }
                }
                catch //This if for the original format
                {
                    using (Stream s = new MemoryStream(File.ReadAllBytes(Path.Combine(GameStorePath.gamepathconfig, filename))))
                    {
                        serverClient = new ServerClient();
                        StreamReader sr = new StreamReader(s);
                        XmlDocument d = new XmlDocument();
                        d.Load(sr);
                        serverClient.Format = int.Parse(XmlTool.XmlVal(d, "/ManicDiggerServerClient/Format"));
                        serverClient.DefaultGroupGuests = XmlTool.XmlVal(d, "/ManicDiggerServerClient/DefaultGroupGuests");
                        serverClient.DefaultGroupRegistered = XmlTool.XmlVal(d, "/ManicDiggerServerClient/DefaultGroupRegistered");
                    }
                    //Save with new version.
                    SaveServerClient();
                }
            }
            if (serverClient.DefaultSpawn == null)
            {
                // server sets a default spawn (middle of map)
                int x = d_Map.MapSizeX / 2;
                int y = d_Map.MapSizeY / 2;
                this.defaultPlayerSpawn =  DontSpawnPlayerInWater(new Vector3i(x, y, MapUtil.blockheight(d_Map, 0, x, y)));
            }
            else
            {
                int z;
                if (serverClient.DefaultSpawn.z == null)
                {
                    z = MapUtil.blockheight(d_Map, 0, serverClient.DefaultSpawn.x, serverClient.DefaultSpawn.y);
                }
                else
                {
                    z = serverClient.DefaultSpawn.z.Value;
                }
                this.defaultPlayerSpawn = new Vector3i(serverClient.DefaultSpawn.x, serverClient.DefaultSpawn.y, z);
            }

            this.defaultGroupGuest = serverClient.Groups.Find(
                delegate(ManicDigger.Group grp)
                {
                    return grp.Name.Equals(serverClient.DefaultGroupGuests);
                }
            );
            if (this.defaultGroupGuest == null)
            {
                throw new Exception("Default guest group not found!");
            }
            this.defaultGroupRegistered = serverClient.Groups.Find(
                delegate(ManicDigger.Group grp)
                {
                    return grp.Name.Equals(serverClient.DefaultGroupRegistered);
                }
            );
            if (this.defaultGroupRegistered == null)
            {
                throw new Exception("Default registered group not found!");
            }
            Console.WriteLine("Server client configuration loaded.");
        }

        public void SaveServerClient()
        {
            //Verify that we have a directory to place the file into.
            if (!Directory.Exists(GameStorePath.gamepathconfig))
            {
                Directory.CreateDirectory(GameStorePath.gamepathconfig);
            }

            XmlSerializer serializer = new XmlSerializer(typeof(ServerClient));
            TextWriter textWriter = new StreamWriter(Path.Combine(GameStorePath.gamepathconfig, "ServerClient.txt"));

            //Check to see if config has been initialized
            if (serverClient == null)
            {
                serverClient = new ServerClient();
            }
            if (serverClient.Groups.Count == 0)
            {
                serverClient.Groups = ServerClientMisc.getDefaultGroups();
            }
            if (serverClient.Clients.Count == 0)
            {
                serverClient.Clients = ServerClientMisc.getDefaultClients();
            }
            serverClient.Clients.Sort();
            //Serialize the ServerConfig class to XML
            serializer.Serialize(textWriter, serverClient);
            textWriter.Close();
        }

        public int dumpmax = 30;
        public void DropItem(ref Item item, Vector3i pos)
        {
            switch (item.ItemClass)
            {
                case ItemClass.Block:
                    for (int i = 0; i < dumpmax; i++)
                    {
                        if (item.BlockCount == 0) { break; }
                        //find empty position that is nearest to dump place AND has a block under.
                        Vector3i? nearpos = FindDumpPlace(pos);
                        if (nearpos == null)
                        {
                            break;
                        }
                        SetBlockAndNotify(nearpos.Value.x, nearpos.Value.y, nearpos.Value.z, item.BlockId);
                        item.BlockCount--;
                    }
                    if (item.BlockCount == 0)
                    {
                        item = null;
                    }
                    break;
                default:
                    //todo
                    break;
            }
        }
        private Vector3i? FindDumpPlace(Vector3i pos)
        {
            List<Vector3i> l = new List<Vector3i>();
            for (int x = 0; x < 10; x++)
            {
                for (int y = 0; y < 10; y++)
                {
                    for (int z = 0; z < 10; z++)
                    {
                        int xx = pos.x + x - 10 / 2;
                        int yy = pos.y + y - 10 / 2;
                        int zz = pos.z + z - 10 / 2;
                        if (!MapUtil.IsValidPos(d_Map, xx, yy, zz))
                        {
                            continue;
                        }
                        if (d_Map.GetBlock(xx, yy, zz) == SpecialBlockId.Empty
                            && d_Map.GetBlock(xx, yy, zz - 1) != SpecialBlockId.Empty)
                        {
                            bool playernear = false;
                            foreach (var player in clients)
                            {
                                if (Length(Minus(PlayerBlockPosition(player.Value), new Vector3i(xx, yy, zz))) < 3)
                                {
                                    playernear = true;
                                }
                            }
                            if (!playernear)
                            {
                                l.Add(new Vector3i(xx, yy, zz));
                            }
                        }
                    }
                }
            }
            l.Sort((a, b) => Length(Minus(a, pos)).CompareTo(Length(Minus(b, pos))));
            if (l.Count > 0)
            {
                return l[0];
            }
            return null;
        }
        private Vector3i Minus(Vector3i a, Vector3i b)
        {
            return new Vector3i(a.x - b.x, a.y - b.y, a.z - b.z);
        }
        int Length(Vector3i v)
        {
            return (int)Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
        }

        public void SetBlockType(int id, string name, BlockType block)
        {
            BlockTypes[id] = block;
            block.Name = name;
            d_Data.UseBlockType(id, block, null);
        }
        public void SetBlockType(string name, BlockType block)
        {
            for (int i = 0; i < BlockTypes.Length; i++)
            {
                if (BlockTypes[i].Name == null)
                {
                    SetBlockType(i, name, block);
                    return;
                }
            }
        }

        int[] sunlevels;
        public void SetSunLevels(int[] sunLevels)
        {
            this.sunlevels = sunLevels;
        }
        float[] lightlevels;
        public void SetLightLevels(float[] lightLevels)
        {
            this.lightlevels = lightLevels;
        }
        public List<CraftingRecipe> craftingrecipes = new List<CraftingRecipe>();

        public bool IsSinglePlayer
        {
            get { return d_MainSocket.GetType() == typeof(DummyNetServer); }
        }

        public void SendDialog(int player, string id, Dialog dialog)
        {
            PacketServerDialog p = new PacketServerDialog()
            {
                DialogId = id,
                Dialog = dialog,
            };
            SendPacket(player, Serialize(new PacketServer() { PacketId = ServerPacketId.Dialog, Dialog = p }));
        }

        public bool PlayerHasPrivilege(int player, string privilege)
        {
            if (extraPrivileges.ContainsKey(privilege))
            {
                return true;
            }
            if (disabledprivileges.ContainsKey(privilege))
            {
                return false;
            }
            return GetClient(player).privileges.Contains(privilege);
        }

        public void PlaySoundAt(int posx, int posy, int posz, string sound)
        {
            PlaySoundAtExceptPlayer(posx, posy, posz, sound, null);
        }
        
        public void PlaySoundAtExceptPlayer(int posx, int posy, int posz, string sound, int? player)
        {
            Vector3i pos = new Vector3i(posx, posy, posz);
            foreach (var k in clients)
            {
                if (player != null && player == k.Key)
                {
                    continue;
                }
                int distance = DistanceSquared(new Vector3i((int)k.Value.PositionMul32GlX / 32, (int)k.Value.PositionMul32GlZ / 32, (int)k.Value.PositionMul32GlY / 32), pos);
                if (distance < 64 * 64)
                {
                    SendSound(k.Key, sound, pos.x, posy, posz);
                }
            }
        }

        public void PlaySoundAt(int posx, int posy, int posz, string sound, int range)
        {
            PlaySoundAtExceptPlayer(posx, posy, posz, sound, null, range);
        }
        public void PlaySoundAtExceptPlayer (int posx, int posy, int posz, string sound, int? player, int range)
        {
            Vector3i pos = new Vector3i (posx, posy, posz);
            foreach (var k in clients)
            {
                if (player != null && player == k.Key)
                {
                    continue;
                }
                int distance = DistanceSquared (new Vector3i ((int)k.Value.PositionMul32GlX / 32, (int)k.Value.PositionMul32GlZ / 32, (int)k.Value.PositionMul32GlY / 32), pos);
                if (distance < range)
                {
                    SendSound (k.Key, sound, pos.x, posy, posz);
                }
            }
        }

        public void SendPacketFollow(int player, int target, bool tpp)
        {
            PacketServerFollow p = new PacketServerFollow()
            {
                Client = target == -1 ? null : clients[target].playername,
                Tpp = tpp,
            };
            SendPacket(player, Serialize(new PacketServer() { PacketId = ServerPacketId.Follow, Follow = p }));
        }

        public void SendAmmo(int playerid, Dictionary<int, int> totalAmmo)
        {
            PacketServerAmmo p = new PacketServerAmmo()
            {
                TotalAmmo = totalAmmo,
            };
            SendPacket(playerid, Serialize(new PacketServer() { PacketId = ServerPacketId.Ammo, Ammo = p }));
        }

        public void SendExplosion(int player, float x, float y, float z, bool relativeposition, float range, float time)
        {
            PacketServerExplosion p = new PacketServerExplosion();
            p.X = x;
            p.Y = y;
            p.Z = z;
            p.IsRelativeToPlayerPosition = relativeposition;
            p.Range = range;
            p.Time = time;
            SendPacket(player, Serialize(new PacketServer() { PacketId = ServerPacketId.Explosion, Explosion = p }));
        }

        public string GetGroupColor(int playerid)
        {
            return GetClient(playerid).clientGroup.GroupColorString();
        }

        public string GetGroupName(int playerid)
        {
            return GetClient(playerid).clientGroup.Name;
        }

        public class ActiveHttpModule
        {
            public string name;
            public ManicDigger.Func<string> description;
            public FragLabs.HTTP.IHttpModule module;
        }

        List<ActiveHttpModule> httpModules = new List<ActiveHttpModule>();

        internal void InstallHttpModule(string name, ManicDigger.Func<string> description, FragLabs.HTTP.IHttpModule module)
        {
            ActiveHttpModule m = new ActiveHttpModule();
            m.name = name;
            m.description = description;
            m.module = module;
            httpModules.Add(m);
            if (httpServer != null)
            {
                httpServer.Install(module);
            }
        }

        public ModEventHandlers modEventHandlers = new ModEventHandlers();
    }

    public class ModEventHandlers
    {
        public List<ModDelegates.WorldGenerator> getchunk = new List<ModDelegates.WorldGenerator>();
        public List<ModDelegates.BlockUse> onuse = new List<ModDelegates.BlockUse>();
        public List<ModDelegates.BlockBuild> onbuild = new List<ModDelegates.BlockBuild>();
        public List<ModDelegates.BlockDelete> ondelete = new List<ModDelegates.BlockDelete>();
        public List<ModDelegates.BlockUseWithTool> onusewithtool = new List<ModDelegates.BlockUseWithTool>();
        public List<ModDelegates.ChangedActiveMaterialSlot> changedactivematerialslot = new List<ModDelegates.ChangedActiveMaterialSlot>();
        public List<ModDelegates.BlockUpdate> blockticks = new List<ModDelegates.BlockUpdate>();
        public List<ModDelegates.PopulateChunk> populatechunk = new List<ModDelegates.PopulateChunk>();
        public List<ModDelegates.Command> oncommand = new List<ModDelegates.Command>();
        public List<ModDelegates.WeaponShot> onweaponshot = new List<ModDelegates.WeaponShot>();
        public List<ModDelegates.WeaponHit> onweaponhit = new List<ModDelegates.WeaponHit>();
        public List<ModDelegates.SpecialKey1> onspecialkey = new List<ModDelegates.SpecialKey1>();
        public List<ModDelegates.PlayerJoin> onplayerjoin = new List<ModDelegates.PlayerJoin>();
        public List<ModDelegates.PlayerLeave> onplayerleave = new List<ModDelegates.PlayerLeave>();
        public List<ModDelegates.PlayerDisconnect> onplayerdisconnect = new List<ModDelegates.PlayerDisconnect>();
        public List<ModDelegates.PlayerChat> onplayerchat = new List<ModDelegates.PlayerChat>();
        public List<ModDelegates.DialogClick> ondialogclick = new List<ModDelegates.DialogClick>();
    }

    public class Ping
    {
        public TimeSpan RoundtripTime { get; set; }

        private bool ready;
        private DateTime timeSend;
        private int timeout = 10; //in seconds
        public int TimeoutValue
        {
            get { return timeout; }
            set { timeout = value; }
        }

        public Ping()
        {
            this.RoundtripTime = TimeSpan.MinValue;
            this.ready = true;
            this.timeSend = DateTime.MinValue;
        }

        public bool Send()
        {
            if (!ready)
            {
                return false;
            }
            ready = false;
            this.timeSend = DateTime.UtcNow;
            return true;
        }

        public bool Receive()
        {
            if (ready)
            {
                return false;
            }
            this.RoundtripTime = DateTime.UtcNow.Subtract(this.timeSend);
            ready = true;
            return true;
        }

        public bool Timeout()
        {
            if (DateTime.UtcNow.Subtract(this.timeSend).TotalSeconds > this.timeout)
            {
                this.ready = true;
                return true;
            }
            return false;
        }
    }
    public class GameTime
    {
            public long Ticks;
            public int TicksPerSecond = 64;
            public double GameYearRealHours = 24;
            public double GameDayRealHours = 1;
            public double HourTotal // 0 - 23
            {
                get
                {
                    return ((double)Ticks / TicksPerSecond) / (GameDayRealHours * 60 * 60);
                }
            }
            public double YearTotal
            {
                get
                {
                    return ((double)Ticks / TicksPerSecond) / (GameYearRealHours * 60 * 60);
                }
            }
    }
}
