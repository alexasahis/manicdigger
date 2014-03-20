﻿using System;
using System.Collections.Generic;
using System.Text;
using ManicDigger;
using ManicDigger.Renderers;
using OpenTK;
using System.Drawing;
using ManicDigger.Network;
using OpenTK.Graphics.OpenGL;
using System.IO;
using System.Windows.Forms;
using System.Threading;
using GameModeFortress;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using ProtoBuf;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using ManicDigger.ClientNative;

namespace ManicDigger
{
    //This is the main game class.
    public partial class ManicDiggerGameWindow : IMyGameWindow,
        IClients,
        IMapStorage,
        ICurrentShadows, IResetMap
    {
        public ManicDiggerGameWindow()
        {
            one = 1;
            game = new Game();
            game.language.platform = new GamePlatformNative();
            game.language.LoadTranslations();
            mvMatrix.Push(Mat4.Create());
            pMatrix.Push(Mat4.Create());
            PerformanceInfo = new DictionaryStringString();
            AudioEnabled = true;
            OverheadCamera_cameraEye = new Vector3Ref();
            players = new Player[2048];
            playersCount = 2048;
        }
        float one;
        public void Start()
        {
            game.platform = new GamePlatformNative() { window = d_GlWindow };
            string[] datapaths = new[] { Path.Combine(Path.Combine(Path.Combine("..", ".."), ".."), "data"), "data" };
            var getfile = new GetFileStream(datapaths);
            var w = this;
            var gamedata = new GameData();
            gamedata.Start();
            var clientgame = w;
            var network = w;
            var mapstorage = clientgame;
            var config3d = new Config3d();
            var the3d = new The3d();
            the3d.game = this;
            the3d.d_GetFile = getfile;
            the3d.d_Config3d = config3d;
            w.d_The3d = the3d;
            var localplayerposition = w;
            var physics = new CharacterPhysicsCi();
            var internetgamefactory = this;
            ICompression compression = new CompressionGzip(); //IsSinglePlayer ? (ICompression)new CompressionGzip() : new CompressionGzip();
            network.d_Compression = compression;
            //network.d_ResetMap = this;
            var terrainTextures = new ITerrainTextures();
            terrainTextures.game = game;
            bool IsMono = Type.GetType("Mono.Runtime") != null;
            d_TextureAtlasConverter = new TextureAtlasConverter();
            if (IsMono)
            {
                d_TextureAtlasConverter.d_FastBitmapFactory = () => { return new FastBitmapDummy(); };
            }
            else
            {
                d_TextureAtlasConverter.d_FastBitmapFactory = () => { return new FastBitmap(); };
            }
            w.game.d_TerrainTextures = terrainTextures;
            var blockrenderertorch = new BlockRendererTorch();
            blockrenderertorch.d_TerainRenderer = terrainTextures;
            blockrenderertorch.d_Data = gamedata;
            //InfiniteMapChunked map = new InfiniteMapChunked();// { generator = new WorldGeneratorDummy() };
            var map = w;
            var terrainchunktesselator = new TerrainChunkTesselatorCi();
            w.d_TerrainChunkTesselator = terrainchunktesselator;
            var frustumculling = new FrustumCulling() { d_GetCameraMatrix = game.CameraMatrix, platform = game.platform };
            w.d_Batcher = new MeshBatcher() { d_FrustumCulling = frustumculling, game = game };
            w.d_FrustumCulling = frustumculling;
            w.BeforeRenderFrame += (a, b) => { frustumculling.CalcFrustumEquations(); };
            //w.d_Map = clientgame.mapforphysics;
            w.d_Physics = physics;
            w.d_Clients = clientgame;
            w.d_Data = gamedata;
            w.d_DataMonsters = new GameDataMonsters();
            w.d_GetFile = getfile;
            w.d_Config3d = config3d;
            w.PICK_DISTANCE = 4.5f;
            var skysphere = new SkySphere();
            skysphere.game = game;
            skysphere.d_MeshBatcher = new MeshBatcher() { d_FrustumCulling = new FrustumCullingDummy(), game = game };
            w.skysphere = skysphere;
            Packet_Inventory inventory = new Packet_Inventory();
            w.d_Weapon = new WeaponRenderer() { d_BlockRendererTorch = blockrenderertorch, game = game };
            var playerrenderer = new CharacterRendererMonsterCode();
            playerrenderer.game = this.game;
            string[] playerTxtLines = MyStream.ReadAllLines(getfile.GetFile("player.txt"));
            playerrenderer.Load(playerTxtLines, playerTxtLines.Length);
            w.d_CharacterRenderer = playerrenderer;
            var particle = new ParticleEffectBlockBreak();
            w.particleEffectBlockBreak = particle;
            w.ENABLE_FINITEINVENTORY = false;
            w.d_Shadows = w;
            clientgame.d_Data = gamedata;
            clientgame.d_CraftingTableTool = new CraftingTableTool() { d_Map = mapstorage, d_Data = gamedata };
            clientgame.d_RailMapUtil = new RailMapUtil() { game = game };
            clientgame.d_MinecartRenderer = new MinecartRenderer() { game = game };
            clientgame.game.d_TerrainTextures = terrainTextures;
            clientgame.d_GetFile = getfile;
            w.Reset(10 * 1000, 10 * 1000, 128);
            clientgame.d_Map = game;
            PlayerSkinDownloader playerskindownloader = new PlayerSkinDownloader();
            playerskindownloader.d_Exit = d_Exit;
            playerskindownloader.d_The3d = the3d;
            try
            {
                if (playerskindownloader.skinserver == null)
                {
                    WebClient c = new WebClient();
                    playerskindownloader.skinserver = c.DownloadString("http://manicdigger.sourceforge.net/skinserver.txt");
                }
            }
            catch
            {
                playerskindownloader.skinserver = "";
            }
            w.playerskindownloader = playerskindownloader;
            w.d_FrustumCulling = frustumculling;
            //w.d_CurrentShadows = this;
            var sunmoonrenderer = new SunMoonRenderer() { game = game };
            w.d_SunMoonRenderer = sunmoonrenderer;
            clientgame.d_SunMoonRenderer = sunmoonrenderer;
            this.d_Heightmap = new InfiniteMapChunked2d() { d_Map = game };
            d_Heightmap.Restart();
            network.d_Heightmap = d_Heightmap;
            //this.light = new InfiniteMapChunkedSimple() { d_Map = map };
            //light.Restart();
            w.d_TerrainChunkTesselator = terrainchunktesselator;
            terrainchunktesselator.game = game;
            /*
            if (fullshadows)
            {
                UseShadowsFull();
            }
            else
            {
                UseShadowsSimple();
            }
            */
            terrainRenderer = new TerrainRenderer();
            terrainRenderer.game = game;
            w.d_HudChat = new HudChat() { game = this.game };
            var dataItems = new GameDataItemsClient() { game = game };
            var inventoryController = ClientInventoryController.Create(game);
            var inventoryUtil = new InventoryUtilClient();
            var hudInventory = new HudInventory();
            hudInventory.game = game;
            hudInventory.dataItems = dataItems;
            hudInventory.inventoryUtil = inventoryUtil;
            hudInventory.controller = inventoryController;
            w.d_Inventory = inventory;
            w.d_InventoryController = inventoryController;
            w.d_InventoryUtil = inventoryUtil;
            inventoryUtil.d_Inventory = inventory;
            inventoryUtil.d_Items = dataItems;

            d_Physics.game = game;
            clientgame.d_Inventory = inventory;
            w.d_HudInventory = hudInventory;
            w.d_CurrentShadows = this;
            w.d_ResetMap = this;
            crashreporter.OnCrash += new EventHandler(crashreporter_OnCrash);

            clientmods = new ClientMod[128];
            clientmodsCount = 0;
            modmanager.game = game;
            AddMod(new ModAutoCamera());
            AddMod(new ModFpsHistoryGraph());
            applicationname = language.GameName();
            s = new BlockOctreeSearcher();
            s.platform = game.platform;
            escapeMenu.game = this;
        }
        void AddMod(ClientMod mod)
        {
            clientmods[clientmodsCount++] = mod;
            mod.Start(modmanager);
        }

        internal Game game;

        void crashreporter_OnCrash(object sender, EventArgs e)
        {
            try
            {
                SendLeave(Packet_LeaveReasonEnum.Crash);
            }
            catch
            {
            }
        }

        public GlWindow d_GlWindow;
        public The3d d_The3d;
        public Game d_Map;
        public IClients d_Clients;

        public GetFileStream d_GetFile;
        public ICharacterRenderer d_CharacterRenderer;
        public ICurrentShadows d_CurrentShadows;
        public SunMoonRenderer d_SunMoonRenderer;
        public IGameExit d_Exit;
        public IInventoryController d_InventoryController;
        public InventoryUtilClient d_InventoryUtil;
        public CraftingTableTool d_CraftingTableTool;
        public IResetMap d_ResetMap;
        public ICompression d_Compression;
        public ManicDiggerGameWindow d_Shadows;
        public Packet_CraftingRecipe[] d_CraftingRecipes;

        public bool IsMono = Type.GetType("Mono.Runtime") != null;
        public bool IsMac = Environment.OSVersion.Platform == PlatformID.MacOSX;

        public int LoadTexture(string filename)
        {
            d_The3d.d_Config3d = d_Config3d;
            return LoadTexture(d_GetFile.GetFile(filename));
        }
        public int LoadTexture(Bitmap bmp)
        {
            d_The3d.d_Config3d = d_Config3d;
            return d_The3d.LoadTexture(bmp);
        }
        public GuiStateEscapeMenu escapeMenu = new GuiStateEscapeMenu();
        public void OnFocusedChanged(EventArgs e)
        {
            if (guistate == GuiState.Normal)
            { escapeMenu.EscapeMenuStart(); }
            else if (guistate == GuiState.EscapeMenu)
            { }
            else if (guistate == GuiState.Inventory)
            { }
            else if (guistate == GuiState.MapLoading)
            { }
            else if (guistate == GuiState.CraftingRecipes)
            { }
            else { throw new Exception(); }
            //..base.OnFocusedChanged(e);
        }
        public List<DisplayResolution> resolutions;
        public void OnLoad(EventArgs e)
        {
            if (resolutions == null)
            {
                resolutions = new List<DisplayResolution>();
                foreach (var r in DisplayDevice.Default.AvailableResolutions)
                {
                    if (r.Width < 800 || r.Height < 600 || r.BitsPerPixel < 16)
                    {
                        continue;
                    }
                    resolutions.Add(r);
                }
            }
            try
            {
                GL.GetInteger(GetPName.MaxTextureSize, out maxTextureSize);
            }
            catch
            {
                maxTextureSize = 1024;
            }
            if (maxTextureSize < 1024)
            {
                maxTextureSize = 1024;
            }
            //Start();
            //Connect();
            MapLoadingStart();

            string version = GL.GetString(StringName.Version);
            int major = (int)version[0];
            int minor = (int)version[2];
            if (major <= 1 && minor < 5)
            {
                System.Windows.Forms.MessageBox.Show("You need at least OpenGL 1.5 to run this example. Aborting.", "VBOs not supported",
                System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Exclamation);
                d_GlWindow.Exit();
            }
            if (!d_Config3d.ENABLE_VSYNC)
            {
                d_GlWindow.TargetRenderFrequency = 0;
            }
            GL.ClearColor(Color.Black);
            d_GlWindow.Mouse.ButtonDown += new EventHandler<OpenTK.Input.MouseButtonEventArgs>(Mouse_ButtonDown);
            d_GlWindow.Mouse.ButtonUp += new EventHandler<OpenTK.Input.MouseButtonEventArgs>(Mouse_ButtonUp);
            d_GlWindow.Mouse.WheelChanged += new EventHandler<OpenTK.Input.MouseWheelEventArgs>(Mouse_WheelChanged);
            if (d_Config3d.ENABLE_BACKFACECULLING)
            {
                GL.DepthMask(true);
                GL.Enable(EnableCap.DepthTest);
                GL.CullFace(CullFaceMode.Back);
                GL.Enable(EnableCap.CullFace);
            }
            d_GlWindow.Keyboard.KeyRepeat = true;
            d_GlWindow.KeyPress += new EventHandler<OpenTK.KeyPressEventArgs>(ManicDiggerGameWindow_KeyPress);
            d_GlWindow.Keyboard.KeyDown += new EventHandler<OpenTK.Input.KeyboardKeyEventArgs>(Keyboard_KeyDown);
            d_GlWindow.Keyboard.KeyUp += new EventHandler<OpenTK.Input.KeyboardKeyEventArgs>(Keyboard_KeyUp);

            GL.Enable(EnableCap.Lighting);
            //SetAmbientLight(terraincolor);
            GL.Enable(EnableCap.ColorMaterial);
            GL.ColorMaterial(MaterialFace.FrontAndBack, ColorMaterialParameter.AmbientAndDiffuse);
            GL.ShadeModel(ShadingModel.Smooth);
            if (!IsMac)
            {
                System.Windows.Forms.Cursor.Hide();
            }
            else
            {
                d_GlWindow.CursorVisible = false;
            }
        }
        void Mouse_ButtonUp(object sender, OpenTK.Input.MouseButtonEventArgs e)
        {
            if (!d_GlWindow.Focused)
            {
                return;
            }
            if (e.Button == OpenTK.Input.MouseButton.Left)
            {
                mouseleftdeclick = true;
            }
            if (e.Button == OpenTK.Input.MouseButton.Right)
            {
                mouserightdeclick = true;
            }
            if (guistate == GuiState.Normal)
            {
                UpdatePicking();
            }
            if (guistate == GuiState.Inventory)
            {
                d_HudInventory.Mouse_ButtonUp(GetMouseEventArgs(e));
            }
        }
        void Mouse_ButtonDown(object sender, OpenTK.Input.MouseButtonEventArgs e)
        {
            if (!d_GlWindow.Focused)
            {
                return;
            }
            if (e.Button == OpenTK.Input.MouseButton.Left)
            {
                mouseleftclick = true;
            }
            if (e.Button == OpenTK.Input.MouseButton.Right)
            {
                mouserightclick = true;
            }
            if (guistate == GuiState.Normal)
            {
                UpdatePicking();
            }
            if (guistate == GuiState.Inventory)
            {
                d_HudInventory.Mouse_ButtonDown(GetMouseEventArgs(e));
            }
        }

        MouseEventArgs GetMouseEventArgs(OpenTK.Input.MouseButtonEventArgs e)
        {
            MouseEventArgs args = new MouseEventArgs();
            args.SetX(e.X);
            args.SetY(e.Y);
            int button = 0;
            if (e.Button == OpenTK.Input.MouseButton.Left)
            {
                button = MouseButtonEnum.Left;
            }
            if (e.Button == OpenTK.Input.MouseButton.Middle)
            {
                button = MouseButtonEnum.Middle;
            }
            if (e.Button == OpenTK.Input.MouseButton.Right)
            {
                button = MouseButtonEnum.Right;
            }
            args.SetButton(button);
            return args;
        }

        void ManicDiggerGameWindow_KeyPress(object sender, OpenTK.KeyPressEventArgs e)
        {
            if (KeyIsEqualChar(OpenTK.Input.Key.T, e.KeyChar) && GuiTyping == TypingState.None)
            {
                GuiTyping = TypingState.Typing;
                d_HudChat.GuiTypingBuffer = "";
                d_HudChat.IsTeamchat = false;
                return;
            }
            if (KeyIsEqualChar(OpenTK.Input.Key.Y, e.KeyChar) && GuiTyping == TypingState.None)
            {
                GuiTyping = TypingState.Typing;
                d_HudChat.GuiTypingBuffer = "";
                d_HudChat.IsTeamchat = true;
                return;
            }
            if (GuiTyping == TypingState.Typing)
            {
                char c = e.KeyChar;
                if ((char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)
                    || char.IsPunctuation(c) || char.IsSeparator(c) || char.IsSymbol(c))
                    && c != '\r' && c != '\t')
                {
                    d_HudChat.GuiTypingBuffer += e.KeyChar;
                }
                //Handles player name autocomplete in chat
                if (c == '\t' && d_HudChat.GuiTypingBuffer.Trim() != "")
                {
                    for (int i = 0; i < playersCount; i++)
                    {
                        if (players[i] == null)
                        {
                            continue;
                        }
                        Player p = players[i];
                        if (p.Type != PlayerType.Player)
                        {
                            continue;
                        }
                        //Use substring here because player names are internally in format &xNAME (so we need to cut first 2 characters)
                        if (p.Name.Substring(2).StartsWith(d_HudChat.GuiTypingBuffer, StringComparison.InvariantCultureIgnoreCase))
                        {
                            d_HudChat.GuiTypingBuffer = p.Name.Substring(2) + ": ";
                            break;
                        }
                    }
                }
            }
            for (int k = 0; k < dialogsCount; k++)
            {
                if (dialogs[k] == null)
                {
                    continue;
                }
                VisibleDialog d = dialogs[k];
                for (int i = 0; i < d.value.WidgetsCount; i++)
                {
                    Packet_Widget w = d.value.Widgets[i];
                    if (w == null)
                    {
                        continue;
                    }
                    if (("abcdefghijklmnopqrstuvwxyz1234567890\t " + (char)27).Contains("" + (char)w.ClickKey))
                    {
                        if (e.KeyChar == w.ClickKey)
                        {
                            Packet_Client p = new Packet_Client();
                            p.Id = Packet_ClientIdEnum.DialogClick;
                            p.DialogClick_ = new Packet_ClientDialogClick();
                            p.DialogClick_.WidgetId = w.Id;
                            SendPacketClient(p);
                            return;
                        }
                    }
                }
            }
        }
        float overheadcameradistance = 10;
        float tppcameradistance = 3;
        void Mouse_WheelChanged(object sender, OpenTK.Input.MouseWheelEventArgs e)
        {
            if (keyboardstate[GetKey(OpenTK.Input.Key.LControl)])
            {
                if (cameratype == CameraType.Overhead)
                {
                    overheadcameradistance -= e.DeltaPrecise;
                    if (overheadcameradistance < TPP_CAMERA_DISTANCE_MIN) { overheadcameradistance = TPP_CAMERA_DISTANCE_MIN; }
                    if (overheadcameradistance > TPP_CAMERA_DISTANCE_MAX) { overheadcameradistance = TPP_CAMERA_DISTANCE_MAX; }
                }
                if (cameratype == CameraType.Tpp)
                {
                    tppcameradistance -= e.DeltaPrecise;
                    if (tppcameradistance < TPP_CAMERA_DISTANCE_MIN) { tppcameradistance = TPP_CAMERA_DISTANCE_MIN; }
                    if (tppcameradistance > TPP_CAMERA_DISTANCE_MAX) { tppcameradistance = TPP_CAMERA_DISTANCE_MAX; }
                }
            }
        }
        public int TPP_CAMERA_DISTANCE_MIN = 1;
        public int TPP_CAMERA_DISTANCE_MAX = 10;
        private static void SetAmbientLight(Color c)
        {
            float mult = 1f;
            float[] global_ambient = new float[] { (float)c.R / 255f * mult, (float)c.G / 255f * mult, (float)c.B / 255f * mult, 1f };
            GL.LightModel(LightModelParameter.LightModelAmbient, global_ambient);
        }
        public void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            SendLeave(Packet_LeaveReasonEnum.Leave);
        }
        public void OnClosed(EventArgs e)
        {
            d_Exit.exit = true;
            //..base.OnClosed(e);
        }
        void ClientCommand(string s)
        {
            if (s == "")
            {
                return;
            }
            string[] ss = s.Split(new char[] { ' ' });
            if (s.StartsWith("."))
            {
                string strFreemoveNotAllowed = game.language.FreemoveNotAllowed();
                try
                {
                    string cmd = ss[0].Substring(1);
                    string arguments;
                    if (s.IndexOf(" ") == -1)
                    { arguments = ""; }
                    else
                    { arguments = s.Substring(s.IndexOf(" ")); }
                    arguments = arguments.Trim();
                    if (cmd == "pos")
                    {
                        ENABLE_DRAWPOSITION = BoolCommandArgument(arguments);
                    }
                    else if (cmd == "fog")
                    {
                        int foglevel;
                        foglevel = int.Parse(arguments);
                        //if (foglevel <= 16)
                        //{
                        //    terrain.DrawDistance = (int)Math.Pow(2, foglevel);
                        //}
                        //else
                        {
                            int foglevel2 = foglevel;
                            if (foglevel2 > 1024)
                            {
                                foglevel2 = 1024;
                            }
                            if (foglevel2 % 2 == 0)
                            {
                                foglevel2--;
                            }
                            d_Config3d.viewdistance = foglevel2;
                            //terrain.UpdateAllTiles();
                        }
                        OnResize(new EventArgs());
                    }
                    else if (cmd == "noclip")
                    {
                        ENABLE_NOCLIP = BoolCommandArgument(arguments);
                    }
                    else if (cmd == "freemove")
                    {
                        if (this.AllowFreemove)
                        {
                            ENABLE_FREEMOVE = BoolCommandArgument(arguments);
                        }
                        else
                        {
                            Log(strFreemoveNotAllowed);
                            return;
                        }
                    }
                    else if (cmd == "fov")
                    {
                        int arg = int.Parse(arguments);
                        int minfov = 1;
                        int maxfov = 179;
                        if (!issingleplayer)
                        {
                            minfov = 60;
                        }
                        if (arg < minfov || arg > maxfov)
                        {
                            throw new Exception(string.Format("Valid field of view: {0}-{1}", minfov, maxfov));
                        }
                        float fov = (float)(2 * Math.PI * ((float)arg / 360));
                        this.fov = fov;
                        OnResize(new EventArgs());
                    }
                    else if (cmd == "clients")
                    {
                        Log("Clients:");
                        for (int i = 0; i < playersCount; i++)
                        {
                            if (players[i] == null)
                            {
                                continue;
                            }
                            Log(string.Format("{0} {1}", i, players[i].Name));
                        }
                    }
                    else if (cmd == "movespeed")
                    {
                        try
                        {
                            if (this.AllowFreemove)
                            {
                                if (float.Parse(arguments) <= 500)
                                {
                                    movespeed = basemovespeed * float.Parse(arguments);
                                    AddChatline("Movespeed: " + arguments + "x");
                                }
                                else
                                {
                                    AddChatline("Entered movespeed to high! max. 500x");
                                }
                            }
                            else
                            {
                                Log(strFreemoveNotAllowed);
                                return;
                            }
                        }
                        catch
                        {
                            AddChatline("Invalid value!");
                            AddChatline("USE: .movespeed [movespeed]");
                        }
                    }
                    else if (cmd == "testmodel")
                    {
                        ENABLE_DRAW_TEST_CHARACTER = BoolCommandArgument(arguments);
                    }
                    else if (cmd == "gui")
                    {
                        ENABLE_DRAW2D = BoolCommandArgument(arguments);
                    }
                    else if (cmd == "reconnect")
                    {
                        Reconnect();
                    }
                    else
                    {
                        for (int i = 0; i < clientmodsCount; i++)
                        {
                            ClientCommandArgs args = new ClientCommandArgs();
                            args.arguments = arguments;
                            args.command = cmd;
                            clientmods[i].OnClientCommand(args);
                        }
                        string chatline = d_HudChat.GuiTypingBuffer.Substring(0, Math.Min(d_HudChat.GuiTypingBuffer.Length, 256));
                        SendChat(chatline);
                    }
                }
                catch (Exception e) { AddChatline(new StringReader(e.Message).ReadLine()); }
            }
            else
            {
                string chatline = d_HudChat.GuiTypingBuffer.Substring(0, Math.Min(d_HudChat.GuiTypingBuffer.Length, 4096));
                SendChat(chatline);

            }
        }

        void Reconnect()
        {
            reconnect = true;
            d_GlWindow.Exit();
        }

        private static bool BoolCommandArgument(string arguments)
        {
            arguments = arguments.Trim();
            return (arguments == "" || arguments == "1" || arguments == "on" || arguments == "yes");
        }

        OpenTK.Input.KeyboardKeyEventArgs keyevent;
        OpenTK.Input.KeyboardKeyEventArgs keyeventup;
        void Keyboard_KeyUp(object sender, OpenTK.Input.KeyboardKeyEventArgs e)
        {
            for (int i = 0; i < clientmodsCount; i++)
            {
                KeyEventArgs args_ = new KeyEventArgs();
                args_.SetKeyCode(GamePlatformNative.ToGlKey(e.Key));
                clientmods[i].OnKeyUp(args_);
            }
            if (e.Key == GetKey(OpenTK.Input.Key.ShiftLeft) || e.Key == GetKey(OpenTK.Input.Key.ShiftRight))
                IsShiftPressed = false;
            if (GuiTyping == TypingState.None)
            {
                keyeventup = e;
            }
        }
        
        void Keyboard_KeyDown(object sender, OpenTK.Input.KeyboardKeyEventArgs e)
        {
            for (int i = 0; i < clientmodsCount; i++)
            {
                KeyEventArgs args_ = new KeyEventArgs();
                args_.SetKeyCode(GamePlatformNative.ToGlKey(e.Key));
                clientmods[i].OnKeyDown(args_);
            }
            if (e.Key == GetKey(OpenTK.Input.Key.F6))
            {
                float lagSeconds = one * (game.platform.TimeMillisecondsFromStart() - LastReceivedMilliseconds) / 1000;
                if ((lagSeconds >= DISCONNECTED_ICON_AFTER_SECONDS) || guistate == GuiState.MapLoading)
                {
                    Reconnect();
                }
            }
            if (e.Key == GetKey(OpenTK.Input.Key.ShiftLeft) || e.Key == GetKey(OpenTK.Input.Key.ShiftRight))
                IsShiftPressed = true;
            if (e.Key == GetKey(OpenTK.Input.Key.F11))
            {
                if (d_GlWindow.WindowState == WindowState.Fullscreen)
                {
                    d_GlWindow.WindowState = WindowState.Normal;
                    escapeMenu.RestoreResolution();
                    escapeMenu.SaveOptions();
                }
                else
                {
                    d_GlWindow.WindowState = WindowState.Fullscreen;
                    escapeMenu.UseResolution();
                    escapeMenu.SaveOptions();
                }
            }
            if (GuiTyping == TypingState.None)
            {
                keyevent = e;
            }
            if (guistate == GuiState.Normal)
            {
                if (Keyboard[GetKey(OpenTK.Input.Key.Escape)])
                {
                    for (int i = 0; i < dialogsCount; i++)
                    {
                        if (dialogs[i] == null)
                        {
                            continue;
                        }
                        VisibleDialog d = dialogs[i];
                        if (d.value.IsModal != 0)
                        {
                            dialogs[i] = null;
                            return;
                        }
                    }
                    guistate = GuiState.EscapeMenu;
                    menustate = new MenuState();
                    FreeMouse = true;
                }
                if (e.Key == GetKey(OpenTK.Input.Key.Number7) && IsShiftPressed && GuiTyping == TypingState.None) // don't need to hit enter for typing commands starting with slash
                {
                    GuiTyping = TypingState.Typing;
                    d_HudChat.IsTyping = true;
                    d_HudChat.GuiTypingBuffer = "";
                    d_HudChat.IsTeamchat = false;
                    return;
                }
                if (e.Key == GetKey(OpenTK.Input.Key.PageUp) && GuiTyping == TypingState.Typing)
                {
                    d_HudChat.ChatPageScroll++;
                }
                if (e.Key == GetKey(OpenTK.Input.Key.PageDown) && GuiTyping == TypingState.Typing)
                {
                    d_HudChat.ChatPageScroll--;
                }
                d_HudChat.ChatPageScroll = Game.ClampInt(d_HudChat.ChatPageScroll, 0, d_HudChat.ChatLinesCount / d_HudChat.ChatLinesMaxToDraw);
                if (e.Key == GetKey(OpenTK.Input.Key.Enter) || e.Key == GetKey(OpenTK.Input.Key.KeypadEnter))
                {
                    if (GuiTyping == TypingState.Typing)
                    {
                        typinglog.Add(d_HudChat.GuiTypingBuffer);
                        typinglogpos = typinglog.Count;
                        ClientCommand(d_HudChat.GuiTypingBuffer);

                        d_HudChat.GuiTypingBuffer = "";
                        d_HudChat.IsTyping = false;

                        GuiTyping = TypingState.None;
                    }
                    else if (GuiTyping == TypingState.None)
                    {
                        GuiTyping = TypingState.Typing;
                        d_HudChat.IsTyping = true;
                        d_HudChat.GuiTypingBuffer = "";
                        d_HudChat.IsTeamchat = false;
                    }
                    else if (GuiTyping == TypingState.Ready)
                    {
                        Console.WriteLine("Keyboard_KeyDown ready");
                    }
                    return;
                }
                if (GuiTyping == TypingState.Typing)
                {
                    var key = e.Key;
                    if (key == GetKey(OpenTK.Input.Key.BackSpace))
                    {
                        if (d_HudChat.GuiTypingBuffer.Length > 0)
                        {
                            d_HudChat.GuiTypingBuffer = d_HudChat.GuiTypingBuffer.Substring(0, d_HudChat.GuiTypingBuffer.Length - 1);
                        }
                        return;
                    }
                    if (Keyboard[GetKey(OpenTK.Input.Key.ControlLeft)] || Keyboard[GetKey(OpenTK.Input.Key.ControlRight)])
                    {
                        if (key == GetKey(OpenTK.Input.Key.V))
                        {
                            if (Clipboard.ContainsText())
                            {
                                d_HudChat.GuiTypingBuffer += Clipboard.GetText();
                            }
                            return;
                        }
                    }
                    if (key == GetKey(OpenTK.Input.Key.Up))
                    {
                        typinglogpos--;
                        if (typinglogpos < 0) { typinglogpos = 0; }
                        if (typinglogpos >= 0 && typinglogpos < typinglog.Count)
                        {
                            d_HudChat.GuiTypingBuffer = typinglog[typinglogpos];
                        }
                    }
                    if (key == GetKey(OpenTK.Input.Key.Down))
                    {
                        typinglogpos++;
                        if (typinglogpos > typinglog.Count) { typinglogpos = typinglog.Count; }
                        if (typinglogpos >= 0 && typinglogpos < typinglog.Count)
                        {
                            d_HudChat.GuiTypingBuffer = typinglog[typinglogpos];
                        }
                        if (typinglogpos == typinglog.Count)
                        {
                            d_HudChat.GuiTypingBuffer = "";
                        }
                    }
                    return;
                }

                string strFreemoveNotAllowed = "You are not allowed to enable freemove.";

                if (e.Key == GetKey(OpenTK.Input.Key.F1))
                {
                    if (!this.AllowFreemove)
                    {
                        Log(strFreemoveNotAllowed);
                        return;
                    }
                    movespeed = basemovespeed * 1;
                    Log("Move speed: 1x.");
                }
                if (e.Key == GetKey(OpenTK.Input.Key.F2))
                {
                    if (!this.AllowFreemove)
                    {
                        Log(strFreemoveNotAllowed);
                        return;
                    }
                    movespeed = basemovespeed * 10;
                    Log(string.Format(language.MoveSpeed(), 10.ToString()));
                }
                if (e.Key == GetKey(OpenTK.Input.Key.F3))
                {
                    if (!this.AllowFreemove)
                    {
                        Log(strFreemoveNotAllowed);
                        return;
                    }
                    player.movedz = 0;
                    if (!ENABLE_FREEMOVE)
                    {
                        ENABLE_FREEMOVE = true;
                        Log(language.MoveFree());
                    }
                    else if (ENABLE_FREEMOVE && (!ENABLE_NOCLIP))
                    {
                        ENABLE_NOCLIP = true;
                        Log(language.MoveFreeNoclip());
                    }
                    else if (ENABLE_FREEMOVE && ENABLE_NOCLIP)
                    {
                        ENABLE_FREEMOVE = false;
                        ENABLE_NOCLIP = false;
                        Log(language.MoveNormal());
                    }
                }
                if (e.Key == GetKey(OpenTK.Input.Key.I))
                {
                    drawblockinfo = !drawblockinfo;
                }
                PerformanceInfo.Set("height", "height:" + d_Heightmap.GetBlock((int)player.playerposition.X, (int)player.playerposition.Z));
                if (e.Key == GetKey(OpenTK.Input.Key.F5))
                {
                    if (cameratype == CameraType.Fpp)
                    {
                        cameratype = CameraType.Tpp;
                        ENABLE_TPP_VIEW = true;
                    }
                    else if (cameratype == CameraType.Tpp)
                    {
                        cameratype = CameraType.Overhead;
                        overheadcamera = true;
                        FreeMouse = true;
                        ENABLE_TPP_VIEW = true;
                        playerdestination = Vector3Ref.Create(player.playerposition.X, player.playerposition.Y, player.playerposition.Z);
                    }
                    else if (cameratype == CameraType.Overhead)
                    {
                        cameratype = CameraType.Fpp;
                        FreeMouse = false;
                        ENABLE_TPP_VIEW = false;
                        overheadcamera = false;
                    }
                    else throw new Exception();
                }
                if (e.Key == GetKey(OpenTK.Input.Key.Plus) || e.Key == GetKey(OpenTK.Input.Key.KeypadPlus))
                {
                    if (cameratype == CameraType.Overhead)
                    {
                        overheadcameradistance -= 1;
                    }
                    else if (cameratype == CameraType.Tpp)
                    {
                        tppcameradistance -= 1;
                    }
                }
                if (e.Key == GetKey(OpenTK.Input.Key.Minus) || e.Key == GetKey(OpenTK.Input.Key.KeypadMinus))
                {
                    if (cameratype == CameraType.Overhead)
                    {
                        overheadcameradistance += 1;
                    }
                    else if (cameratype == CameraType.Tpp)
                    {
                        tppcameradistance += 1;
                    }
                }
                if (overheadcameradistance < TPP_CAMERA_DISTANCE_MIN) { overheadcameradistance = TPP_CAMERA_DISTANCE_MIN; }
                if (overheadcameradistance > TPP_CAMERA_DISTANCE_MAX) { overheadcameradistance = TPP_CAMERA_DISTANCE_MAX; }

                if (tppcameradistance < TPP_CAMERA_DISTANCE_MIN) { tppcameradistance = TPP_CAMERA_DISTANCE_MIN; }
                if (tppcameradistance > TPP_CAMERA_DISTANCE_MAX) { tppcameradistance = TPP_CAMERA_DISTANCE_MAX; }

                if (e.Key == GetKey(OpenTK.Input.Key.F6))
                {
                    RedrawAllBlocks();
                }
                if (e.Key == OpenTK.Input.Key.F8)
                {
                    ToggleVsync();
                    if (ENABLE_LAG == 0) { Log(language.FrameRateVsync()); }
                    if (ENABLE_LAG == 1) { Log(language.FrameRateUnlimited()); }
                    if (ENABLE_LAG == 2) { Log(language.FrameRateLagSimulation()); }
                }
                if (e.Key == GetKey(OpenTK.Input.Key.F12))
                {
                    game.platform.SaveScreenshot();
                    screenshotflash = 5;
                }
                if (e.Key == GetKey(OpenTK.Input.Key.Tab))
                {
                    Packet_Client p = new Packet_Client();
                    p.Id = Packet_ClientIdEnum.SpecialKey;
                    p.SpecialKey_ = new Packet_ClientSpecialKey();
                    p.SpecialKey_.Key_ = Packet_SpecialKeyEnum.TabPlayerList;
                    SendPacketClient(p);
                }
                if (e.Key == GetKey(OpenTK.Input.Key.E))
                {
                    if (currentAttackedBlock != null)
                    {
                        Vector3 pos = new Vector3(currentAttackedBlock.X, currentAttackedBlock.Y, currentAttackedBlock.Z);
                        int blocktype = d_Map.GetBlock(currentAttackedBlock.X, currentAttackedBlock.Y, currentAttackedBlock.Z);
                        if (IsUsableBlock(blocktype))
                        {
                            if (d_Data.IsRailTile(blocktype))
                            {
                                player.playerposition.X = pos.X + .5f;
                                player.playerposition.Y = pos.Z + 1;
                                player.playerposition.Z = pos.Y + .5f;
                                ENABLE_FREEMOVE = false;
                            }
                            else
                            {
                                SendSetBlock(pos, Packet_BlockSetModeEnum.Use, 0, ActiveMaterial);
                            }
                        }
                    }
                }
                if (e.Key == GetKey(OpenTK.Input.Key.R))
                {
                    Packet_Item item = d_Inventory.RightHand[ActiveMaterial];
                    if (item != null && item.ItemClass == Packet_ItemClassEnum.Block
                        && game.blocktypes[item.BlockId].IsPistol
                        && reloadstartMilliseconds == 0)
                    {
                        int sound = rnd.Next(game.blocktypes[item.BlockId].Sounds.Reload.Length);
                        AudioPlay(game.blocktypes[item.BlockId].Sounds.Reload[sound] + ".ogg");
                        reloadstartMilliseconds = game.platform.TimeMillisecondsFromStart();
                        reloadblock = item.BlockId;
                        Packet_Client p = new Packet_Client();
                        p.Id = Packet_ClientIdEnum.Reload;
                        p.Reload = new Packet_ClientReload();
                        SendPacketClient(p);
                    }
                }
                if (e.Key == GetKey(OpenTK.Input.Key.O))
                {
                    Respawn();
                }
                if (e.Key == GetKey(OpenTK.Input.Key.L))
                {
                    Packet_Client p = new Packet_Client();
                    {
                        p.Id = Packet_ClientIdEnum.SpecialKey;
                        p.SpecialKey_ = new Packet_ClientSpecialKey();
                        p.SpecialKey_.Key_ = Packet_SpecialKeyEnum.SelectTeam;
                    }
                    SendPacketClient(p);
                }
                if (e.Key == GetKey(OpenTK.Input.Key.P))
                {
                    Packet_Client p = new Packet_Client();
                    {
                        p.Id = Packet_ClientIdEnum.SpecialKey;
                        p.SpecialKey_ = new Packet_ClientSpecialKey();
                        p.SpecialKey_.Key_ = Packet_SpecialKeyEnum.SetSpawn;
                    }
                    SendPacketClient(p);
                    PlayerPositionSpawn = ToVector3(player.playerposition);
                    player.playerposition.X = (int)player.playerposition.X + 0.5f;
                    //player.playerposition.Y = player.playerposition.Y;
                    player.playerposition.Z = (int)player.playerposition.Z + 0.5f;
                }
                if (e.Key == GetKey(OpenTK.Input.Key.F))
                {
                    ToggleFog();
                    Log(string.Format(language.FogDistance(), d_Config3d.viewdistance));
                    OnResize(new EventArgs());
                }
                if (e.Key == GetKey(OpenTK.Input.Key.B))
                {
                    guistate = GuiState.Inventory;
                    menustate = new MenuState();
                    FreeMouse = true;
                }
                HandleMaterialKeys(e);
                if (e.Key == GetKey(OpenTK.Input.Key.Escape))
                {
                    escapeMenu.EscapeMenuStart();
                }
            }
            else if (guistate == GuiState.EscapeMenu)
            {
                escapeMenu.EscapeMenuKeyDown(e);
                return;
            }
            else if (guistate == GuiState.Inventory)
            {
                if (e.Key == GetKey(OpenTK.Input.Key.B)
                    || e.Key == GetKey(OpenTK.Input.Key.Escape))
                {
                    GuiStateBackToGame();
                }
                if (e.Key == GetKey(OpenTK.Input.Key.F12))
                {
                    game.platform.SaveScreenshot();
                    screenshotflash = 5;
                }
                return;
            }
            else if (guistate == GuiState.ModalDialog)
            {
                if (e.Key == GetKey(OpenTK.Input.Key.B)
                    || e.Key == GetKey(OpenTK.Input.Key.Escape))
                {
                    GuiStateBackToGame();
                }
                if (e.Key == GetKey(OpenTK.Input.Key.F12))
                {
                    game.platform.SaveScreenshot();
                    screenshotflash = 5;
                }
            }
            else if (guistate == GuiState.MapLoading)
            {
            }
            else if (guistate == GuiState.CraftingRecipes)
            {
                if (e.Key == GetKey(OpenTK.Input.Key.Escape))
                {
                    GuiStateBackToGame();
                }
            }
            else throw new Exception();
        }

        public Vector3 ToVector3(Vector3Ref vector3Ref)
        {
            return new Vector3(vector3Ref.X, vector3Ref.Y, vector3Ref.Z);
        }

        public void ToggleVsync()
        {
            ENABLE_LAG++;
            ENABLE_LAG = ENABLE_LAG % 3;
            UseVsync();
        }

        public void UseVsync()
        {
            d_GlWindow.VSync = (ENABLE_LAG == 1) ? VSyncMode.Off : VSyncMode.On;
        }

        public void HandleMaterialKeys(OpenTK.Input.KeyboardKeyEventArgs e)
        {
            if (e.Key == GetKey(OpenTK.Input.Key.Number1)) { ActiveMaterial = 0; }
            if (e.Key == GetKey(OpenTK.Input.Key.Number2)) { ActiveMaterial = 1; }
            if (e.Key == GetKey(OpenTK.Input.Key.Number3)) { ActiveMaterial = 2; }
            if (e.Key == GetKey(OpenTK.Input.Key.Number4)) { ActiveMaterial = 3; }
            if (e.Key == GetKey(OpenTK.Input.Key.Number5)) { ActiveMaterial = 4; }
            if (e.Key == GetKey(OpenTK.Input.Key.Number6)) { ActiveMaterial = 5; }
            if (e.Key == GetKey(OpenTK.Input.Key.Number7)) { ActiveMaterial = 6; }
            if (e.Key == GetKey(OpenTK.Input.Key.Number8)) { ActiveMaterial = 7; }
            if (e.Key == GetKey(OpenTK.Input.Key.Number9)) { ActiveMaterial = 8; }
            if (e.Key == GetKey(OpenTK.Input.Key.Number0)) { ActiveMaterial = 9; }
        }
        List<string> typinglog = new List<string>();
        public void GuiStateBackToGame()
        {
            guistate = GuiState.Normal;
            FreeMouse = false;
            freemousejustdisabled = true;
        }
        public CrashReporter crashreporter;
        private void Connect()
        {
            escapeMenu.LoadOptions();
            MapLoaded += new EventHandler<EventArgs>(network_MapLoaded);

            while (issingleplayer && !StartedSinglePlayerServer)
            {
                Thread.Sleep(1);
            }

            if (string.IsNullOrEmpty(connectdata.ServerPassword))
            {
                Connect(connectdata.Ip, connectdata.Port, connectdata.Username, connectdata.Auth);
            }
            else
            {
                Connect(connectdata.Ip, connectdata.Port, connectdata.Username, connectdata.Auth, connectdata.ServerPassword);
            }
            MapLoadingStart();
        }
        void network_MapLoaded(object sender, EventArgs e)
        {
            terrainRenderer.StartTerrain();
            RedrawAllBlocks();
            materialSlots = d_Data.DefaultMaterialSlots();
            GuiStateBackToGame();
            OnNewMap();
        }
        //[Obsolete]
        int[] materialSlots;
        //[Obsolete]
        public int[] MaterialSlots
        {
            get
            {
                return game.MaterialSlots();
            }
            set
            {
                materialSlots = value;
            }
        }
        public void OnResize(EventArgs e)
        {
            //.mainwindow.OnResize(e);

            GL.Viewport(0, 0, Width, Height);
            this.Set3dProjection();
        }
        Vector3 up = new Vector3(0f, 1f, 0f);
        public Point mouse_current { get { return new Point(game.mouseCurrentX, game.mouseCurrentY); } set { game.mouseCurrentX = value.X; game.mouseCurrentY = value.Y; } }
        Point mouse_previous;

        void UpdateMouseButtons()
        {
            if (!d_GlWindow.Focused)
            {
                return;
            }
            mouseleftclick = (!wasmouseleft) && Mouse[OpenTK.Input.MouseButton.Left];
            mouserightclick = (!wasmouseright) && Mouse[OpenTK.Input.MouseButton.Right];
            mouseleftdeclick = wasmouseleft && (!Mouse[OpenTK.Input.MouseButton.Left]);
            mouserightdeclick = wasmouseright && (!Mouse[OpenTK.Input.MouseButton.Right]);
            wasmouseleft = Mouse[OpenTK.Input.MouseButton.Left];
            wasmouseright = Mouse[OpenTK.Input.MouseButton.Right];
        }
        void UpdateMousePosition()
        {
            mouse_current = System.Windows.Forms.Cursor.Position;
            if (FreeMouse)
            {
                mouse_current = new Point(mouse_current.X - d_GlWindow.X, mouse_current.Y - d_GlWindow.Y);
                mouse_current = new Point(mouse_current.X, mouse_current.Y - System.Windows.Forms.SystemInformation.CaptionHeight);
            }
            if (!d_GlWindow.Focused)
            {
                return;
            }
            if (freemousejustdisabled)
            {
                mouse_previous = mouse_current;
                freemousejustdisabled = false;
            }
            if (!FreeMouse)
            {
                //There are two versions:

                //a) System.Windows.Forms.Cursor and GameWindow.CursorVisible = true.
                //It works by centering global mouse cursor every frame.
                //*Windows:
                //   *OK.
                //   *On a few YouTube videos mouse cursor is not hiding properly.
                //    That could be just a problem with video recording software.
                //*Ubuntu: Broken, mouse cursor doesn't hide.
                //*Mac: Broken, mouse doesn't move at all.

                //b) OpenTk.Input.Mouse and GameWindow.CursorVisible = false.
                //Uses raw mouse coordinates, movement is not accelerated.
                //*Windows:
                //  *OK.
                //  *Worse than a), because this doesn't use system-wide mouse acceleration.
                //*Ubuntu: Broken, crashes with "libxi" library missing.
                //*Mac: OK.

                if (!IsMac)
                {
                    //a)
                    int centerx = d_GlWindow.Bounds.Left + (d_GlWindow.Bounds.Width / 2);
                    int centery = d_GlWindow.Bounds.Top + (d_GlWindow.Bounds.Height / 2);

                    mouseDeltaX = mouse_current.X - mouse_previous.X;
                    mouseDeltaY = mouse_current.Y - mouse_previous.Y;

                    System.Windows.Forms.Cursor.Position =
                        new Point(centerx, centery);
                    mouse_previous = new Point(centerx, centery);
                }
                else
                {
                    //b)
                    var state = OpenTK.Input.Mouse.GetState();
                    float dx = state.X - mouse_previous_state.X;
                    float dy = state.Y - mouse_previous_state.Y;
                    mouse_previous_state = state;
                    //These are raw coordinates, so need to apply acceleration manually.
                    float dx2 = (dx * Math.Abs(dx) * MouseAcceleration1);
                    float dy2 = (dy * Math.Abs(dy) * MouseAcceleration1);
                    dx2 += dx * MouseAcceleration2;
                    dy2 += dy * MouseAcceleration2;
                    mouseDeltaX = dx2;
                    mouseDeltaY = dy2;
                }
            }
        }
        public float MouseAcceleration1 = 0.12f;
        public float MouseAcceleration2 = 0.7f;
        OpenTK.Input.MouseState mouse_previous_state;
        float fallspeed { get { return movespeed / 10; } }
        int lastbuildMilliseconds;
        float walksoundtimer = 0;
        int lastwalksound = 0;
        float stepsoundduration = 0.4f;
        void UpdateWalkSound(double dt)
        {
            if (dt == -1)
            {
                dt = stepsoundduration / 2;
            }
            walksoundtimer += (float)dt;
            string[] soundwalk = soundwalkcurrent();
            if (soundwalk.Length == 0)
            {
                return;
            }
            if (walksoundtimer >= stepsoundduration)
            {
                walksoundtimer = 0;
                lastwalksound++;
                if (lastwalksound >= soundwalk.Length)
                {
                    lastwalksound = 0;
                }
                if (rnd.Next(100) < 40)
                {
                    lastwalksound = rnd.Next(soundwalk.Length);
                }
                AudioPlay(soundwalk[lastwalksound]);
            }
        }
        string[] soundwalkcurrent()
        {
            IntRef b = BlockUnderPlayer();
            if (b != null)
            {
                return d_Data.WalkSound()[b.value];
            }
            return d_Data.WalkSound()[0];
        }
        bool IsInLeft(Vector3 player_yy, Vector3 tile_yy)
        {
            return (int)player_yy.X == (int)tile_yy.X && (int)player_yy.Z == (int)tile_yy.Z;
        }

        bool enable_move = true;
        public bool ENABLE_MOVE { get { return enable_move; } set { enable_move = value; } }
        public void OnUpdateFrame(FrameEventArgs e)
        {
        }
        //DateTime lasttodo;
        void FrameTick(FrameEventArgs e)
        {
            //if ((DateTime.Now - lasttodo).TotalSeconds > BuildDelay && todo.Count > 0)
            //UpdateTerrain();
            OnNewFrame(e.Time);
            UpdateMousePosition();
            if (guistate == GuiState.Normal && game.enableCameraControl)
            {
                UpdateMouseViewportControl((float)e.Time);
            }
            NetworkProcess();
            if (guistate == GuiState.MapLoading) { return; }

            bool angleup = false;
            bool angledown = false;
            float overheadcameraanglemovearea = 0.05f;
            float overheadcameraspeed = 3;
            if (guistate == GuiState.Normal && d_GlWindow.Focused && cameratype == CameraType.Overhead)
            {
                if (mouse_current.X > Width - Width * overheadcameraanglemovearea)
                {
                    overheadcameraK.TurnLeft((float)e.Time * overheadcameraspeed);
                }
                if (mouse_current.X < Width * overheadcameraanglemovearea)
                {
                    overheadcameraK.TurnRight((float)e.Time * overheadcameraspeed);
                }
                if (mouse_current.Y < Height * overheadcameraanglemovearea)
                {
                    angledown = true;
                }
                if (mouse_current.Y > Height - Height * overheadcameraanglemovearea)
                {
                    angleup = true;
                }
            }
            bool wantsjump = GuiTyping == TypingState.None && Keyboard[GetKey(OpenTK.Input.Key.Space)];
            bool shiftkeydown = Keyboard[GetKey(OpenTK.Input.Key.ShiftLeft)];
            int movedx = 0;
            int movedy = 0;
            bool moveup = false;
            bool movedown = false;
            if (guistate == GuiState.Normal)
            {
                if (GuiTyping == TypingState.None)
                {
                    if (overheadcamera)
                    {
                        CameraMove m = new CameraMove();
                        if (Keyboard[GetKey(OpenTK.Input.Key.A)]) { overheadcameraK.TurnRight((float)e.Time * overheadcameraspeed); }
                        if (Keyboard[GetKey(OpenTK.Input.Key.D)]) { overheadcameraK.TurnLeft((float)e.Time * overheadcameraspeed); }
                        if (Keyboard[GetKey(OpenTK.Input.Key.W)]) { angleup = true; }
                        if (Keyboard[GetKey(OpenTK.Input.Key.S)]) { angledown = true; }
                        overheadcameraK.Center.X = player.playerposition.X;
                        overheadcameraK.Center.Y = player.playerposition.Y;
                        overheadcameraK.Center.Z = player.playerposition.Z;
                        m.Distance = overheadcameradistance;
                        m.AngleUp = angleup;
                        m.AngleDown = angledown;
                        overheadcameraK.Move(m, (float)e.Time);
                        if ((ToVector3(player.playerposition) - ToVector3(playerdestination)).Length >= 1f)
                        {
                            movedy += 1;
                            if (d_Physics.reachedwall)
                            {
                                wantsjump = true;
                            }
                            //player orientation
                            Vector3 q = ToVector3(playerdestination) - ToVector3(player.playerposition);
                            float angle = VectorAngleGet(q);
                            player.playerorientation.Y = (float)Math.PI / 2 + angle;
                            player.playerorientation.X = (float)Math.PI;
                        }
                    }
                    else if (ENABLE_MOVE)
                    {
                        if (Keyboard[GetKey(OpenTK.Input.Key.W)]) { movedy += 1; }
                        if (Keyboard[GetKey(OpenTK.Input.Key.S)]) { movedy += -1; }
                        if (Keyboard[GetKey(OpenTK.Input.Key.A)]) { movedx += -1; localplayeranimationhint.leanleft = true; localstance = 1; }
                        else { localplayeranimationhint.leanleft = false; }
                        if (Keyboard[GetKey(OpenTK.Input.Key.D)]) { movedx += 1; localplayeranimationhint.leanright = true; localstance = 2; }
                        else { localplayeranimationhint.leanright = false; }
                        if (!localplayeranimationhint.leanleft && !localplayeranimationhint.leanright) { localstance = 0; }
                    }
                }
                if (ENABLE_FREEMOVE || Swimming)
                {
                    if (GuiTyping == TypingState.None && Keyboard[GetKey(OpenTK.Input.Key.Space)])
                    {
                        moveup = true;
                    }
                    if (GuiTyping == TypingState.None && Keyboard[GetKey(OpenTK.Input.Key.ControlLeft)])
                    {
                        movedown = true;
                    }
                }
            }
            else if (guistate == GuiState.EscapeMenu)
            {
            }
            else if (guistate == GuiState.Inventory)
            {
            }
            else if (guistate == GuiState.MapLoading)
            {
                //todo back to game when escape key pressed.
            }
            else if (guistate == GuiState.CraftingRecipes)
            {
            }
            else if (guistate == GuiState.ModalDialog)
            {
            }
            else throw new Exception();
            float movespeednow = MoveSpeedNow();
            Acceleration acceleration = new Acceleration();
            IntRef blockunderplayer = BlockUnderPlayer();
            {
                //slippery walk on ice and when swimming
                if ((blockunderplayer != null && d_Data.IsSlipperyWalk()[blockunderplayer.value]) || Swimming)
                {
                    acceleration = new Acceleration();
                    {
                        acceleration.acceleration1 = one * 99 / 100;
                        acceleration.acceleration2 = one * 2 / 10;
                        acceleration.acceleration3 = 70;
                    }
                }
            }
            float jumpstartacceleration = 13.333f * d_Physics.gravity;
            if (blockunderplayer != null && blockunderplayer.value == d_Data.BlockIdTrampoline()
                && (!player.isplayeronground) && !shiftkeydown)
            {
                wantsjump = true;
                jumpstartacceleration = 20.666f * d_Physics.gravity;
            }
            //no aircontrol
            if (!player.isplayeronground)
            {
                acceleration = new Acceleration();
                {
                    acceleration.acceleration1 = one * 99 / 100;
                    acceleration.acceleration2 = one * 2 / 10;
                    acceleration.acceleration3 = 70;
                };
            }
            float pushX = 0;
            float pushY = 0;
            float pushZ = 0;
            Vector3Ref push = new Vector3Ref();
            GetPlayersPush(push);
            GetEntitiesPush(push);
            pushX += push.X;
            pushY += push.Y;
            pushZ += push.Z;
            EntityExpire((float)e.Time);
            MoveInfo move = new MoveInfo();
            {
                move.movedx = movedx;
                move.movedy = movedy;
                move.acceleration = acceleration;
                move.ENABLE_FREEMOVE = ENABLE_FREEMOVE;
                move.ENABLE_NOCLIP = ENABLE_NOCLIP;
                move.jumpstartacceleration = jumpstartacceleration;
                move.movespeednow = movespeednow;
                move.moveup = moveup;
                move.movedown = movedown;
                move.Swimming = Swimming;
                move.wantsjump = wantsjump;
                move.shiftkeydown = shiftkeydown;
            }
            BoolRef soundnow = new BoolRef();
            if (FollowId() == null)
            {
                d_Physics.Move(player, move, (float)e.Time, soundnow, Vector3Ref.Create(pushX, pushY, pushZ), Players[LocalPlayerId].ModelHeight);
                if (soundnow.value)
                {
                    UpdateWalkSound(-1);
                }
                if (player.isplayeronground && movedx != 0 || movedy != 0)
                {
                    UpdateWalkSound(e.Time);
                }
                UpdateBlockDamageToPlayer();
                UpdateFallDamageToPlayer();
            }
            else
            {
                if (FollowId().value == LocalPlayerId)
                {
                    move.movedx = 0;
                    move.movedy = 0;
                    move.wantsjump = false;
                    d_Physics.Move(player, move, (float)e.Time, soundnow, Vector3Ref.Create(pushX, pushY, pushZ), players[LocalPlayerId].ModelHeight);
                }
            }
            if (guistate == GuiState.CraftingRecipes)
            {
                CraftingMouse();
            }
            if (guistate == GuiState.EscapeMenu)
            {
                //EscapeMenuMouse();
            }
            if (guistate == GuiState.Normal)
            {
                UpdatePicking();
            }
            //must be here because frametick can be called more than once per render frame.
            keyevent = null;
            keyeventup = null;

            Vector3 orientation = new Vector3((float)Math.Sin(LocalPlayerOrientation.Y), 0, -(float)Math.Cos(LocalPlayerOrientation.Y));
            Vector3 listenerPos = EyesPos();
            game.platform.AudioUpdateListener(listenerPos.X, listenerPos.Y, listenerPos.Z, orientation.X, orientation.Y, orientation.Z);
            Packet_Item activeitem = d_Inventory.RightHand[ActiveMaterial];
            int activeblock = 0;
            if (activeitem != null) { activeblock = activeitem.BlockId; }
            if (activeblock != PreviousActiveMaterialBlock)
            {
                Packet_Client p = new Packet_Client();
                {
                    p.Id = Packet_ClientIdEnum.ActiveMaterialSlot;
                    p.ActiveMaterialSlot = new Packet_ClientActiveMaterialSlot();
                    p.ActiveMaterialSlot.ActiveMaterialSlot = ActiveMaterial;
                }
                SendPacketClient(p);
            }
            PreviousActiveMaterialBlock = activeblock;
            playervelocity.X = LocalPlayerPosition.X - lastplayerposition.X;
            playervelocity.Y = LocalPlayerPosition.Y - lastplayerposition.Y;
            playervelocity.Z = LocalPlayerPosition.Z - lastplayerposition.Z;
            playervelocity.X *= 75;
            playervelocity.Y *= 75;
            playervelocity.Z *= 75;
            lastplayerposition = LocalPlayerPosition;
            if (reloadstartMilliseconds != 0
                && (one * (game.platform.TimeMillisecondsFromStart() - reloadstartMilliseconds) / 1000)
                > DeserializeFloat(game.blocktypes[reloadblock].ReloadDelayFloat))
            {
                {
                    int loaded = TotalAmmo[reloadblock];
                    loaded = Math.Min(game.blocktypes[reloadblock].AmmoMagazine, loaded);
                    LoadedAmmo[reloadblock] = loaded;
                    reloadstartMilliseconds = 0;
                    reloadblock = -1;
                }
            }
            for (int i = 0; i < entitiesCount; i++)
            {
                Entity entity = entities[i];
                if (entity == null) { continue; }
                if (entity.grenade == null) { continue; }
                UpdateGrenade(i, (float)e.Time);
            }
        }

        public Vector3 EyesPos()
        {
            return new Vector3(game.EyesPosX(), game.EyesPosY(), game.EyesPosZ());
        }

        Vector3 lastplayerposition;
        
        //bool test;

        //TODO server side?
        private void UpdateBlockDamageToPlayer()
        {
            var p = LocalPlayerPosition;
            p += new Vector3(0, Players[LocalPlayerId].EyeHeight, 0);
            int block1 = 0;
            int block2 = 0;
            if (d_Map.IsValidPos((int)Math.Floor(p.X), (int)Math.Floor(p.Z), (int)Math.Floor(p.Y)))
            {
                block1 = d_Map.GetBlock((int)p.X, (int)p.Z, (int)p.Y);
            }
            if (d_Map.IsValidPos((int)Math.Floor(p.X), (int)Math.Floor(p.Z), (int)Math.Floor(p.Y) - 1))
            {
                block2 = d_Map.GetBlock((int)p.X, (int)p.Z, (int)p.Y - 1);
            }
            
            int damage = d_Data.DamageToPlayer()[block1] + d_Data.DamageToPlayer()[block2];
            if (damage > 0)
            {
            	int hurtingBlock = block1;	//Use block at eyeheight as source block
            	if (hurtingBlock == 0) { hurtingBlock = block2; }	//Fallback to block at feet if eyeheight block is air
                BlockDamageToPlayerTimer.Update(delegate { ApplyDamageToPlayer(damage, Packet_DeathReasonEnum.BlockDamage, hurtingBlock); });
            }

            //Player drowning
            int deltaTime = (int)(one * (game.platform.TimeMillisecondsFromStart() - lastOxygenTickMilliseconds)); //Time in milliseconds
            if (deltaTime >= 1000)
            {
                if (WaterSwimming)
                {
                    PlayerStats.CurrentOxygen -= 1;
                    if (PlayerStats.CurrentOxygen <= 0)
                    {
                        PlayerStats.CurrentOxygen = 0;
                        BlockDamageToPlayerTimer.Update(delegate { ApplyDamageToPlayer((int)Math.Ceiling((float)PlayerStats.MaxHealth / 10.0f), Packet_DeathReasonEnum.Drowning, block1); });
                    }
                }
                else
                {
                    PlayerStats.CurrentOxygen = PlayerStats.MaxOxygen;
                }
                {
                    Packet_Client packet =new Packet_Client();
                    packet.Id = Packet_ClientIdEnum.Oxygen;
                    packet.Oxygen = new Packet_ClientOxygen();
                    packet.Oxygen.CurrentOxygen = PlayerStats.CurrentOxygen;
                    SendPacketClient(packet);
                }
                lastOxygenTickMilliseconds = game.platform.TimeMillisecondsFromStart();
            }
        }

        public const int BlockDamageToPlayerEvery = 1;

        Timer BlockDamageToPlayerTimer = new Timer() { INTERVAL = BlockDamageToPlayerEvery, MaxDeltaTime = BlockDamageToPlayerEvery * 2 };

        public OpenTK.Input.Key GetKey(OpenTK.Input.Key key)
        {
            if (escapeMenu.options.Keys[(int)key] != 0)
            {
                return (OpenTK.Input.Key)escapeMenu.options.Keys[(int)key];
            }
            return key;
        }
        public bool KeyIsEqualChar(OpenTK.Input.Key key1, char key2)
        {
            // TODO: Any better solution? http://www.opentk.com/node/1202
            if (escapeMenu.options.Keys[(int)key1] != 0)
            {
                return ((OpenTK.Input.Key)escapeMenu.options.Keys[(int)key1]).ToString().Equals(key2.ToString(), StringComparison.InvariantCultureIgnoreCase);
            }
            return key1.ToString().Equals(key2.ToString(), StringComparison.InvariantCultureIgnoreCase);
        }
        private float VectorAngleGet(Vector3 q)
        {
            return (float)(Math.Acos(q.X / q.Length) * Math.Sign(q.Z));
        }
        private float MoveSpeedNow()
        {
            float movespeednow = movespeed;
            {
                //walk faster on cobblestone
                IntRef blockunderplayer = BlockUnderPlayer();
                if (blockunderplayer != null)
                {
                    movespeednow *= d_Data.WalkSpeed()[blockunderplayer.value];
                }
            }
            if (Keyboard[GetKey(OpenTK.Input.Key.ShiftLeft)])
            {
                //enable_acceleration = false;
                movespeednow *= 0.2f;
            }
            Packet_Item item = d_Inventory.RightHand[ActiveMaterial];
            if (item != null && item.ItemClass == Packet_ItemClassEnum.Block)
            {
                movespeednow *= DeserializeFloat(game.blocktypes[item.BlockId].WalkSpeedWhenUsedFloat);
                if (IronSights)
                {
                    movespeednow *= DeserializeFloat(game.blocktypes[item.BlockId].IronSightsMoveSpeedFloat);
                }
            }
            return movespeednow;
        }

        void UpdateMouseViewportControl(float dt)
        {
            game.UpdateMouseViewportControl(dt);
        }

        private void UpdatePicking()
        {
            if (FollowId() != null)
            {
                SelectedBlockPositionX = 0 - 1;
                SelectedBlockPositionY = 0 - 1;
                SelectedBlockPositionZ = 0 - 1;
                return;
            }
            int bulletsshot = 0;
            bool IsNextShot = false;
        NextBullet:
            bool left = Mouse[OpenTK.Input.MouseButton.Left];//destruct
            bool middle = Mouse[OpenTK.Input.MouseButton.Middle];//clone material as active
            bool right = Mouse[OpenTK.Input.MouseButton.Right];//build

            if (!leftpressedpicking)
            {
                if (mouseleftclick)
                {
                    leftpressedpicking = true;
                }
                else
                {
                    left = false;
                }
            }
            else
            {
                if (mouseleftdeclick)
                {
                    leftpressedpicking = false;
                    left = false;
                }
            }
            if (!left)
            {
                currentAttackedBlock = null;
            }

            float pick_distance = PICK_DISTANCE;
            if (cameratype == CameraType.Tpp) { pick_distance = tppcameradistance * 2; }
            if (cameratype == CameraType.Overhead) { pick_distance = overheadcameradistance; }

            Packet_Item item = d_Inventory.RightHand[ActiveMaterial];
            bool ispistol = (item != null && game.blocktypes[item.BlockId].IsPistol);
            bool ispistolshoot = ispistol && left;
            bool isgrenade = ispistol && game.blocktypes[item.BlockId].PistolType == Packet_PistolTypeEnum.Grenade;
            if (ispistol && isgrenade)
            {
                ispistolshoot = mouseleftdeclick;
            }
            //grenade cooking
            if (mouseleftclick)
            {
                grenadecookingstartMilliseconds = game.platform.TimeMillisecondsFromStart();
                if (ispistol && isgrenade)
                {
                    if (game.blocktypes[item.BlockId].Sounds.Shoot.Length > 0)
                    {
                        AudioPlay(game.blocktypes[item.BlockId].Sounds.Shoot[0] + ".ogg");
                    }
                }
            }
            float wait = ((float)(game.platform.TimeMillisecondsFromStart() - grenadecookingstartMilliseconds) / 1000);
            if (isgrenade && left)
            {
                if (wait >= grenadetime && isgrenade && grenadecookingstartMilliseconds != 0)
                {
                    ispistolshoot = true;
                    mouseleftdeclick = true;
                }
                else
                {
                    return;
                }
            }
            else
            {
                grenadecookingstartMilliseconds = 0;
            }

            if (ispistol && mouserightclick && (game.platform.TimeMillisecondsFromStart() - lastironsightschangeMilliseconds) >= 500)
            {
                IronSights = !IronSights;
                lastironsightschangeMilliseconds = game.platform.TimeMillisecondsFromStart();
            }

            float unit_x = 0;
            float unit_y = 0;
            int NEAR = 1;
            int FOV = (int)currentfov() * 10; // 600
            float ASPECT = 640f / 480;
            float near_height = NEAR * (float)(Math.Tan(FOV * Math.PI / 360.0));
            Vector3 ray = new Vector3(unit_x * near_height * ASPECT, unit_y * near_height, 1);//, 0);

            Vector3 ray_start_point = new Vector3(0, 0, 0);
            PointFloatRef aim = GetAim();
            if (overheadcamera || aim.X != 0 || aim.Y != 0)
            {
                float mx = 0;
                float my = 0;
                if (overheadcamera)
                {
                    mx = (float)mouse_current.X / Width - 0.5f;
                    my = (float)mouse_current.Y / Height - 0.5f;
                }
                else if (ispistolshoot && (aim.X != 0 || aim.Y != 0))
                {
                    mx += aim.X / Width;
                    my += aim.Y / Height;
                }
                //ray_start_point = new Vector3(mx * 1.4f, -my * 1.1f, 0.0f);
                ray_start_point = new Vector3(mx * 3f, -my * 2.2f, -1.0f);
            }

            //Matrix4 the_modelview;
            //Read the current modelview matrix into the array the_modelview
            //GL.GetFloat(GetPName.ModelviewMatrix, out the_modelview);
            
            Matrix4 theModelView = ToMatrix4(mvMatrix.Peek());
            if (theModelView.M11 == float.NaN || theModelView.Equals(new Matrix4())) { return; }
            //Matrix4 theModelView = d_The3d.ModelViewMatrix;
            theModelView.Invert();
            //the_modelview = new Matrix4();
            ray = Vector3.Transform(ray, theModelView);
            ray_start_point = Vector3.Transform(ray_start_point, theModelView);
            Line3D pick = new Line3D();
            Vector3 raydir = -(ray - ray_start_point);
            raydir.Normalize();
            pick.Start = Vector3ToFloatArray(ray + Vector3.Multiply(raydir, 1f)); //do not pick behind
            pick.End = Vector3ToFloatArray( ray + Vector3.Multiply(raydir, pick_distance * ((ispistolshoot) ? 100 : 2)));

            //pick models
            selectedmodelid = -1;
            //Intersection intersection1 = new Intersection();
            //foreach (var m in Models)
            //{
            //    Vector3 closestmodelpos = new Vector3(int.MaxValue, int.MaxValue, int.MaxValue);
            //    foreach (var t in m.TrianglesForPicking)
            //    {
            //        float[] intersection_ = new float[3];
            //        if (intersection1.RayTriangle(pick, t, intersection_) == 1)
            //        {
            //            Vector3 intersection = FloatArrayToVector3(intersection_);
            //            if ((FloatArrayToVector3(pick.Start) - intersection).Length > pick_distance)
            //            {
            //                continue;
            //            }
            //            if ((FloatArrayToVector3(pick.Start) - intersection).Length < (FloatArrayToVector3(pick.Start) - closestmodelpos).Length)
            //            {
            //                closestmodelpos = intersection;
            //                selectedmodelid = m.Id;
            //            }
            //        }
            //    }
            //}
            if (selectedmodelid != -1)
            {
                SelectedBlockPositionX = -1;
                SelectedBlockPositionY = -1;
                SelectedBlockPositionZ = -1;
                if (mouseleftclick)
                {
                    ModelClick(selectedmodelid);
                }
                mouseleftclick = false;
                leftpressedpicking = false;
                return;
            }

            if (left)
            {
                d_Weapon.SetAttack(true, false);
            }
            else if (right)
            {
                d_Weapon.SetAttack(true, true);
            }

            //pick terrain
            s.StartBox = Box3D.Create(0, 0, 0, BitTools.NextPowerOfTwo(Math.Max(d_Map.MapSizeX, Math.Max(d_Map.MapSizeY, d_Map.MapSizeZ))));
            List<BlockPosSide> pick2 = new List<BlockPosSide>(s.LineIntersection(IsBlockEmpty_.Create(this), GetBlockHeight_.Create(this), pick));
            
            pick2.Sort((a, b) => { return (FloatArrayToVector3(a.blockPos) - ray_start_point).Length.CompareTo((FloatArrayToVector3(b.blockPos) - ray_start_point).Length); });

            if (overheadcamera && pick2.Count > 0 && left)
            {
                //if not picked any object, and mouse button is pressed, then walk to destination.
                playerdestination = Vector3Ref.Create(pick2[0].blockPos[0], pick2[0].blockPos[1], pick2[0].blockPos[2]);
            }
            bool pickdistanceok = pick2.Count > 0 && (FloatArrayToVector3(pick2[0].blockPos) - ToVector3(player.playerposition)).Length <= pick_distance;
            bool playertileempty = IsTileEmptyForPhysics(
                        (int)(player.playerposition.X),
                        (int)(player.playerposition.Z),
                        (int)(player.playerposition.Y + (one / 2)));
            bool playertileemptyclose = IsTileEmptyForPhysicsClose(
                        (int)(player.playerposition.X),
                        (int)(player.playerposition.Z),
                        (int)(player.playerposition.Y + (one / 2)));
            BlockPosSide pick0 = new BlockPosSide();
            if (pick2.Count > 0 &&
                ((pickdistanceok && (playertileempty || (playertileemptyclose)))
                || overheadcamera)
                )
            {
                SelectedBlockPositionX = game.platform.FloatToInt(pick2[0].Current()[0]);
                SelectedBlockPositionY = game.platform.FloatToInt(pick2[0].Current()[1]);
                SelectedBlockPositionZ = game.platform.FloatToInt(pick2[0].Current()[2]);
                pick0 = pick2[0];
            }
            else
            {
                SelectedBlockPositionX = -1;
                SelectedBlockPositionY = -1;
                SelectedBlockPositionZ = -1;
                pick0.blockPos = new float[3];
                pick0.blockPos[0] = -1;
                pick0.blockPos[1] = -1;
                pick0.blockPos[2] = -1;
            }
            if (FreeMouse)
            {
                if (pick2.Count > 0)
                {
                    OnPick(pick0);
                }
                return;
            }
            var ntile = FloatArrayToVector3(pick0.Current());
            if (IsUsableBlock(d_Map.GetBlock((int)ntile.X, (int)ntile.Z, (int)ntile.Y)))
            {
                currentAttackedBlock = Vector3IntRef.Create((int)ntile.X, (int)ntile.Z, (int)ntile.Y);
            }
            if ((one * (game.platform.TimeMillisecondsFromStart() - lastbuildMilliseconds) / 1000) >= BuildDelay
                || IsNextShot)
            {
                if (left && d_Inventory.RightHand[ActiveMaterial] == null)
                {
                    Packet_ClientHealth p = new Packet_ClientHealth { CurrentHealth = (int)(2 + rnd.NextDouble() * 4) };
                    byte[] packet = Serialize(new Packet_Client() { Id = Packet_ClientIdEnum.MonsterHit, Health = p }, packetLen);
                    SendPacket(packet, packetLen.value);
                }
                if (left && !fastclicking)
                {
                    //todo animation
                    fastclicking = false;
                }
                if ((left || right || middle) && (!isgrenade))
                {
                    lastbuildMilliseconds = game.platform.TimeMillisecondsFromStart();
                }
                if (isgrenade && mouseleftdeclick)
                {
                    lastbuildMilliseconds = game.platform.TimeMillisecondsFromStart();
                }
                if (reloadstartMilliseconds != 0)
                {
                    goto end;
                }
                if (ispistolshoot)
                {
                    if ((!(LoadedAmmo[item.BlockId] > 0))
                        || (!(TotalAmmo[item.BlockId] > 0)))
                    {
                        AudioPlay("Dry Fire Gun-SoundBible.com-2053652037.ogg");
                        goto end;
                    }
                }
                if (ispistolshoot)
                {
                    Vector3 to = FloatArrayToVector3(pick.End);
                    if (pick2.Count > 0)
                    {
                        to = FloatArrayToVector3(pick2[0].blockPos);
                    }

                    Packet_ClientShot shot = new Packet_ClientShot();
                    shot.FromX = SerializeFloat(FloatArrayToVector3(pick.Start).X);
                    shot.FromY = SerializeFloat(FloatArrayToVector3(pick.Start).Y);
                    shot.FromZ = SerializeFloat(FloatArrayToVector3(pick.Start).Z);
                    shot.ToX = SerializeFloat(to.X);
                    shot.ToY = SerializeFloat(to.Y);
                    shot.ToZ = SerializeFloat(to.Z);
                    shot.HitPlayer = -1;

                    for(int i = 0; i < playersCount; i++)
                    {
                        if (players[i] == null)
                        {
                            continue;
                        }
                        Player p_ = players[i];
                        if (!p_.PositionLoaded)
                        {
                            continue;
                        }
                        Vector3 feetpos = new Vector3(p_.PositionX, p_.PositionY, p_.PositionZ);
                        //var p = PlayerPositionSpawn;
                        Box3D bodybox = new Box3D();
                        float headsize = (p_.ModelHeight - p_.EyeHeight) * 2; //0.4f;
                        float h = p_.ModelHeight - headsize;
                        float r = 0.35f;

                        bodybox.AddPoint(feetpos.X - r, feetpos.Y + 0, feetpos.Z - r);
                        bodybox.AddPoint(feetpos.X - r, feetpos.Y + 0, feetpos.Z + r);
                        bodybox.AddPoint(feetpos.X + r, feetpos.Y + 0, feetpos.Z - r);
                        bodybox.AddPoint(feetpos.X + r, feetpos.Y + 0, feetpos.Z + r);

                        bodybox.AddPoint(feetpos.X - r, feetpos.Y + h, feetpos.Z - r);
                        bodybox.AddPoint(feetpos.X - r, feetpos.Y + h, feetpos.Z + r);
                        bodybox.AddPoint(feetpos.X + r, feetpos.Y + h, feetpos.Z - r);
                        bodybox.AddPoint(feetpos.X + r, feetpos.Y + h, feetpos.Z + r);

                        Box3D headbox = new Box3D();

                        headbox.AddPoint(feetpos.X - r, feetpos.Y + h, feetpos.Z - r);
                        headbox.AddPoint(feetpos.X - r, feetpos.Y + h, feetpos.Z + r);
                        headbox.AddPoint(feetpos.X + r, feetpos.Y + h, feetpos.Z - r);
                        headbox.AddPoint(feetpos.X + r, feetpos.Y + h, feetpos.Z + r);

                        headbox.AddPoint(feetpos.X - r, feetpos.Y + h + headsize, feetpos.Z - r);
                        headbox.AddPoint(feetpos.X - r, feetpos.Y + h + headsize, feetpos.Z + r);
                        headbox.AddPoint(feetpos.X + r, feetpos.Y + h + headsize, feetpos.Z - r);
                        headbox.AddPoint(feetpos.X + r, feetpos.Y + h + headsize, feetpos.Z + r);

                        float[] p;
                        Vector3 localeyepos = LocalPlayerPosition + new Vector3(0, players[LocalPlayerId].ModelHeight, 0);
                        if ((p = Intersection.CheckLineBoxExact(pick, headbox)) != null)
                        {
                            //do not allow to shoot through terrain
                            if (pick2.Count == 0 || ((FloatArrayToVector3(pick2[0].blockPos) - localeyepos).Length > (FloatArrayToVector3(p) - localeyepos).Length))
                            {
                                if (!isgrenade)
                                {
                                    Entity entity = new Entity();
                                    Sprite sprite = new Sprite();
                                    sprite.positionX = p[0];
                                    sprite.positionY = p[1];
                                    sprite.positionZ = p[2];
                                    sprite.image = "blood.png";
                                    entity.sprite = sprite;
                                    entity.expires = Expires.Create(one * 2 / 10);
                                    EntityAdd(entity);
                                }
                                shot.HitPlayer = i;
                                shot.IsHitHead = 1;
                            }
                        }
                        else if ((p = Intersection.CheckLineBoxExact(pick, bodybox)) != null)
                        {
                            //do not allow to shoot through terrain
                            if (pick2.Count == 0 || ((FloatArrayToVector3(pick2[0].blockPos) - localeyepos).Length > (FloatArrayToVector3(p) - localeyepos).Length))
                            {
                                if (!isgrenade)
                                {
                                    Entity entity = new Entity();
                                    Sprite sprite = new Sprite();
                                    sprite.positionX = p[0];
                                    sprite.positionY = p[1];
                                    sprite.positionZ = p[2];
                                    sprite.image = "blood.png";
                                    entity.sprite = sprite;
                                    entity.expires = Expires.Create(one * 2 / 10);
                                    EntityAdd(entity);
                                }
                                shot.HitPlayer = i;
                                shot.IsHitHead = 0;
                            }
                        }
                    }
                    shot.WeaponBlock = item.BlockId;
                    LoadedAmmo[item.BlockId] = LoadedAmmo[item.BlockId] - 1;
                    TotalAmmo[item.BlockId] = TotalAmmo[item.BlockId] - 1;
                    float projectilespeed = DeserializeFloat(game.blocktypes[item.BlockId].ProjectileSpeedFloat);
                    if (projectilespeed == 0)
                    {
                        {
                            Entity entity = CreateBulletEntity(
                              pick.Start[0], pick.Start[1], pick.Start[2],
                              to.X, to.Y, to.Z, 150);
                            EntityAdd(entity);
                        }
                    }
                    else
                    {
                        Vector3 v = to - FloatArrayToVector3(pick.Start);
                        v.Normalize();
                        v *= projectilespeed;
                        shot.ExplodesAfter = SerializeFloat(grenadetime - wait);

                        {
                            Entity grenadeEntity = new Entity();

                            Sprite sprite = new Sprite();
                            sprite.image = "ChemicalGreen.png";
                            sprite.size = 14;
                            sprite.animationcount = 0;
                            sprite.positionX = pick.Start[0];
                            sprite.positionY = pick.Start[1];
                            sprite.positionZ = pick.Start[2];
                            grenadeEntity.sprite = sprite;
                            
                            Grenade_ projectile = new Grenade_();
                            projectile.velocityX = v.X;
                            projectile.velocityY = v.Y;
                            projectile.velocityZ = v.Z;
                            projectile.block = item.BlockId;

                            grenadeEntity.expires = Expires.Create(grenadetime - wait);
                            
                            grenadeEntity.grenade = projectile;
                            EntityAdd(grenadeEntity);
                        }
                    }
                    SendPacketClient(new Packet_Client() { Id = Packet_ClientIdEnum.Shot, Shot = shot });

                    if (game.blocktypes[item.BlockId].Sounds.ShootEnd.Length > 0)
                    {
                        pistolcycle = rnd.Next(game.blocktypes[item.BlockId].Sounds.ShootEnd.Length);
                        AudioPlay(game.blocktypes[item.BlockId].Sounds.ShootEnd[pistolcycle] + ".ogg");
                    }

                    bulletsshot++;
                    if (bulletsshot < DeserializeFloat(game.blocktypes[item.BlockId].BulletsPerShotFloat))
                    {
                        IsNextShot = true;
                        goto NextBullet;
                    }

                    //recoil
                    player.playerorientation.X -= (float)rnd.NextDouble() * CurrentRecoil;
                    player.playerorientation.Y += (float)rnd.NextDouble() * CurrentRecoil * 2 - CurrentRecoil;

                    goto end;
                }
                if (ispistol && right)
                {
                    goto end;
                }
                if (pick2.Count > 0)
                {
                    if (middle)
                    {
                        var newtile = FloatArrayToVector3(pick0.Current());
                        if (d_Map.IsValidPos((int)newtile.X, (int)newtile.Z, (int)newtile.Y))
                        {
                            int clonesource = d_Map.GetBlock((int)newtile.X, (int)newtile.Z, (int)newtile.Y);
                            int clonesource2 = (int)d_Data.WhenPlayerPlacesGetsConvertedTo()[(int)clonesource];
                            //find this block in another right hand.
                            for (int i = 0; i < 10; i++)
                            {
                                if (d_Inventory.RightHand[i] != null
                                    && d_Inventory.RightHand[i].ItemClass == Packet_ItemClassEnum.Block
                                    && (int)d_Inventory.RightHand[i].BlockId == clonesource2)
                                {
                                    ActiveMaterial = i;
                                    goto done;
                                }
                            }
                            IntRef freehand = d_InventoryUtil.FreeHand(ActiveMaterial);
                            //find this block in inventory.
                            foreach (var k in d_Inventory.Items)
                            {
                                if (k == null)
                                {
                                    continue;
                                }
                                if (k.Value_.ItemClass == Packet_ItemClassEnum.Block
                                    && k.Value_.BlockId == clonesource2)
                                {
                                    //free hand
                                    if (freehand != null)
                                    {
                                        d_InventoryController.WearItem(
                                            InventoryPositionMainArea(k.X, k.Y),
                                            InventoryPositionMaterialSelector(freehand.value));
                                        goto done;
                                    }
                                    //try to replace current slot
                                    if (d_Inventory.RightHand[ActiveMaterial] != null
                                        && d_Inventory.RightHand[ActiveMaterial].ItemClass == Packet_ItemClassEnum.Block)
                                    {
                                        d_InventoryController.MoveToInventory(
                                            InventoryPositionMaterialSelector(ActiveMaterial));
                                        d_InventoryController.WearItem(
                                            InventoryPositionMainArea(k.X, k.Y),
                                            InventoryPositionMaterialSelector(ActiveMaterial));
                                    }
                                }
                            }
                        done:
                            string[] sound = d_Data.CloneSound()[clonesource];
                            if (sound != null && sound.Length > 0)
                            {
                                AudioPlay(sound[0]); //todo sound cycle
                            }
                        }
                    }
                    if (left || right)
                    {
                        BlockPosSide tile = pick0;
                        Vector3 newtile = right ? FloatArrayToVector3(tile.Translated()) : FloatArrayToVector3(tile.Current());
                        if (d_Map.IsValidPos((int)newtile.X, (int)newtile.Z, (int)newtile.Y))
                        {
                            Console.WriteLine(". newtile:" + newtile + " type: " + d_Map.GetBlock((int)newtile.X, (int)newtile.Z, (int)newtile.Y));
                            if (FloatArrayToVector3(pick0.blockPos) != new Vector3(-1, -1, -1))
                            {
                                int blocktype;
                                if (left) { blocktype = d_Map.GetBlock((int)newtile.X, (int)newtile.Z, (int)newtile.Y); }
                                else { blocktype = ((BlockInHand() == null) ? 1 : BlockInHand().value); }
                                if (left && blocktype == d_Data.BlockIdAdminium()) { goto end; }
                                string[] sound = left ? d_Data.BreakSound()[blocktype] : d_Data.BuildSound()[blocktype];
                                if (sound != null && sound.Length > 0)
                                {
                                    AudioPlay(sound[0]); //todo sound cycle
                                }
                            }
                            //normal attack
                            if (!right)
                            {
                                //attack
                                var pos = new Vector3i((int)newtile.X, (int)newtile.Z, (int)newtile.Y);
                                currentAttackedBlock = Vector3IntRef.Create(pos.x, pos.y, pos.z);
                                if (!blockHealth.ContainsKey(pos.x, pos.y, pos.z))
                                {
                                    blockHealth.Set(pos.x, pos.y, pos.z, GetCurrentBlockHealth(pos.x, pos.y, pos.z));
                                }
                                blockHealth.Set(pos.x, pos.y, pos.z, blockHealth.Get(pos.x, pos.y, pos.z) - WeaponAttackStrength());
                                float health = GetCurrentBlockHealth(pos.x, pos.y, pos.z);
                                if (health <= 0)
                                {
                                    if (currentAttackedBlock != null)
                                    {
                                        blockHealth.Remove(pos.x, pos.y, pos.z);
                                    }
                                    currentAttackedBlock = null;
                                    goto broken;
                                }
                                goto end;
                            }
                            if (!right)
                            {
                                particleEffectBlockBreak.StartParticleEffect(newtile.X, newtile.Y, newtile.Z);//must be before deletion - gets ground type.
                            }
                            if (!d_Map.IsValidPos((int)newtile.X, (int)newtile.Z, (int)newtile.Y))
                            {
                                throw new Exception();
                            }
                        broken:
                            OnPick(new Vector3(newtile.X, newtile.Z, newtile.Y),
                                new Vector3((int)tile.Current()[0], (int)tile.Current()[2], (int)tile.Current()[1]),
                                tile.collisionPos,
                                right);
                            //network.SendSetBlock(new Vector3((int)newtile.X, (int)newtile.Z, (int)newtile.Y),
                            //    right ? BlockSetMode.Create : BlockSetMode.Destroy, (byte)MaterialSlots[activematerial]);
                        }
                    }
                }
            }
        end:
            fastclicking = false;
            if ((!(left || right || middle)) && (!ispistol))
            {
                lastbuildMilliseconds = 0;
                fastclicking = true;
            }
        }

        Vector3 FloatArrayToVector3(float[] v)
        {
            return new Vector3(v[0], v[1], v[2]);
        }

        float[] Vector3ToFloatArray(Vector3 v)
        {
            return Vec3.FromValues(v.X, v.Y, v.Z);
        }

        Matrix4 ToMatrix4(float[] mvMatrix)
        {
            return new Matrix4(
                mvMatrix[0],
                mvMatrix[1],
                mvMatrix[2],
                mvMatrix[3],
                mvMatrix[4],
                mvMatrix[5],
                mvMatrix[6],
                mvMatrix[7],
                mvMatrix[8],
                mvMatrix[9],
                mvMatrix[10],
                mvMatrix[11],
                mvMatrix[12],
                mvMatrix[13],
                mvMatrix[14],
                mvMatrix[15]);
        }

        public const float RailHeight = 0.3f;

        private void OnPick(BlockPosSide pick0)
        {
            //playerdestination = pick0.pos;
        }
        Vector3 ToMapPos(Vector3 a)
        {
            return new Vector3((int)a.X, (int)a.Z, (int)a.Y);
        }
        bool fastclicking = false;

        //double currentTime = 0;
        double accumulator = 0;
        double t = 0;
        //Vector3 oldplayerposition;

        public float CharacterModelHeight { get { return Players[LocalPlayerId].ModelHeight; } }
        public Color clearcolor = Color.FromArgb(171, 202, 228);
        public Stopwatch framestopwatch;
        public void OnRenderFrame(FrameEventArgs e)
        {
            framestopwatch = new Stopwatch();
            framestopwatch.Start();
            terrainRenderer.UpdateTerrain();
            GL.ClearColor(guistate == GuiState.MapLoading ? Color.Black : clearcolor);

            //Sleep is required in Mono for running the terrain background thread.
            if (IsMono)
            {
                Application.DoEvents();
                Thread.Sleep(0);
            }
            var deltaTime = e.Time;

            accumulator += deltaTime;
            double dt = 1d / 75;

            while (accumulator >= dt)
            {
                FrameTick(new FrameEventArgs(dt));
                t += dt;
                accumulator -= dt;
            }

            if (guistate == GuiState.MapLoading) { goto draw2d; }

            if (ENABLE_LAG == 2) { Thread.SpinWait(20 * 1000 * 1000); }
            //..base.OnRenderFrame(e);


            if (d_HudInventory.IsMouseOverCells() && guistate == GuiState.Inventory)
            {
                int delta = Mouse.WheelDelta;
                if (delta > 0)
                {
                    d_HudInventory.ScrollUp();
                }
                if (delta < 0)
                {
                    d_HudInventory.ScrollDown();
                }
            }
            else if (!keyboardstate[GetKey(OpenTK.Input.Key.LControl)])
            {
                ActiveMaterial -= Mouse.WheelDelta;
                ActiveMaterial = ActiveMaterial % 10;
                while (ActiveMaterial < 0)
                {
                    ActiveMaterial += 10;
                }
            }
            SetAmbientLight(terraincolor);
            //const float alpha = accumulator / dt;
            //Vector3 currentPlayerPosition = currentState * alpha + previousState * (1.0f - alpha);
            UpdateTitleFps(e);

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GL.BindTexture(TextureTarget.Texture2D, game.d_TerrainTextures.terrainTexture());

            GLMatrixModeModelView();

            Matrix4 camera;
            if (overheadcamera)
            {
                camera = OverheadCamera();
            }
            else
            {
                camera = FppCamera();
            }
            GLLoadMatrix(camera);
            game.CameraMatrix.lastmvmatrix = Matrix4ToFloat(camera);

            if (BeforeRenderFrame != null) { BeforeRenderFrame(this, new EventArgs()); }

            bool drawgame = guistate != GuiState.MapLoading;
            if (drawgame)
            {
                GL.Disable(EnableCap.Fog);
                DrawSkySphere();
                if (d_Config3d.viewdistance < 512)
                {
                    SetFog();
                }
                d_SunMoonRenderer.Draw((float)e.Time);
                
                DrawPlayers((float)e.Time);
                terrainRenderer.DrawTerrain();
                DrawPlayerNames();
                particleEffectBlockBreak.Draw((float)e.Time);
                if (ENABLE_DRAW2D)
                {
                    game.DrawLinesAroundSelectedBlock(SelectedBlockPositionX,
                        SelectedBlockPositionY, SelectedBlockPositionZ);
                }
                DrawSprites();
                UpdateBullets((float)e.Time);
                if (ENABLE_DRAW_TEST_CHARACTER)
                {
                    d_CharacterRenderer.DrawCharacter(a, PlayerPositionSpawn.X,
                        PlayerPositionSpawn.Y, PlayerPositionSpawn.Z,
                        0, 0, true, (float)dt, GetPlayerTexture(this.LocalPlayerId),
                        new AnimationHint(), new float());
                }
                foreach (IModelToDraw m in Models)
                {
                    if (m.Id() == selectedmodelid)
                    {
                        //GL.Color3(Color.Red);
                    }
                    m.Draw((float)e.Time);
                    //GL.Color3(Color.White);
                    
                    //GL.Begin(BeginMode.Triangles);
                    //foreach (var tri in m.TrianglesForPicking)
                    //{
                    //    GL.Vertex3(tri.PointA);
                    //    GL.Vertex3(tri.PointB);
                    //    GL.Vertex3(tri.PointC);
                    //}
                    //GL.End();
                    
                }
                if ((!ENABLE_TPP_VIEW) && ENABLE_DRAW2D)
                {
                    Packet_Item item = d_Inventory.RightHand[ActiveMaterial];
                    string img = null;
                    if (item != null)
                    {
                        img = game.blocktypes[item.BlockId].Handimage;
                        if (IronSights)
                        {
                            img = game.blocktypes[item.BlockId].IronSightsImage;
                        }
                    }
                    if (img == null)
                    {
                        d_Weapon.DrawWeapon((float)e.Time);
                    }
                    else
                    {
                        OrthoMode(Width, Height);
                        Draw2dBitmapFile_(img, Width / 2, Height - 512, 512, 512);
                        PerspectiveMode();
                    }
                }
            }
        draw2d:
            SetAmbientLight(Color.White);
            Draw2d();

            for (int i = 0; i < clientmodsCount; i++)
            {
                NewFrameEventArgs args_ = new NewFrameEventArgs();
                args_.SetDt((float)e.Time);
                clientmods[i].OnNewFrame(args_);
            }

            //OnResize(new EventArgs());
            d_GlWindow.SwapBuffers();
            mouseleftclick = mouserightclick = false;
            mouseleftdeclick = mouserightdeclick = false;
            if (!startedconnecting) { startedconnecting = true; Connect(); }
        }

        float[] Matrix4ToFloat(Matrix4 m)
        {
            float[] m2 = Mat4.Create();
            m2[0] = m.M11;
            m2[1] = m.M12;
            m2[2] = m.M13;
            m2[3] = m.M14;
            m2[4] = m.M21;
            m2[5] = m.M22;
            m2[6] = m.M23;
            m2[7] = m.M24;
            m2[8] = m.M31;
            m2[9] = m.M32;
            m2[10] = m.M33;
            m2[11] = m.M34;
            m2[12] = m.M41;
            m2[13] = m.M42;
            m2[14] = m.M43;
            m2[15] = m.M44;
            return m2;
        }

        public void GLLoadMatrix(Matrix4 m)
        {
            game.GLLoadMatrix(Matrix4ToFloat(m));
        }

        private int GetTexture(string s)
        {
            if (!textures1.ContainsKey(s))
            {
                textures1[s] = LoadTexture(d_GetFile.GetFile(s));
            }
            return textures1[s];
        }

        Dictionary<string, int> textures1 = new Dictionary<string, int>();
        bool startedconnecting;
        private void SetFog()
        {
            //Density for linear fog
            //float density = 0.3f;
            // use this density for exp2 fog (0.0045f was a bit too much at close ranges)
            float density = 0.0025f;
            //float[] fogColor = new[] { 1f, 1f, 1f, 1.0f };
            float[] fogColor;
            if (SkySphereNight && (!terrainRenderer.shadowssimple))
            {
                fogColor = new[] { 0f, 0f, 0f, 1.0f };
            }
            else
            {
                fogColor = new[] { (float)clearcolor.R / 256, (float)clearcolor.G / 256, (float)clearcolor.B / 256, (float)clearcolor.A / 256 };
            }
            GL.Enable(EnableCap.Fog);
            GL.Hint(HintTarget.FogHint, HintMode.Nicest);
            //old linear fog
            //GL.Fog(FogParameter.FogMode, (int)FogMode.Linear);
            // looks better
            GL.Fog(FogParameter.FogMode, (int)FogMode.Exp2);
            GL.Fog(FogParameter.FogColor, fogColor);
            GL.Fog(FogParameter.FogDensity, density);
            //Unfortunately not used for exp/exp2 fog
            /*float fogsize = 10;
            if (d_Config3d.viewdistance <= 64)
            {
                fogsize = 5;
            }
            //float fogstart = d_Config3d.viewdistance - fogsize + 200;
			float fogstart = d_Config3d.viewdistance - fogsize;
            GL.Fog(FogParameter.FogStart, fogstart);
            GL.Fog(FogParameter.FogEnd, fogstart + fogsize);*/
        }
        public event EventHandler BeforeRenderFrame;
        Dictionary<string, int> playertextures = new Dictionary<string, int>();
        Dictionary<int, int> monstertextures = new Dictionary<int, int>();
        Dictionary<string, int> diskplayertextures = new Dictionary<string, int>();
        private int GetPlayerTexture(int playerid)
        {
            if (playertexturedefault == -1)
            {
                playertexturedefault = LoadTexture(Game.playertexturedefaultfilename);
            }
            Player player = this.players[playerid];
            if (player.Type == PlayerType.Monster)
            {
                if (!monstertextures.ContainsKey(player.MonsterType))
                {
                    string skinfile = d_DataMonsters.MonsterSkin[this.players[playerid].MonsterType];
                    using (Bitmap bmp = new Bitmap(d_GetFile.GetFile(skinfile)))
                    {
                        monstertextures[player.MonsterType] = d_The3d.LoadTexture(bmp);
                    }
                }
                return monstertextures[player.MonsterType];
            }
            if (!string.IsNullOrEmpty(player.Texture))
            {
                if (!diskplayertextures.ContainsKey(player.Texture))
                {
                    try
                    {
                        diskplayertextures[player.Texture] = LoadTexture(d_GetFile.GetFile(player.Texture));
                    }
                    catch
                    {
                        diskplayertextures[player.Texture] = playertexturedefault; // invalid
                    }
                }
                return diskplayertextures[player.Texture];
            }
            List<string> players_ = new List<string>();
            for (int i = 0; i < playersCount; i++)
            {
                if (players[i] == null)
                {
                    continue;
                }
                Player p = players[i];
                if (!p.Name.Equals("Local", StringComparison.InvariantCultureIgnoreCase))
                {
                    players_.Add(p.Name);
                }
            }
            playerskindownloader.Update(players_.ToArray(), playertextures, playertexturedefault);
            string playername;
            if (playerid == this.LocalPlayerId)
            {
                playername = connectdata.Username;
            }
            else
            {
                playername = d_Clients.Players[playerid].Name;
            }
            if (playername == null)
            {
                playername = "";
            }
            if (playertextures.ContainsKey(playername))
            {
                return playertextures[playername];
            }
            return playertexturedefault;
        }
        public PlayerSkinDownloader playerskindownloader { get; set; }
        NetworkInterpolation interpolation = new NetworkInterpolation();
        Dictionary<int, PlayerDrawInfo> playerdrawinfo = new Dictionary<int, PlayerDrawInfo>();
        private void DrawPlayers(float dt)
        {
            totaltimeMilliseconds = platform.TimeMillisecondsFromStart();
            for (int i = 0; i < playersCount; i++)
            {
                if (players[i] == null)
                {
                    continue;
                }
                Player p = players[i];
                if (i == this.LocalPlayerId)
                {
                    continue;
                }
                if (!p.PositionLoaded)
                {
                    continue;
                }
                if (!playerdrawinfo.ContainsKey(i))
                {
                    playerdrawinfo[i] = new PlayerDrawInfo();
                    NetworkInterpolation n = new NetworkInterpolation();
                    PlayerInterpolate playerInterpolate = new PlayerInterpolate();
                    playerInterpolate.platform = platform;
                    n.req = playerInterpolate;
                    n.DELAYMILLISECONDS = 500;
                    n.EXTRAPOLATE = false;
                    n.EXTRAPOLATION_TIMEMILLISECONDS = 300;
                    playerdrawinfo[i].interpolation = n;
                }
                playerdrawinfo[i].interpolation.DELAYMILLISECONDS = Game.MaxInt(100, ServerInfo.ServerPing.RoundtripTimeTotalMilliseconds());
                PlayerDrawInfo info = playerdrawinfo[i];
                Vector3 realpos = new Vector3(p.PositionX, p.PositionY, p.PositionZ);
                bool redraw = false;
                if (totaltimeMilliseconds - lastdrawplayersMilliseconds >= 100)
                {
                    //redraw = true;
                    lastdrawplayersMilliseconds = totaltimeMilliseconds;
                }
                if ((!Vec3Equal(realpos.X, realpos.Y, realpos.Z,
                                info.lastrealposX, info.lastrealposY, info.lastrealposZ))
                    || p.Heading != info.lastrealheading
                    || p.Pitch != info.lastrealpitch
                    || redraw)
                {
                    PlayerInterpolationState state = new PlayerInterpolationState();
                    state.positionX = realpos.X;
                    state.positionY = realpos.Y;
                    state.positionZ = realpos.Z;
                    state.heading = p.Heading;
                    state.pitch = p.Pitch;
                    info.interpolation.AddNetworkPacket(state, totaltimeMilliseconds);
                }
                var curstate = ((PlayerInterpolationState)info.interpolation.InterpolatedState(totaltimeMilliseconds));
                if (curstate == null)
                {
                    curstate = new PlayerInterpolationState();
                }
                //do not interpolate player position if player is controlled by game world
                if (EnablePlayerUpdatePositionContainsKey(i) && !EnablePlayerUpdatePosition(i))
                {
                    curstate.positionX = p.PositionX;
                    curstate.positionY = p.PositionY;
                    curstate.positionZ = p.PositionZ;
                }
                Vector3 curpos = new Vector3(curstate.positionX, curstate.positionY, curstate.positionZ);
                info.velocityX = curpos.X - info.lastcurposX;
                info.velocityY = curpos.Y - info.lastcurposY;
                info.velocityZ = curpos.Z - info.lastcurposZ;
                float playerspeed = (Length(info.velocityX, info.velocityY, info.velocityZ) / dt) * 0.04f;
                bool moves = (!Vec3Equal(curpos.X, curpos.Y, curpos.Z, info.lastcurposX, info.lastcurposY, info.lastcurposZ));
                info.lastcurposX = curpos.X;
                info.lastcurposY = curpos.Y;
                info.lastcurposZ = curpos.Z;
                info.lastrealposX = realpos.X;
                info.lastrealposY = realpos.Y;
                info.lastrealposZ = realpos.Z;
                info.lastrealheading = p.Heading;
                info.lastrealpitch = p.Pitch;
                if (!d_FrustumCulling.SphereInFrustum(curpos.X, curpos.Y, curpos.Z, 3))
                {
                    continue;
                }
                if (!terrainRenderer.IsChunkRendered((int)curpos.X / chunksize, (int)curpos.Z / chunksize, (int)curpos.Y / chunksize))
                {
                    continue;
                }
                float shadow = (float)d_Shadows.MaybeGetLight((int)curpos.X, (int)curpos.Z, (int)curpos.Y) / d_Shadows.maxlight;
                GL.Color3(shadow, shadow, shadow);
                info.anim.light = shadow;
                Vector3 FeetPos = curpos;
                var animHint = d_Clients.Players[i].AnimationHint_;
                if (p.Type == PlayerType.Player)
                {
                    ICharacterRenderer r = GetCharacterRenderer(p.Model);
                    r.SetAnimation("walk");
                    r.DrawCharacter(info.anim, FeetPos.X, FeetPos.Y, FeetPos.Z, (byte)(-curstate.heading - 256 / 4), curstate.pitch, moves, dt, GetPlayerTexture(i), animHint, playerspeed);
                    //DrawCharacter(info.anim, FeetPos,
                    //    curstate.heading, curstate.pitch, moves, dt, GetPlayerTexture(k.Key), animHint);
                }
                else
                {
                    //fix crash on monster spawn
                    ICharacterRenderer r = GetCharacterRenderer(d_DataMonsters.MonsterCode[p.MonsterType]);
                    //var r = MonsterRenderers[d_DataMonsters.MonsterCode[k.Value.MonsterType]];
                    r.SetAnimation("walk");
                    //curpos += new Vector3(0, -CharacterPhysics.walldistance, 0); //todos
                    r.DrawCharacter(info.anim, curpos.X, curpos.Y, curpos.Z,
                        (byte)(-curstate.heading - 256 / 4), curstate.pitch,
                        moves, dt, GetPlayerTexture(i), animHint, playerspeed);
                }
                GL.Color3(1f, 1f, 1f);
            }
            if (ENABLE_TPP_VIEW)
            {
                Vector3 velocity = lastlocalplayerpos - LocalPlayerPosition;
                bool moves = lastlocalplayerpos != LocalPlayerPosition; //bool moves = velocity.Length > 0.08;
                float shadow = (float)d_Shadows.MaybeGetLight(
                    (int)LocalPlayerPosition.X,
                    (int)LocalPlayerPosition.Z,
                    (int)LocalPlayerPosition.Y)
                    / d_Shadows.maxlight;
                GL.Color3(shadow, shadow, shadow);
                localplayeranim.light = shadow;
                var r = GetCharacterRenderer(d_Clients.Players[LocalPlayerId].Model);
                r.SetAnimation("walk");
                Vector3Ref playerspeed = Vector3Ref.Create(playervelocity.X / 60, playervelocity.Y / 60, playervelocity.Z / 60);
                float playerspeedf = playerspeed.Length() * 1.5f;
                r.DrawCharacter
                    (localplayeranim, LocalPlayerPosition.X, LocalPlayerPosition.Y,
                    LocalPlayerPosition.Z,
                    (byte)(-NetworkHelper.HeadingByte(LocalPlayerOrientation) - 256 / 4),
                    NetworkHelper.PitchByte(LocalPlayerOrientation),
                    moves, dt, GetPlayerTexture(this.LocalPlayerId), localplayeranimationhint, playerspeedf);
                lastlocalplayerpos = LocalPlayerPosition;
                GL.Color3(1f, 1f, 1f);
            }
        }

        bool Vec3Equal(float ax, float ay, float az, float bx, float by, float bz)
        {
            return ax == bx && ay == by && az == bz;
        }

        float Length(float x, float y, float z)
        {
            return platform.MathSqrt(x * x + y * y + z * z);
        }

        ICharacterRenderer GetCharacterRenderer(string modelfilename)
        {
            if (!MonsterRenderers.ContainsKey(modelfilename))
            {
                try
                {
                    string[] lines = MyStream.ReadAllLines(d_GetFile.GetFile(modelfilename));
                    var renderer = new CharacterRendererMonsterCode();
                    renderer.game = this.game;
                    renderer.Load(lines, lines.Length);
                    MonsterRenderers[modelfilename] = renderer;
                }
                catch
                {
                    MonsterRenderers[modelfilename] = GetCharacterRenderer("player.txt"); // todo invalid.txt
                }
            }
            return MonsterRenderers[modelfilename];
        }
        Vector3 lastlocalplayerpos;
        AnimationState localplayeranim = new AnimationState();
        Kamera overheadcameraK = new Kamera();
        Matrix4 FppCamera()
        {
            Vector3Ref forward = new Vector3Ref();
            VectorTool.ToVectorInFixedSystem(0, 0, 1, player.playerorientation.X, player.playerorientation.Y, forward);
            Vector3 cameraEye;
            Vector3 cameraTarget;
            Vector3 playerEye = ToVector3(player.playerposition) + new Vector3(0, CharacterEyesHeight, 0);
            if (!ENABLE_TPP_VIEW)
            {
                cameraEye = playerEye;
                cameraTarget = playerEye + ToVector3(forward);
            }
            else
            {
                cameraEye = playerEye + Vector3.Multiply(ToVector3(forward), -tppcameradistance);
                cameraTarget = playerEye;
                float currentTppcameradistance = tppcameradistance;
                LimitThirdPersonCameraToWalls(ref cameraEye, cameraTarget, ref currentTppcameradistance);
            }
            return Matrix4.LookAt(cameraEye, cameraTarget, up);
        }
        Vector3Ref OverheadCamera_cameraEye;
        Matrix4 OverheadCamera()
        {
            overheadcameraK.GetPosition(game.platform, OverheadCamera_cameraEye);
            Vector3 cameraEye = ToVector3(OverheadCamera_cameraEye);
            Vector3 cameraTarget = ToVector3(overheadcameraK.Center) + new Vector3(0, CharacterEyesHeight, 0);
            float currentOverheadcameradistance = overheadcameradistance;
            LimitThirdPersonCameraToWalls(ref cameraEye, cameraTarget, ref currentOverheadcameradistance);
            return Matrix4.LookAt(cameraEye, cameraTarget, up);
        }
        BlockOctreeSearcher s;
        //Don't allow to look through walls.
        private void LimitThirdPersonCameraToWalls(ref Vector3 eye, Vector3 target, ref float curtppcameradistance)
        {
            var ray_start_point = target;
            var raytarget = eye;

            var pick = new Line3D();
            var raydir = (raytarget - ray_start_point);
            raydir.Normalize();
            raydir = Vector3.Multiply(raydir, tppcameradistance + 1);
            pick.Start = Vector3ToFloatArray(ray_start_point);
            pick.End = Vector3ToFloatArray(ray_start_point + raydir);

            //pick terrain
            s.StartBox = Box3D.Create(0, 0, 0, BitTools.NextPowerOfTwo(Math.Max(d_Map.MapSizeX, Math.Max(d_Map.MapSizeY, d_Map.MapSizeZ))));
            List<BlockPosSide> pick2 = new List<BlockPosSide>(s.LineIntersection(IsBlockEmpty_.Create(this), GetBlockHeight_.Create(this), pick));
            pick2.Sort((a, b) => { return (FloatArrayToVector3(a.blockPos) - ray_start_point).Length.CompareTo((FloatArrayToVector3(b.blockPos) - ray_start_point).Length); });
            if (pick2.Count > 0)
            {
                var pickdistance = (FloatArrayToVector3(pick2[0].blockPos) - target).Length;
                curtppcameradistance = Math.Min(pickdistance - 1, curtppcameradistance);
                if (curtppcameradistance < 0.3f) { curtppcameradistance = 0.3f; }
            }

            Vector3 cameraDirection = target - eye;
            raydir.Normalize();
            eye = target + Vector3.Multiply(raydir, curtppcameradistance);
        }
        Dictionary<string, ICharacterRenderer> MonsterRenderers = new Dictionary<string, ICharacterRenderer>();
        private void DrawMouseCursor()
        {
            Draw2dBitmapFile_(Path.Combine("gui", "mousecursor.png"), mouse_current.X, mouse_current.Y, 32, 32);
        }
        private void Draw2d()
        {
            OrthoMode(Width, Height);
            switch (guistate)
            {
                case GuiState.Normal:
                    {
                        if (!ENABLE_DRAW2D)
                        {
                            if (GuiTyping == TypingState.Typing)
                            {
                                d_HudChat.DrawChatLines(true);
                                d_HudChat.DrawTypingBuffer();
                            }
                            PerspectiveMode();
                            return;
                        }
                        if (cameratype != CameraType.Overhead)
                        {
                            DrawAim();
                        }
                        d_HudInventory.DrawMaterialSelector();
                        DrawPlayerHealth();
                        DrawPlayerOxygen();
                        DrawEnemyHealthBlock();
                        DrawCompass();
                        d_HudChat.DrawChatLines(GuiTyping == TypingState.Typing);
                        if (GuiTyping == TypingState.Typing)
                        {
                            d_HudChat.DrawTypingBuffer();
                        }
                        DrawAmmo();
                        DrawDialogs();
                    }
                    break;
                case GuiState.EscapeMenu:
                    {
                        if (!ENABLE_DRAW2D)
                        {
                            PerspectiveMode();
                            return;
                        }
                        d_HudChat.DrawChatLines(GuiTyping == TypingState.Typing);
                        DrawDialogs();
                        escapeMenu.EscapeMenuDraw();
                    }
                    break;
                case GuiState.Inventory:
                    {
                        DrawDialogs();
                        //d_The3d.ResizeGraphics(Width, Height);
                        //d_The3d.OrthoMode(d_HudInventory.ConstWidth, d_HudInventory.ConstHeight);
                        d_HudInventory.Draw();
                        //d_The3d.PerspectiveMode();
                    }
                    break;
                case GuiState.MapLoading:
                    {
                        MapLoadingDraw();
                    }
                    break;
                case GuiState.CraftingRecipes:
                    {
                        DrawCraftingRecipes();
                    }
                    break;
                case GuiState.ModalDialog:
                    {
                        DrawDialogs();
                    }
                    break;
                default:
                    throw new Exception();
            }
            //d_The3d.OrthoMode(Width, Height);
            if (ENABLE_DRAWPOSITION)
            {
                double heading = (double)NetworkHelper.HeadingByte(LocalPlayerOrientation);
                double pitch = (double)NetworkHelper.PitchByte(LocalPlayerOrientation);
                string postext = "X: " + Math.Floor(player.playerposition.X)
                	+ ",\tY: " + Math.Floor(player.playerposition.Z)
                	+ ",\tZ: " + Math.Floor(player.playerposition.Y)
                	+ "\nHeading: " + Math.Floor(heading)
                	+ "\nPitch: " + Math.Floor(pitch);
                Draw2dText_(postext, 100f, 460f, d_HudChat.ChatFontSize, Color.White);
            }
            if (drawblockinfo)
            {
                DrawBlockInfo();
            }
            if (FreeMouse)
            {
                DrawMouseCursor();
            }
            if (screenshotflash > 0)
            {
                DrawScreenshotFlash();
                screenshotflash--;
            }
            double lagSeconds = one * (game.platform.TimeMillisecondsFromStart() - LastReceivedMilliseconds) / 1000;
            if (lagSeconds >= DISCONNECTED_ICON_AFTER_SECONDS && lagSeconds < 60 * 60 * 24)
            {
                Draw2dBitmapFile_("disconnected.png", Width - 100, 50, 50, 50);
                Draw2dText_(((int)lagSeconds).ToString(), Width - 100, 50 + 50 + 10, 12, Color.White);
                Draw2dText_("Press F6 to reconnect", Width / 2 - 200 / 2, 50, 12, Color.White);
            }
            PerspectiveMode();
        }

        bool ammostarted;

        public int DISCONNECTED_ICON_AFTER_SECONDS = 10;

        private void DrawPlayerNames()
        {
            for (int i = 0; i < playersCount; i++)
            {
                if (players[i] == null)
                {
                    continue;
                }
                int kKey = i;
                Player p = players[i];
                if ((!p.PositionLoaded) ||
                    (kKey == this.LocalPlayerId) || (p.Name == "")
                    || (!playerdrawinfo.ContainsKey(kKey))
                    || (playerdrawinfo[kKey].interpolation == null))
                {
                    continue;
                }
                //todo if picking
                if ((Dist(LocalPlayerPosition.X, LocalPlayerPosition.Y, LocalPlayerPosition.Z, p.PositionX, p.PositionY, p.PositionZ) < 20)
                    || Keyboard[GetKey(OpenTK.Input.Key.AltLeft)] || Keyboard[GetKey(OpenTK.Input.Key.AltRight)])
                {
                    string name = p.Name;
                    var ppos = playerdrawinfo[kKey].interpolation.InterpolatedState(totaltimeMilliseconds);
                    if (ppos != null)
                    {
                        var state = ((PlayerInterpolationState)ppos);
                        Vector3 pos = new Vector3(state.positionX, state.positionY, state.positionZ);
                        float shadow = (float)d_Shadows.MaybeGetLight((int)pos.X, (int)pos.Z, (int)pos.Y) / d_Shadows.maxlight;
                        //do not interpolate player position if player is controlled by game world
                        if (EnablePlayerUpdatePositionContainsKey(kKey) && !EnablePlayerUpdatePosition(kKey))
                        {
                            pos.X = p.PositionX;
                            pos.Y = p.PositionY;
                            pos.Z = p.PositionZ;
                        }
                        GLPushMatrix();
                        GLTranslate(pos.X, pos.Y + CharacterModelHeight + 0.8f, pos.Z);
                        if (p.Type == PlayerType.Monster)
                        {
                            GLTranslate(0, 1f, 0);
                        }
                        GLRotate(-player.playerorientation.Y * 360 / (2 * Game.GetPi()), 0.0f, 1.0f, 0.0f);
                        GLRotate(-player.playerorientation.X * 360 / (2 * Game.GetPi()), 1.0f, 0.0f, 0.0f);
                        GLScale(0.02f, 0.02f, 0.02f);

                        //Color c = Color.FromArgb((int)(shadow * 255), (int)(shadow * 255), (int)(shadow * 255));
                        //Todo: Can't change text color because text has outline anyway.
                        if (p.Type == PlayerType.Monster)
                        {
                            Draw2dTexture_(WhiteTexture(), -26, -11, 52, 12, null, Color.FromArgb(0, Color.Black));
                            Draw2dTexture_(WhiteTexture(), -25, -10, 50 * (p.Health / 20f), 10, null, Color.FromArgb(0, Color.Red));
                        }
                        Draw2dText_(name, -TextSize_(name, 14).Width / 2, 0, 14, Game.ColorFromArgb(255, 255, 255, 255), true);
                        //                        GL.Translate(0, 1, 0);
                        GLPopMatrix();
                    }
                }
            }
        }

        bool drawblockinfo = false;
        void CraftingMouse()
        {
            if (okrecipes == null)
            {
                return;
            }
            int menustartx = xcenter(600);
            int menustarty = ycenter(okrecipes.Count * 80);
            if (mouse_current.Y >= menustarty && mouse_current.Y < menustarty + okrecipes.Count * 80)
            {
                craftingselectedrecipe = (mouse_current.Y - menustarty) / 80;
            }
            else
            {
                //craftingselectedrecipe = -1;
            }
            if (mouseleftclick)
            {
                if (okrecipes.Count != 0)
                {
                    craftingrecipeselected(IntRef.Create(okrecipes[craftingselectedrecipe]));
                }
                mouseleftclick = false;
                GuiStateBackToGame();
            }
        }
        
        Random rnd = new Random();
        void UpdateTitleFps(FrameEventArgs e)
        {
            float elapsed = one * (game.platform.TimeMillisecondsFromStart() - lasttitleupdateMilliseconds) / 1000;
            if (elapsed >= 1)
            {
                lasttitleupdateMilliseconds = game.platform.TimeMillisecondsFromStart();
                int chunkupdates = terrainRenderer.ChunkUpdates();
                PerformanceInfo.Set("chunk updates", string.Format(language.ChunkUpdates(), (chunkupdates - lastchunkupdates)));
                lastchunkupdates = terrainRenderer.ChunkUpdates();
                PerformanceInfo.Set("triangles", string.Format(language.Triangles(), terrainRenderer.TrianglesCount()));
            }
            if (!titleset)
            {
                d_GlWindow.Title = applicationname;
                titleset = true;
            }
        }
        bool titleset = false;
        string applicationname;
        #region ILocalPlayerPosition Members
        public Vector3 LocalPlayerPosition
        {
            get
            {
                if (FollowId() != null)
                {
                    if (FollowId().value == LocalPlayerId)
                    {
                        return ToVector3(player.playerposition);
                    }
                    var curstate = ((PlayerInterpolationState)playerdrawinfo[FollowId().value].interpolation.InterpolatedState(totaltimeMilliseconds));
                    return new Vector3(curstate.positionX, curstate.positionY, curstate.positionZ);
                }
                return ToVector3(player.playerposition);
            }
            set
            {
                if (FollowId() != null)
                {
                    return;
                }
                player.playerposition.X = value.X;
                player.playerposition.Y = value.Y;
                player.playerposition.Z = value.Z;
            }
        }
        public Vector3 LocalPlayerOrientation
        {
            get
            {
                if (FollowId() != null)
                {
                    if (FollowId().value == LocalPlayerId)
                    {
                        return new Vector3(
                            player.playerorientation.X,
                            player.playerorientation.Y,
                            player.playerorientation.Z);
                    }
                    var curstate = ((PlayerInterpolationState)playerdrawinfo[FollowId().value].interpolation.InterpolatedState(totaltimeMilliseconds));
                    return HeadingPitchToOrientation(curstate.heading, curstate.pitch);
                }
                return new Vector3(
                    player.playerorientation.X,
                    player.playerorientation.Y,
                    player.playerorientation.Z);
            }
            set
            {
                player.playerorientation.X = value.X;
                player.playerorientation.Y = value.Y;
                player.playerorientation.Z = value.Z;
            }
        }
        #endregion
        #region ILocalPlayerPosition Members
        public bool Swimming
        {
            get
            {
                var p = LocalPlayerPosition;
                p += new Vector3(0, Players[LocalPlayerId].EyeHeight, 0);
                if (!d_Map.IsValidPos((int)Math.Floor(p.X), (int)Math.Floor(p.Z), (int)Math.Floor(p.Y)))
                {
                    return p.Y < WaterLevel;
                }
                return d_Data.WalkableType1()[d_Map.GetBlock((int)p.X, (int)p.Z, (int)p.Y)] == (int)WalkableType.Fluid;
            }
        }
        public bool WaterSwimming
        {
            get
            {
                var p = LocalPlayerPosition;
                p += new Vector3(0, Players[LocalPlayerId].EyeHeight, 0);
                if (!d_Map.IsValidPos((int)Math.Floor(p.X), (int)Math.Floor(p.Z), (int)Math.Floor(p.Y)))
                {
                    return p.Y < WaterLevel;
                }
                return IsWater(d_Map.GetBlock((int)p.X, (int)p.Z, (int)p.Y));
            }
        }
        public bool LavaSwimming
        {
            get
            {
                var p = LocalPlayerPosition;
                p += new Vector3(0, Players[LocalPlayerId].EyeHeight, 0);
                if (!d_Map.IsValidPos((int)Math.Floor(p.X), (int)Math.Floor(p.Z), (int)Math.Floor(p.Y)))
                {
                    return false;
                }
                return IsLava(d_Map.GetBlock((int)p.X, (int)p.Z, (int)p.Y));
            }
        }

        #endregion
        public float WaterLevel { get { return d_Map.MapSizeZ / 2; } set { } }
        Color terraincolor
        {
            get
            {
                if (WaterSwimming)
                {
                    return Color.FromArgb(255, 78, 95, 140);
                }
                else if (LavaSwimming)
                {
                    return Color.FromArgb(255, 222, 101, 46);
                }
                else
                {
                    return Color.White;
                }
            }
        }
        #region IKeyboard Members
        public OpenTK.Input.KeyboardDevice keyboardstate
        {
            get { return Keyboard; }
        }
        #endregion
        #region IKeyboard Members
        public OpenTK.Input.KeyboardKeyEventArgs keypressed
        {
            get { return keyevent; }
        }
        public OpenTK.Input.KeyboardKeyEventArgs keydepressed
        {
            get { return keyeventup; }
        }
        #endregion
        AnimationHint localplayeranimationhint = new AnimationHint();
        #region IViewport3d Members
        public AnimationHint LocalPlayerAnimationHint
        {
            get { return localplayeranimationhint; }
            set { localplayeranimationhint = value; }
        }
        #endregion
        #region IViewport3d Members
        public Vector3 PickCubePos { get { return new Vector3(SelectedBlockPositionX, SelectedBlockPositionY, SelectedBlockPositionZ); } }
        #endregion
        #region IViewport3d Members
        public string LocalPlayerName { get { return connectdata.Username; } }
        #endregion
        public int Height { get { return d_GlWindow.Height; } }
        public int Width { get { return d_GlWindow.Width; } }
        public OpenTK.Input.KeyboardDevice Keyboard { get { return d_GlWindow.Keyboard; } }
        public OpenTK.Input.MouseDevice Mouse { get { return d_GlWindow.Mouse; } }
        public void Run()
        {
            d_GlWindow.Run();
        }
        public void OnKeyPress(OpenTK.KeyPressEventArgs e)
        {
            if (guistate == GuiState.Inventory)
            {
                d_HudInventory.OnKeyPress(e.KeyChar);
            }
        }

        public Point MouseCurrent
        {
            get { return mouse_current; }
        }

        public Vector3i SelectedBlock()
        {
            Vector3 pos = new Vector3(SelectedBlockPositionX, SelectedBlockPositionY, SelectedBlockPositionZ);
            if (pos == new Vector3(-1, -1, -1))
            {
                pos = ToVector3(player.playerposition);
            }
            return new Vector3i((int)pos.X, (int)pos.Z, (int)pos.Y);
        }
        private void OnPickUseWithTool(Vector3 pos)
        {
            SendSetBlock(new Vector3(pos.X, pos.Y, pos.Z), Packet_BlockSetModeEnum.UseWithTool, d_Inventory.RightHand[ActiveMaterial].BlockId, ActiveMaterial);
        }
        public void OnPick(Vector3 blockpos, Vector3 blockposold, float[] collisionPos, bool right)
        {
            float xfract = collisionPos[0] - (float)Math.Floor(collisionPos[0]);
            float zfract = collisionPos[2] - (float)Math.Floor(collisionPos[2]);
            int activematerial = (ushort)MaterialSlots[ActiveMaterial];
            int railstart = d_Data.BlockIdRailstart();
            if (activematerial == railstart + (int)RailDirectionFlags.TwoHorizontalVertical
                || activematerial == railstart + (int)RailDirectionFlags.Corners)
            {
                RailDirection dirnew;
                if (activematerial == railstart + (int)RailDirectionFlags.TwoHorizontalVertical)
                {
                    dirnew = PickHorizontalVertical(xfract, zfract);
                }
                else
                {
                    dirnew = PickCorners(xfract, zfract);
                }
                int dir = d_Data.Rail()[GetBlock((int)blockposold.X, (int)blockposold.Y, (int)blockposold.Z)];
                if (dir != 0)
                {
                    blockpos = blockposold;
                }
                activematerial = railstart + (int)(dir | DirectionUtils.ToRailDirectionFlags(dirnew));
                //Console.WriteLine(blockposold);
                //Console.WriteLine(xfract + ":" + zfract + ":" + activematerial + ":" + dirnew);
            }
            int x = (short)blockpos.X;
            int y = (short)blockpos.Y;
            int z = (short)blockpos.Z;
            var mode = right ? Packet_BlockSetModeEnum.Create : Packet_BlockSetModeEnum.Destroy;
            {
                if (IsAnyPlayerInPos(x, y, z) || activematerial == 151)
                {
                    return;
                }
                Vector3i v = new Vector3i(x, y, z);
                Vector3i? oldfillstart = fillstart;
                Vector3i? oldfillend = fillend;
                if (mode == Packet_BlockSetModeEnum.Create)
                {
                    if (game.blocktypes[activematerial].IsTool)
                    {
                        OnPickUseWithTool(blockpos);
                        return;
                    }
                    /*
                    if (GameDataManicDigger.IsDoorTile(activematerial))
                    {
                        if (z + 1 == d_Map.MapSizeZ || z == 0) return;
                    }
                    */
                    if (activematerial == d_Data.BlockIdCuboid())
                    {
                        ClearFillArea();

                        if (fillstart != null)
                        {
                            Vector3i f = fillstart.Value;
                            if (!IsFillBlock(d_Map.GetBlock(f.x, f.y, f.z)))
                            {
                                fillarea[f] = d_Map.GetBlock(f.x, f.y, f.z);
                            }
                            SetBlock(f.x, f.y, f.z, d_Data.BlockIdFillStart());


                            FillFill(v, fillstart.Value);
                        }
                        if (!IsFillBlock(d_Map.GetBlock(v.x, v.y, v.z)))
                        {
                            fillarea[v] = d_Map.GetBlock(v.x, v.y, v.z);
                        }
                        SetBlock(v.x, v.y, v.z, d_Data.BlockIdCuboid());
                        fillend = v;
                        RedrawBlock(v.x, v.y, v.z);
                        return;
                    }
                    if (activematerial == d_Data.BlockIdFillStart())
                    {
                        ClearFillArea();
                        if (!IsFillBlock(d_Map.GetBlock(v.x, v.y, v.z)))
                        {
                            fillarea[v] = d_Map.GetBlock(v.x, v.y, v.z);
                        }
                        SetBlock(v.x, v.y, v.z, d_Data.BlockIdFillStart());
                        fillstart = v;
                        fillend = null;
                        RedrawBlock(v.x, v.y, v.z);
                        return;
                    }
                    if (fillarea.ContainsKey(v))// && fillarea[v])
                    {
                        SendFillArea(fillstart.Value, fillend.Value, activematerial);
                        ClearFillArea();
                        fillstart = null;
                        fillend = null;
                        return;
                    }
                }
                else
                {
                    if (game.blocktypes[activematerial].IsTool)
                    {
                        OnPickUseWithTool(blockpos);
                        return;
                    }
                    //delete fill start
                    if (fillstart != null && fillstart == v)
                    {
                        ClearFillArea();
                        fillstart = null;
                        fillend = null;
                        return;
                    }
                    //delete fill end
                    if (fillend != null && fillend == v)
                    {
                        ClearFillArea();
                        fillend = null;
                        return;
                    }
                }
                if (mode == Packet_BlockSetModeEnum.Create && activematerial == d_Data.BlockIdMinecart())
                {
                    /*
                    CommandRailVehicleBuild cmd2 = new CommandRailVehicleBuild();
                    cmd2.x = (short)x;
                    cmd2.y = (short)y;
                    cmd2.z = (short)z;
                    TrySendCommand(MakeCommand(CommandId.RailVehicleBuild, cmd2));
                    */
                    return;
                }
                //if (TrySendCommand(MakeCommand(CommandId.Build, cmd)))
                SendSetBlockAndUpdateSpeculative(activematerial, x, y, z, mode);
            }
        }
        private void SendSetBlockAndUpdateSpeculative(int material, int x, int y, int z, int mode)
        {
            SendSetBlock(new Vector3(x, y, z), mode, material, ActiveMaterial);

            Packet_Item item = d_Inventory.RightHand[ActiveMaterial];
            if (item != null && item.ItemClass == Packet_ItemClassEnum.Block)
            {
                //int blockid = d_Inventory.RightHand[d_Viewport.ActiveMaterial].BlockId;
                int blockid = material;
                if (mode == Packet_BlockSetModeEnum.Destroy)
                {
                    blockid = SpecialBlockId.Empty;
                }
                speculative[new Vector3i(x, y, z)] = new Speculative() { blocktype = d_Map.GetBlock(x, y, z), timeMilliseconds = game.platform.TimeMillisecondsFromStart() };
                SetBlock(x, y, z, blockid);
                RedrawBlock(x, y, z);
            }
            else
            {
                //TODO
            }
        }
        private void ClearFillArea()
        {
            foreach (var k in fillarea)
            {
                var vv = k.Key;
                SetBlock(vv.x, vv.y, vv.z, k.Value);
                RedrawBlock(vv.x, vv.y, vv.z);
            }
            fillarea.Clear();
        }
        //value is original block.
        Dictionary<Vector3i, int> fillarea = new Dictionary<Vector3i, int>();
        Vector3i? fillstart;
        Vector3i? fillend;
        private int fillAreaLimit = 200;

        private void FillFill(Vector3i a, Vector3i b)
        {
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
                        if (fillarea.Count > fillAreaLimit)
                        {
                            ClearFillArea();
                            return;
                        }
                        if (!IsFillBlock(d_Map.GetBlock(x, y, z)))
                        {
                            fillarea[new Vector3i(x, y, z)] = d_Map.GetBlock(x, y, z);
                            SetBlock(x, y, z, d_Data.BlockIdFillArea());
                            RedrawBlock(x, y, z);
                        }
                    }
                }
            }
        }
        struct Speculative
        {
            public int timeMilliseconds;
            public int blocktype;
        }
        Dictionary<Vector3i, Speculative> speculative = new Dictionary<Vector3i, Speculative>();
        public void SendSetBlock(Vector3 vector3, int blockSetMode, int p)
        {
            SendSetBlock(vector3, blockSetMode, p, ActiveMaterial);
        }
        public void OnNewFrame(double dt)
        {
            foreach (var k in new Dictionary<Vector3i, Speculative>(speculative))
            {
                if ((one * (game.platform.TimeMillisecondsFromStart() - k.Value.timeMilliseconds) / 1000) > 2)
                {
                    speculative.Remove(k.Key);
                    RedrawBlock(k.Key.x, k.Key.y, k.Key.z);
                }
            }
            if (KeyPressed(GetKey(OpenTK.Input.Key.C)))
            {
                if (PickCubePos != new Vector3(-1, -1, -1))
                {
                    Vector3i pos = new Vector3i((int)PickCubePos.X, (int)PickCubePos.Z, (int)PickCubePos.Y);
                    if (d_Map.GetBlock(pos.x, pos.y, pos.z)
                        == d_Data.BlockIdCraftingTable())
                    {
                        //draw crafting recipes list.
                        CraftingRecipesStart(d_CraftingRecipes, d_CraftingTableTool.GetOnTable(d_CraftingTableTool.GetTable(pos)),
                        (recipe) => { CraftingRecipeSelected(pos.x, pos.y, pos.z, recipe); });
                    }
                }
            }
            ENABLE_FINITEINVENTORY = this.ENABLE_FINITEINVENTORY;
            RailOnNewFrame((float)dt);
        }
        private bool KeyPressed(OpenTK.Input.Key key)
        {
            return keypressed != null && keypressed.Key == key;
        }
        private bool KeyDepressed(OpenTK.Input.Key key)
        {
            return keydepressed != null && keydepressed.Key == key;
        }
        Dictionary<int, int> finiteinventory = new Dictionary<int, int>();
        private Vector3 playerpositionspawn = new Vector3(15.5f, 64, 15.5f);
        public Vector3 PlayerPositionSpawn { get { return playerpositionspawn; } set { playerpositionspawn = value; } }
        public Vector3 PlayerOrientationSpawn { get { return new Vector3((float)Math.PI, 0, 0); } }
        public Player[] Players { get { return players; } set { players = value; } }

        public void SetChunk(int x, int y, int z, int[, ,] chunk)
        {
            SetMapPortion(x, y, z, chunk);
        }
        public void OnNewMap()
        {
            playerpositionspawn = LocalPlayerPosition;
        }
        public void ModelClick(int selectedmodelid)
        {
        }
        public ManicDiggerGameWindow mapforphysics { get { return this; } }
        public bool ENABLE_FINITEINVENTORY { get; set; }
        public int HourDetail = 4;
        public int[] NightLevels;
        public bool ENABLE_PER_SERVER_TEXTURES = false;
        string blobdownloadname;
        MemoryStream blobdownload;
        #region ICurrentSeason Members
        public int CurrentSeason { get; set; }
        #endregion

        public event EventHandler<EventArgs> MapLoaded;
        public bool ENABLE_FORTRESS = true;
        public void Connect(string serverAddress, int port, string username, string auth)
        {
            iep = new IPEndPoint(IPAddress.Any, port);
            main.Start();
            main.Connect(serverAddress, port);
            this.username = username;
            this.auth = auth;
            byte[] n = CreateLoginPacket(username, auth);
            var msg = main.CreateMessage();
            msg.Write(n, n.Length);
            main.SendMessage(msg, MyNetDeliveryMethod.ReliableOrdered);
        }
        public void Connect(string serverAddress, int port, string username, string auth, string serverPassword)
        {
            iep = new IPEndPoint(IPAddress.Any, port);
            main.Start();
            main.Connect(serverAddress, port);
            this.username = username;
            this.auth = auth;
            byte[] n = CreateLoginPacket(username, auth, serverPassword);
            var msg = main.CreateMessage();
            msg.Write(n, n.Length);
            main.SendMessage(msg, MyNetDeliveryMethod.ReliableOrdered);
        }
        string username;
        string auth;
        private byte[] CreateLoginPacket(string username, string verificationKey)
        {
            Packet_ClientIdentification p = new Packet_ClientIdentification()
            {
                Username = username,
                MdProtocolVersion = GameVersion.Version,
                VerificationKey = verificationKey
            };
            byte[] packet = Serialize(new Packet_Client() { Id = Packet_ClientIdEnum.PlayerIdentification, Identification = p }, packetLen);
            return packet;
        }
        private byte[] CreateLoginPacket(string username, string verificationKey, string serverPassword)
        {
            Packet_ClientIdentification p = new Packet_ClientIdentification()
            {
                Username = username,
                MdProtocolVersion = GameVersion.Version,
                VerificationKey = verificationKey,
                ServerPassword = serverPassword
            };
            byte[] packet = Serialize(new Packet_Client() { Id = Packet_ClientIdEnum.PlayerIdentification, Identification = p }, packetLen);
            return packet;
        }
        IPEndPoint iep;
        void EmptyCallback(IAsyncResult result)
        {
        }
        public void SendSetBlock(Vector3 position, int mode, int type, int materialslot)
        {
            game.SendSetBlock((int)position.X, (int)position.Y, (int)position.Z, mode, type, materialslot);
        }
        IntRef packetLen = new IntRef();
        /// <summary>
        /// This function should be called in program main loop.
        /// It exits immediately.
        /// </summary>
        public void NetworkProcess()
        {
            currentTimeMilliseconds = game.platform.TimeMillisecondsFromStart();
            stopwatch.Reset();
            stopwatch.Start();
            if (main == null)
            {
                return;
            }
            INetIncomingMessage msg;
            while ((msg = main.ReadMessage()) != null)
            {
                TryReadPacket(msg.ReadBytes(msg.LengthBytes()));
            }
            if (spawned && ((game.platform.TimeMillisecondsFromStart() - lastpositionsentMilliseconds) > 100))
            {
                lastpositionsentMilliseconds = game.platform.TimeMillisecondsFromStart();
                SendPosition(LocalPlayerPosition.X, LocalPlayerPosition.Y, LocalPlayerPosition.Z,
                    LocalPlayerOrientation.X, LocalPlayerOrientation.Y, LocalPlayerOrientation.Z);
            }
            int now = game.platform.TimeMillisecondsFromStart();
            for (int i = 0; i < playersCount; i++)
            {
                if (players[i] == null)
                {
                    continue;
                }
                int kKey = i;
                Player p = players[i];
                if ((one * (now - p.LastUpdateMilliseconds) / 1000) > 2)
                {
                    playerdrawinfo.Remove(kKey);
                    p.PositionLoaded = false;
                }
            }
        }
        public int mapreceivedsizex;
        public int mapreceivedsizey;
        public int mapreceivedsizez;

        public static Vector3 HeadingPitchToOrientation(byte heading, byte pitch)
        {
            float x = ((float)heading / 256) * 2 * (float)Math.PI;
            float y = (((float)pitch / 256) * 2 * (float)Math.PI) - (float)Math.PI;
            return new Vector3() { X = x, Y = y };
        }

        Stopwatch stopwatch = new Stopwatch();
        public int maxMiliseconds = 3;
        private void TryReadPacket(byte[] data)
        {
            Packet_Server packet = new Packet_Server();
            Packet_ServerSerializer.DeserializeBuffer(data,data.Length, packet);
            if (Debugger.IsAttached
                && packet.Id != Packet_ServerIdEnum.PositionUpdate
                && packet.Id != Packet_ServerIdEnum.OrientationUpdate
                && packet.Id != Packet_ServerIdEnum.PlayerPositionAndOrientation
                && packet.Id != Packet_ServerIdEnum.ExtendedPacketTick
                && packet.Id != Packet_ServerIdEnum.Chunk_
                && packet.Id != Packet_ServerIdEnum.Ping)
            {
                //Console.WriteLine("read packet: " + Enum.GetName(typeof(MinecraftServerPacketId), packet.PacketId));
            }
            switch (packet.Id)
            {
                case Packet_ServerIdEnum.ServerIdentification:
                    {
                        string invalidversionstr = language.InvalidVersionConnectAnyway();
                        {
                            string servergameversion = packet.Identification.MdProtocolVersion;
                            if (servergameversion != GameVersion.Version)
                            {
                                for (int i = 0; i < 5; i++)
                                {
                                    System.Windows.Forms.Cursor.Show();
                                    System.Threading.Thread.Sleep(100);
                                    Application.DoEvents();
                                }
                                string q = string.Format(invalidversionstr, GameVersion.Version, servergameversion);
                                var result = System.Windows.Forms.MessageBox.Show(q, "Invalid version", System.Windows.Forms.MessageBoxButtons.OKCancel);
                                if (result == System.Windows.Forms.DialogResult.Cancel)
                                {
                                    throw new Exception(q);
                                }
                                for (int i = 0; i < 10; i++)
                                {
                                    System.Windows.Forms.Cursor.Hide();
                                    System.Threading.Thread.Sleep(100);
                                    Application.DoEvents();
                                }
                            }
                        }
                        this.LocalPlayerId = packet.Identification.AssignedClientId;
                        this.ServerInfo.connectdata = this.connectdata;
                        this.ServerInfo.ServerName = packet.Identification.ServerName;
                        this.ServerInfo.ServerMotd = packet.Identification.ServerMotd;
                        this.d_TerrainChunkTesselator.ENABLE_TEXTURE_TILING = packet.Identification.RenderHint_ == (int)RenderHint.Fast;
                        ChatLog("---Connected---");
                        SendRequestBlob();
                        if (packet.Identification.MapSizeX != d_Map.MapSizeX
                            || packet.Identification.MapSizeY != d_Map.MapSizeY
                            || packet.Identification.MapSizeZ != d_Map.MapSizeZ)
                        {
                            d_ResetMap.Reset(packet.Identification.MapSizeX,
                                packet.Identification.MapSizeY,
                                packet.Identification.MapSizeZ);
                            d_Heightmap.Restart();
                        }
                        //serverterraintexture = ByteArrayToString(packet.Identification.TerrainTextureMd5);
                        terrainRenderer.shadowssimple = packet.Identification.DisableShadows == 1 ? true : false;
                        maxdrawdistance = packet.Identification.PlayerAreaSize / 2;
                        if (maxdrawdistance == 0)
                        {
                            maxdrawdistance = 128;
                        }
                    }
                    break;
                case Packet_ServerIdEnum.Ping:
                    {
                        this.SendPingReply();
                        this.ServerInfo.ServerPing.Send(game.platform);
                    }
                    break;
                case Packet_ServerIdEnum.PlayerPing:
                    {
                        for (int i = 0; i < this.ServerInfo.Players.count;i++)
                        {
                            ConnectedPlayer k = ServerInfo.Players.items[i];
                            if (k == null)
                            {
                                continue;
                            }
                            if (k.id == packet.PlayerPing.ClientId)
                            {
                                if (k.id == this.LocalPlayerId)
                                {
                                    this.ServerInfo.ServerPing.Receive(game.platform);
                                }
                                k.ping = packet.PlayerPing.Ping;
                                break;
                            }
                        }
                    }
                    break;
                case Packet_ServerIdEnum.LevelInitialize:
                    {
                        ReceivedMapLength = 0;
                        InvokeMapLoadingProgress(0, 0, language.Connecting());
                    }
                    break;
                case Packet_ServerIdEnum.LevelDataChunk:
                    {
                        MapLoadingPercentComplete = packet.LevelDataChunk.PercentComplete;
                        MapLoadingStatus = packet.LevelDataChunk.Status;
                        InvokeMapLoadingProgress(MapLoadingPercentComplete, (int)ReceivedMapLength, MapLoadingStatus);
                    }
                    break;
                case Packet_ServerIdEnum.LevelFinalize:
                    {
                        //d_Data.Load(MyStream.ReadAllLines(d_GetFile.GetFile("blocks.csv")),
                        //    MyStream.ReadAllLines(d_GetFile.GetFile("defaultmaterialslots.csv")),
                        //    MyStream.ReadAllLines(d_GetFile.GetFile("lightlevels.csv")));
                        //d_CraftingRecipes.Load(MyStream.ReadAllLines(d_GetFile.GetFile("craftingrecipes.csv")));

                        if (MapLoaded != null)
                        {
                            MapLoaded.Invoke(this, new EventArgs() { });
                        }
                    }
                    break;
                case Packet_ServerIdEnum.SetBlock:
                    {
                        int x = packet.SetBlock.X;
                        int y = packet.SetBlock.Y;
                        int z = packet.SetBlock.Z;
                        int type = packet.SetBlock.BlockType;
                        try { SetTileAndUpdate(x, y, z, type); }
                        catch { Console.WriteLine("Cannot update tile!"); }
                    }
                    break;
                case Packet_ServerIdEnum.FillArea:
                    {
                        Vector3i a = new Vector3i(packet.FillArea.X1, packet.FillArea.Y1, packet.FillArea.Z1);
                        Vector3i b = new Vector3i(packet.FillArea.X2, packet.FillArea.Y2, packet.FillArea.Z2);

                        int startx = Math.Min(a.x, b.x);
                        int endx = Math.Max(a.x, b.x);
                        int starty = Math.Min(a.y, b.y);
                        int endy = Math.Max(a.y, b.y);
                        int startz = Math.Min(a.z, b.z);
                        int endz = Math.Max(a.z, b.z);

                        int blockCount = packet.FillArea.BlockCount;

                        Jint.Delegates.Action fillArea = delegate
                        {
                            for (int x = startx; x <= endx; ++x)
                            {
                                for (int y = starty; y <= endy; ++y)
                                {
                                    for (int z = startz; z <= endz; ++z)
                                    {
                                        // if creative mode is off and player run out of blocks
                                        if (blockCount == 0)
                                        {
                                            return;
                                        }
                                        try
                                        {
                                            SetTileAndUpdate(x, y, z, packet.FillArea.BlockType);
                                        }
                                        catch
                                        {
                                            Console.WriteLine("Cannot update tile!");
                                        }
                                        blockCount--;
                                    }
                                }
                            }
                        };
                        fillArea();
                    }
                    break;
                case Packet_ServerIdEnum.FillAreaLimit:
                    {
                        this.fillAreaLimit = packet.FillAreaLimit.Limit;
                        if (this.fillAreaLimit > 100000)
                        {
                            this.fillAreaLimit = 100000;
                        }
                    }
                    break;
                case Packet_ServerIdEnum.Freemove:
                    {
                        this.AllowFreemove = packet.Freemove.IsEnabled != 0;
                        if (!this.AllowFreemove)
                        {
                            ENABLE_FREEMOVE = false;
                            ENABLE_NOCLIP = false;
                            movespeed = basemovespeed;
                            Log(language.MoveNormal());
                        }
                    }
                    break;
                case Packet_ServerIdEnum.PlayerSpawnPosition:
                    {
                        int x = packet.PlayerSpawnPosition.X;
                        int y = packet.PlayerSpawnPosition.Y;
                        int z = packet.PlayerSpawnPosition.Z;
                        this.PlayerPositionSpawn = new Vector3(x, z, y);
                        Log(string.Format(language.SpawnPositionSetTo(), x + "," + y + "," + z));
                    }
                    break;
                case Packet_ServerIdEnum.SpawnPlayer:
                    {
                        int playerid = packet.SpawnPlayer.PlayerId;
                        string playername = packet.SpawnPlayer.PlayerName;
                        bool isnewplayer = true;
                        for (int i = 0; i < ServerInfo.Players.count; i++)
                        {
                            ConnectedPlayer p = ServerInfo.Players.items[i];
                            if (p == null)
                            {
                                continue;
                            }
                            if (p.id == playerid)
                            {
                                isnewplayer = false;
                                p.name = playername;
                            }
                        }
                        if (isnewplayer)
                        {
                            this.ServerInfo.Players.Add(new ConnectedPlayer() { name = playername, id = playerid, ping = -1 });
                        }
                        d_Clients.Players[playerid] = new Player();
                        d_Clients.Players[playerid].Name = playername;
                        d_Clients.Players[playerid].Model = packet.SpawnPlayer.Model_;
                        d_Clients.Players[playerid].Texture = packet.SpawnPlayer.Texture_;
                        d_Clients.Players[playerid].EyeHeight = DeserializeFloat(packet.SpawnPlayer.EyeHeightFloat);
                        d_Clients.Players[playerid].ModelHeight = DeserializeFloat(packet.SpawnPlayer.ModelHeightFloat);
                        ReadAndUpdatePlayerPosition(packet.SpawnPlayer.PositionAndOrientation, playerid);
                        if (playerid == this.LocalPlayerId)
                        {
                            spawned = true;
                        }
                    }
                    break;
                case Packet_ServerIdEnum.PlayerPositionAndOrientation:
                    {
                        int playerid = packet.PositionAndOrientation.PlayerId;
                        ReadAndUpdatePlayerPosition(packet.PositionAndOrientation.PositionAndOrientation, playerid);
                    }
                    break;
                case Packet_ServerIdEnum.Monster:
                    {
                        if (packet.Monster.Monsters == null)
                        {
                            break;
                        }
                        Dictionary<int, int> updatedMonsters = new Dictionary<int, int>();
                        foreach (var k in packet.Monster.Monsters)
                        {
                            int id = k.Id + MonsterIdFirst;
                            if (players[id] == null)
                            {
                                players[id] = new Player();
                                players[id].Name = d_DataMonsters.MonsterName[k.MonsterType];
                            }
                            ReadAndUpdatePlayerPosition(k.PositionAndOrientation, id);
                            players[id].Type = PlayerType.Monster;
                            players[id].Health = k.Health;
                            players[id].MonsterType = k.MonsterType;
                            updatedMonsters[id] = 1;
                        }
                        //remove all old monsters that were not sent by server now.

                        //this causes monster flicker on chunk boundaries,
                        //commented out
                        /*foreach (int id in new List<int>(players.Keys))
                        {
                            if (id >= MonsterIdFirst)
                            {
                                if (!updatedMonsters.ContainsKey(id))
                                {
                                    players.Remove(id);
                                }
                            }
                        }*/
                    }
                    break;
                case Packet_ServerIdEnum.DespawnPlayer:
                    {
                        int playerid = packet.DespawnPlayer.PlayerId;
                        for (int i = 0; i < this.ServerInfo.Players.count; i++)
                        {
                            ConnectedPlayer p = ServerInfo.Players.items[i];
                            if (p == null)
                            {
                                continue;
                            }
                            if (p.id == playerid)
                            {
                                this.ServerInfo.Players.RemoveAt(i);
                            }
                        }
                        players[playerid] = null;
                    }
                    break;
                case Packet_ServerIdEnum.Message:
                    {
                        AddChatline(packet.Message.Message);
                        ChatLog(packet.Message.Message);
                    }
                    break;
                case Packet_ServerIdEnum.DisconnectPlayer:
                    {
                        System.Windows.Forms.MessageBox.Show(packet.DisconnectPlayer.DisconnectReason, "Disconnected from server", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Exclamation);
                        d_GlWindow.Exit();
                        //Not needed anymore - avoids "cryptic" error messages on being kicked/banned
                        //throw new Exception(packet.DisconnectPlayer.DisconnectReason);
                        break;
                    }
                case Packet_ServerIdEnum.ChunkPart:
                    BinaryWriter bw1 = new BinaryWriter(CurrentChunk);
                    bw1.Write((byte[])packet.ChunkPart.CompressedChunkPart);
                    break;
                case Packet_ServerIdEnum.Chunk_:
                    {
                        var p = packet.Chunk_;
                        int[, ,] receivedchunk;
                        if (CurrentChunk.Length != 0)
                        {
                            byte[] decompressedchunk = d_Compression.Decompress(CurrentChunk.ToArray());
                            receivedchunk = new int[p.SizeX, p.SizeY, p.SizeZ];
                            {
                                BinaryReader br2 = new BinaryReader(new MemoryStream(decompressedchunk));
                                for (int zz = 0; zz < p.SizeZ; zz++)
                                {
                                    for (int yy = 0; yy < p.SizeY; yy++)
                                    {
                                        for (int xx = 0; xx < p.SizeX; xx++)
                                        {
                                            receivedchunk[xx, yy, zz] = br2.ReadUInt16();
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            receivedchunk = new int[p.SizeX, p.SizeY, p.SizeZ];
                        }
                        {
                            SetMapPortion(p.X, p.Y, p.Z, receivedchunk);
                            for (int xx = 0; xx < 2; xx++)
                            {
                                for (int yy = 0; yy < 2; yy++)
                                {
                                    for (int zz = 0; zz < 2; zz++)
                                    {
                                        //d_Shadows.OnSetChunk(p.X + 16 * xx, p.Y + 16 * yy, p.Z + 16 * zz);//todo
                                    }
                                }
                            }
                        }
                        ReceivedMapLength += data.Length;// lengthPrefixLength + packetLength;
                        CurrentChunk = new MemoryStream();
                    }
                    break;
                case Packet_ServerIdEnum.HeightmapChunk:
                    {
                        var p = packet.HeightmapChunk;
                        byte[] decompressedchunk = d_Compression.Decompress(p.CompressedHeightmap);
                        ushort[] decompressedchunk1 = Misc.ByteArrayToUshortArray(decompressedchunk);
                        for (int xx = 0; xx < p.SizeX; xx++)
                        {
                            for (int yy = 0; yy < p.SizeY; yy++)
                            {
                                int height = decompressedchunk1[MapUtilCi.Index2d(xx, yy, p.SizeX)];
                                d_Heightmap.SetBlock(p.X + xx, p.Y + yy, height);
                            }
                        }
                    }
                    break;
                case Packet_ServerIdEnum.PlayerStats:
                    {
                        var p = packet.PlayerStats;
                        this.PlayerStats = p;
                    }
                    break;
                case Packet_ServerIdEnum.FiniteInventory:
                    {
                        //check for null so it's possible to connect
                        //to old versions of game (before 2011-05-05)
                        if (packet.Inventory.Inventory != null)
                        {
                            //d_Inventory.CopyFrom(ConvertInventory(packet.Inventory.Inventory));
                            UseInventory(packet.Inventory.Inventory);
                        }
                        /*
                        FiniteInventory = packet.FiniteInventory.BlockTypeAmount;
                        ENABLE_FINITEINVENTORY = packet.FiniteInventory.IsFinite;
                        FiniteInventoryMax = packet.FiniteInventory.Max;
                        */
                    }
                    break;
                case Packet_ServerIdEnum.Season:
                    {
                        packet.Season.Hour -= 1;
                        if (packet.Season.Hour < 0)
                        {
                            //shouldn't happen
                            packet.Season.Hour = 12 * HourDetail;
                        }
                        if (NightLevels == null)
                        {
                            string[] l = MyStream.ReadAllLines(d_GetFile.GetFile("sunlevels.csv"));
                            NightLevels = new int[24 * HourDetail];
                            for (int i = 0; i < 24 * HourDetail; i++)
                            {
                                string s = l[i];
                                if (s.Contains(";")) { s = s.Substring(0, s.IndexOf(";")); }
                                if (s.Contains(",")) { s = s.Substring(0, s.IndexOf(",")); }
                                s = s.Trim();
                                NightLevels[i] = int.Parse(s);
                            }
                        }
                        int sunlight = NightLevels[packet.Season.Hour];
                        SkySphereNight = sunlight < 8;
                        d_SunMoonRenderer.day_length_in_seconds = 60 * 60 * 24 / packet.Season.DayNightCycleSpeedup;
                        int hour = packet.Season.Hour / HourDetail;
                        if (d_SunMoonRenderer.GetHour() != hour)
                        {
                            d_SunMoonRenderer.SetHour(hour);
                        }

                        if (d_Shadows.sunlight != sunlight)
                        {
                            d_Shadows.sunlight = sunlight;
                            //d_Shadows.ResetShadows();
                            RedrawAllBlocks();
                        }
                    }
                    break;
                case Packet_ServerIdEnum.BlobInitialize:
                    {
                        blobdownload = new MemoryStream();
                        //blobdownloadhash = ByteArrayToString(packet.BlobInitialize.hash);
                        blobdownloadname = packet.BlobInitialize.Name;
                        ReceivedMapLength = 0; //todo
                    }
                    break;
                case Packet_ServerIdEnum.BlobPart:
                    {
                        BinaryWriter bw = new BinaryWriter(blobdownload);
                        bw.Write(packet.BlobPart.Data);
                        ReceivedMapLength += packet.BlobPart.Data.Length; //todo
                    }
                    break;
                case Packet_ServerIdEnum.BlobFinalize:
                    {
                        byte[] downloaded = blobdownload.ToArray();
                        /*
                        if (ENABLE_PER_SERVER_TEXTURES || Options.UseServerTextures)
                        {
                            if (blobdownloadhash == serverterraintexture)
                            {
                                using (Bitmap bmp = new Bitmap(new MemoryStream(downloaded)))
                                {
                                    d_TerrainTextures.UseTerrainTextureAtlas2d(bmp);
                                }
                            }
                        }
                        */
                        if (blobdownloadname != null) // old servers
                        {
                            d_GetFile.SetFile(blobdownloadname, downloaded);
                        }
                        blobdownload = null;
                    }
                    break;
                case Packet_ServerIdEnum.Sound:
                    {
                        PlaySoundAt(packet.Sound.Name, packet.Sound.X, packet.Sound.Y, packet.Sound.Z);
                    }
                    break;
                case Packet_ServerIdEnum.RemoveMonsters:
                    {
                        for (int i = MonsterIdFirst; i < playersCount; i++)
                        {
                            players[i] = null;
                        }
                    }
                    break;
                case Packet_ServerIdEnum.Translation:
                    language.Override(packet.Translation.Lang, packet.Translation.Id, packet.Translation.Translation);
                    break;
                case Packet_ServerIdEnum.BlockType:
                    NewBlockTypes[packet.BlockType.Id] = packet.BlockType.Blocktype;
                    break;
                case Packet_ServerIdEnum.BlockTypes:
                    game.blocktypes = NewBlockTypes;
                    NewBlockTypes = new Packet_BlockType[game.blocktypes.Length];

                    Dictionary<string, int> textureInAtlasIds = new Dictionary<string, int>();
                    int lastTextureId = 0;
                    for (int i = 0; i < game.blocktypes.Length; i++)
                    {
                        if (game.blocktypes[i] != null)
                        {
                            string[] to_load = new string[]
                            {
                                game.blocktypes[i].TextureIdLeft,
                                game.blocktypes[i].TextureIdRight,
                                game.blocktypes[i].TextureIdFront,
                                game.blocktypes[i].TextureIdBack,
                                game.blocktypes[i].TextureIdTop,
                                game.blocktypes[i].TextureIdBottom,
                                game.blocktypes[i].TextureIdForInventory,
                            };
                            for (int k = 0; k < to_load.Length; k++)
                            {
                                if (!textureInAtlasIds.ContainsKey(to_load[k]))
                                {
                                    textureInAtlasIds[to_load[k]] = lastTextureId++;
                                }
                            }
                        }
                    }
                    d_Data.UseBlockTypes(game.platform, game.blocktypes, game.blocktypes.Length);
                    for (int i = 0; i < game.blocktypes.Length; i++)
                    {
                        Packet_BlockType b = game.blocktypes[i];
                        //Indexed by block id and TileSide.
                        if (textureInAtlasIds != null)
                        {
                            game.TextureId[i][0] = textureInAtlasIds[b.TextureIdTop];
                            game.TextureId[i][1] = textureInAtlasIds[b.TextureIdBottom];
                            game.TextureId[i][2] = textureInAtlasIds[b.TextureIdFront];
                            game.TextureId[i][3] = textureInAtlasIds[b.TextureIdBack];
                            game.TextureId[i][4] = textureInAtlasIds[b.TextureIdLeft];
                            game.TextureId[i][5] = textureInAtlasIds[b.TextureIdRight];
                            game.TextureIdForInventory[i] = textureInAtlasIds[b.TextureIdForInventory];
                        }
                    }
                    UseTerrainTextures(textureInAtlasIds);
                    d_Weapon.redraw = true;
                    RedrawAllBlocks();
                    break;
                case Packet_ServerIdEnum.SunLevels:
                    NightLevels = packet.SunLevels.Sunlevels;
                    break;
                case Packet_ServerIdEnum.LightLevels:
                    for (int i = 0; i < packet.LightLevels.LightlevelsCount; i++)
                    {
                        game.mLightLevels[i] = DeserializeFloat(packet.LightLevels.Lightlevels[i]);
                    }
                    break;
                case Packet_ServerIdEnum.CraftingRecipes:
                    d_CraftingRecipes = packet.CraftingRecipes.CraftingRecipes;
                    break;
                case Packet_ServerIdEnum.Dialog:
                    var d = packet.Dialog;
                    if (d.Dialog == null)
                    {
                        if (GetDialogId(d.DialogId) != -1 && dialogs[GetDialogId(d.DialogId)].value.IsModal != 0)
                        {
                            GuiStateBackToGame();
                        }
                        dialogs[GetDialogId(d.DialogId)] = null;
                        if (DialogsCount() == 0)
                        {
                            FreeMouse = false;
                        }
                    }
                    else
                    {
                        VisibleDialog d2 = new VisibleDialog();
                        d2.key = d.DialogId;
                        d2.value = d.Dialog;
                        if (GetDialogId(d.DialogId) == -1)
                        {
                            for (int i = 0; i < dialogsCount; i++)
                            {
                                if (dialogs[i] == null)
                                {
                                    dialogs[i] = d2;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            dialogs[GetDialogId(d.DialogId)] = d2;
                        }
                        if (d.Dialog.IsModal != 0)
                        {
                            guistate = GuiState.ModalDialog;
                            FreeMouse = true;
                        }
                    }
                    break;
                case Packet_ServerIdEnum.Follow:
                    IntRef oldFollowId = FollowId();
                    Follow = packet.Follow.Client;
                    if (packet.Follow.Tpp != 0)
                    {
                        SetCamera(CameraType.Overhead);
                        player.playerorientation.X = (float)Math.PI;
                        GuiStateBackToGame();
                    }
                    else
                    {
                        SetCamera(CameraType.Fpp);
                    }
                    break;
                case Packet_ServerIdEnum.Bullet:
                    EntityAdd(CreateBulletEntity(
                        DeserializeFloat(packet.Bullet.FromXFloat),
                        DeserializeFloat(packet.Bullet.FromYFloat),
                        DeserializeFloat(packet.Bullet.FromZFloat),
                        DeserializeFloat(packet.Bullet.ToXFloat),
                        DeserializeFloat(packet.Bullet.ToYFloat),
                        DeserializeFloat(packet.Bullet.ToZFloat),
                        DeserializeFloat(packet.Bullet.SpeedFloat)));
                    break;
                case Packet_ServerIdEnum.Ammo:
                    if (!ammostarted)
                    {
                        ammostarted = true;
                        for (int i = 0; i < packet.Ammo.TotalAmmoCount; i++)
                        {
                            var k = packet.Ammo.TotalAmmo[i];
                            LoadedAmmo[k.Key_] = Math.Min(k.Value_, game.blocktypes[k.Key_].AmmoMagazine);
                        }
                    }
                    TotalAmmo = new int[GlobalVar.MAX_BLOCKTYPES];
                    for (int i = 0; i < packet.Ammo.TotalAmmoCount; i++)
                    {
                        TotalAmmo[packet.Ammo.TotalAmmo[i].Key_] = packet.Ammo.TotalAmmo[i].Value_;
                    }
                    break;
                case Packet_ServerIdEnum.Explosion:
                    {
                        Entity entity = new Entity();
                        entity.expires = new Expires();
                        entity.expires.timeLeft = DeserializeFloat(packet.Explosion.TimeFloat);
                        entity.push = packet.Explosion;
                        EntityAdd(entity);
                    }
                    break;
                case Packet_ServerIdEnum.Projectile:
                    {
                        Entity entity = new Entity();
                        
                        Sprite sprite = new Sprite();
                        sprite.image = "ChemicalGreen.png";
                        sprite.size = 14;
                        sprite.animationcount = 0;
                        sprite.positionX = DeserializeFloat(packet.Projectile.FromXFloat);
                        sprite.positionY = DeserializeFloat(packet.Projectile.FromYFloat);
                        sprite.positionZ = DeserializeFloat(packet.Projectile.FromZFloat);
                        entity.sprite = sprite;

                        Grenade_ grenade = new Grenade_();
                        grenade.velocityX = DeserializeFloat(packet.Projectile.VelocityXFloat);
                        grenade.velocityY = DeserializeFloat(packet.Projectile.VelocityYFloat);
                        grenade.velocityZ = DeserializeFloat(packet.Projectile.VelocityZFloat);
                        grenade.block = packet.Projectile.BlockId;
                        grenade.sourcePlayer = packet.Projectile.SourcePlayerID;
                        entity.grenade = grenade;

                        entity.expires = Expires.Create(DeserializeFloat(packet.Projectile.ExplodesAfterFloat));

                        EntityAdd(entity);
                    }
                    break;
                default:
                    break;
            }
            LastReceivedMilliseconds = currentTimeMilliseconds;
            //return lengthPrefixLength + packetLength;
        }

        void UseInventory(Packet_Inventory packet_Inventory)
        {
            d_Inventory = packet_Inventory;
            d_InventoryUtil.d_Inventory = packet_Inventory;
        }
        

        private Inventory ConvertInventory(Packet_Inventory packet_Inventory)
        {
            Inventory inv = new Inventory();
            inv.Boots = GetItem(packet_Inventory.Boots);
            inv.DragDropItem = GetItem(packet_Inventory.DragDropItem);
            inv.Gauntlet = GetItem(packet_Inventory.Gauntlet);
            inv.Helmet = GetItem(packet_Inventory.Helmet);
            inv.Items = new Dictionary<ProtoPoint, Item>();
            for (int i = 0; i < packet_Inventory.ItemsCount; i++)
            {
                inv.Items[DeserializePoint(packet_Inventory.Items[i].Key_)] = GetItem(packet_Inventory.Items[i].Value_);
            }
            inv.MainArmor = GetItem(packet_Inventory.MainArmor);
            inv.RightHand = new Item[10];
            for (int i = 0; i < 10; i++)
            {
                inv.RightHand[i] = GetItem(packet_Inventory.RightHand[i]);
            }
            return inv;
        }

        ProtoPoint DeserializePoint(string s)
        {
            string[] ss = s.Split(new char[] { ' ' });
            ProtoPoint p = new ProtoPoint();
            p.X = int.Parse(ss[0]);
            p.Y = int.Parse(ss[1]);
            return p;
        }

        Item GetItem(Packet_Item p)
        {
            if (p == null || p.BlockId == 0)
            {
                return null;
            }
            Item item = new Item();
            item.BlockCount = p.BlockCount;
            item.BlockId = p.BlockId;
            item.ItemClass = (ItemClass)p.ItemClass;
            item.ItemId = p.ItemId;
            return item;
        }
        MemoryStream CurrentChunk = new MemoryStream();
        Packet_BlockType[] NewBlockTypes = new Packet_BlockType[GlobalVar.MAX_BLOCKTYPES];
        IntRef FollowId()
        {
            return game.FollowId();
        }
        public bool ENABLE_CHATLOG = true;
        public string gamepathlogs() { return Path.Combine(game.platform.PathStorage(), "Logs"); }
        private void ChatLog(string p)
        {
            if (!ENABLE_CHATLOG)
            {
                return;
            }
            if (!Directory.Exists(gamepathlogs()))
            {
                Directory.CreateDirectory(gamepathlogs());
            }
            string filename = Path.Combine(gamepathlogs(), MakeValidFileName(this.ServerInfo.ServerName) + ".txt");
            try
            {
                File.AppendAllText(filename, string.Format("{0} {1}\n", DateTime.Now, p));
            }
            catch
            {
                Console.WriteLine(language.CannotWriteChatLog(), filename);
            }
        }
        private static string MakeValidFileName(string name)
        {
            string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            string invalidReStr = string.Format(@"[{0}]", invalidChars);
            return Regex.Replace(name, invalidReStr, "_");
        }
        public void Dispose()
        {
            if (main != null)
            {
                //main.Disconnect(false);
                main = null;
            }
        }

        #region IMapStorage Members
        public unsafe void SetMapPortion(int x, int y, int z, int[, ,] chunk)
        {
            int chunksizex = chunk.GetUpperBound(0) + 1;
            int chunksizey = chunk.GetUpperBound(1) + 1;
            int chunksizez = chunk.GetUpperBound(2) + 1;
            if (chunksizex % chunksize != 0) { throw new ArgumentException(); }
            if (chunksizey % chunksize != 0) { throw new ArgumentException(); }
            if (chunksizez % chunksize != 0) { throw new ArgumentException(); }
            Chunk[, ,] localchunks = new Chunk[chunksizex / chunksize, chunksizey / chunksize, chunksizez / chunksize];
            for (int cx = 0; cx < chunksizex / chunksize; cx++)
            {
                for (int cy = 0; cy < chunksizey / chunksize; cy++)
                {
                    for (int cz = 0; cz < chunksizex / chunksize; cz++)
                    {
                        localchunks[cx, cy, cz] = GetChunk(x + cx * chunksize, y + cy * chunksize, z + cz * chunksize);
                        FillChunk(localchunks[cx, cy, cz], chunksize, cx * chunksize, cy * chunksize, cz * chunksize, chunk);
                    }
                }
            }
            for (int xxx = 0; xxx < chunksizex; xxx += chunksize)
            {
                for (int yyy = 0; yyy < chunksizex; yyy += chunksize)
                {
                    for (int zzz = 0; zzz < chunksizex; zzz += chunksize)
                    {
                        terrainRenderer.SetChunkDirty((x + xxx) / chunksize, (y + yyy) / chunksize, (z + zzz) / chunksize, true, true);
                        SetChunksAroundDirty((x + xxx) / chunksize, (y + yyy) / chunksize, (z + zzz) / chunksize);
                    }
                }
            }
        }
        private unsafe void FillChunk(Chunk destination, int destinationchunksize,
            int sourcex, int sourcey, int sourcez, int[,,] source)
        {
            for (int x = 0; x < destinationchunksize; x++)
            {
                for (int y = 0; y < destinationchunksize; y++)
                {
                    for (int z = 0; z < destinationchunksize; z++)
                    {
                        //if (x + sourcex < source.GetUpperBound(0) + 1
                        //    && y + sourcey < source.GetUpperBound(1) + 1
                        //    && z + sourcez < source.GetUpperBound(2) + 1)
                        {
                            game.SetBlockInChunk(destination, MapUtilCi.Index3d(x, y, z, destinationchunksize, destinationchunksize)
                                , source[x + sourcex, y + sourcey, z + sourcez]);
                        }
                    }
                }
            }
        }
        #endregion

        public TextureAtlasConverter d_TextureAtlasConverter;


        public void UseTerrainTextures(Dictionary<string, int> textureIds)
        {
            //todo bigger than 32x32
            int tilesize = 32;
            Bitmap atlas2d = new Bitmap(tilesize * atlas2dtiles, tilesize * atlas2dtiles);
            IFastBitmap atlas2dFast;
            if (IsMono) { atlas2dFast = new FastBitmapDummy(); } else { atlas2dFast = new FastBitmap(); }
            atlas2dFast.bmp = atlas2d;
            atlas2dFast.Lock();
            foreach (var k in textureIds)
            {
                using (Bitmap bmp = new Bitmap(d_GetFile.GetFile(k.Key + ".png")))
                {
                    IFastBitmap bmpFast;
                    if (IsMono) { bmpFast = new FastBitmapDummy(); } else { bmpFast = new FastBitmap(); }
                    bmpFast.bmp = bmp;
                    bmpFast.Lock();
                    int x = k.Value % game.texturesPacked();
                    int y = k.Value / game.texturesPacked();
                    for (int xx = 0; xx < tilesize; xx++)
                    {
                        for (int yy = 0; yy < tilesize; yy++)
                        {
                            int c = bmpFast.GetPixel(xx, yy);
                            atlas2dFast.SetPixel(x * tilesize + xx, y * tilesize + yy, c);
                        }
                    }
                    bmpFast.Unlock();
                }
            }
            atlas2dFast.Unlock();
            UseTerrainTextureAtlas2d(atlas2d);
        }
        public void UseTerrainTextureAtlas2d(Bitmap atlas2d)
        {
            game.terrainTexture = d_The3d.LoadTexture(atlas2d);
            List<int> terrainTextures1d = new List<int>();
            {
                terrainTexturesPerAtlas = atlas1dheight / (atlas2d.Width / atlas2dtiles);
                List<Bitmap> atlases1d = d_TextureAtlasConverter.Atlas2dInto1d(atlas2d, atlas2dtiles, atlas1dheight);
                foreach (Bitmap bmp in atlases1d)
                {
                    int texture = LoadTexture(bmp);
                    terrainTextures1d.Add(texture);
                    bmp.Dispose();
                }
            }
            game.terrainTextures1d = terrainTextures1d.ToArray();
        }
        int maxTextureSize; // detected at runtime
        public int atlas1dheight { get { return maxTextureSize; } }
        public int atlas2dtiles = GlobalVar.MAX_BLOCKTYPES_SQRT; // 16x16



        public int LoadTexture(Stream file)
        {
            using (file)
            {
                using (Bitmap bmp = new Bitmap(file))
                {
                    return LoadTexture(bmp);
                }
            }
        }

        Dictionary<TextAndSize, int[]> textsizes = new Dictionary<TextAndSize, int[]>();
        public SizeF TextSize_(string text, float fontsize)
        {
            int[] size;
            if (textsizes.TryGetValue(new TextAndSize() { text = text, size = fontsize }, out size))
            {
                return new SizeF(size[0], size[1]);
            }
            IntRef width = new IntRef();
            IntRef height = new IntRef();
            game.platform.TextSize(text, fontsize, width, height);
            size = new int[2];
            size[0] = width.value;
            size[1] = height.value;
            textsizes[new TextAndSize() { text = text, size = fontsize }] = size;
            return new SizeF(width.value, height.value);
        }
        public void Draw2dText_(string text, float x, float y, float fontsize, Color? color)
        {
            int? c = null;
            if (color != null)
            {
                c = Game.ColorFromArgb(color.Value.A, color.Value.R, color.Value.G, color.Value.B);
            }
            Draw2dText_(text, x, y, fontsize, c, false);
        }
        public void Draw2dText_(string text, float x, float y, float fontsize, int? color, bool enabledepthtest)
        {
            FontCi font = FontCi.Create("Arial", fontsize, 0);
            Draw2dText_(text, font, x, y, color, false);
        }

        public void Draw2dText_(string text, FontCi font, float x, float y, Color? color)
        {
            int? c = null;
            if (color != null)
            {
                c = Game.ColorFromArgb(color.Value.A, color.Value.R, color.Value.G, color.Value.B);
            }
            Draw2dText_(text, font, x, y, c, false);
        }
        public void Draw2dText_(string text, FontCi font, float x, float y, int? color, bool enabledepthtest)
        {
            IntRef c = null;
            if (color != null)
            {
                c = IntRef.Create(color.Value);
            }
            game.Draw2dText(text, font, x, y, c, enabledepthtest);
        }


        public void Draw2dBitmapFile_(string filename, float x1, float y1, float width, float height)
        {
            if (!textures.ContainsKey(filename))
            {
                textures[filename] = LoadTexture(d_GetFile.GetFile(filename));
            }
            Draw2dTexture_(textures[filename], x1, y1, width, height, null);
        }

        public void Draw2dTexture_(int textureid, float x1, float y1, float width, float height, int? inAtlasId)
        {
            Draw2dTexture_(textureid, x1, y1, width, height, inAtlasId, Color.White);
        }
        public void Draw2dTexture_(int textureid, float x1, float y1, float width, float height, int? inAtlasId, Color color)
        {
            Draw2dTexture_(textureid, x1, y1, width, height, inAtlasId, color, false);
        }
        public void Draw2dTexture_(int textureid, float x1, float y1, float width, float height, int? inAtlasId, Color color, bool enabledepthtest)
        {
            Draw2dTexture_(textureid, x1, y1, width, height, inAtlasId, game.texturesPacked(), Game.ColorFromArgb(color.A,color.R, color.G, color.B), enabledepthtest);
        }
        public void Draw2dTexture_(int textureid, float x1, float y1, float width, float height, int? inAtlasId, int atlastextures, int color, bool enabledepthtest)
        {
            IntRef inatlasid = null;
            if (inAtlasId != null)
            {
                inatlasid = IntRef.Create(inAtlasId.Value);
            }
            game.Draw2dTexture(textureid, x1, y1, width, height, inatlasid, atlastextures, color, enabledepthtest);
        }

        public void Draw2dTextures_(Draw2dData[] todraw, int textureid)
        {
            Draw2dTextures(todraw, todraw.Length, textureid, 0);
        }

        public Dictionary<string, int> textures = new Dictionary<string, int>();
        public void Set3dProjection()
        {
            Set3dProjection(zfar());
        }

        public void Set3dProjection(float zfar)
        {
            game.Set3dProjection(zfar, currentfov());
        }

        public int sunlight { get { return game.sunlight_; } set { game.sunlight_ = value; } }

        public bool ShadowsFull { get { return false; } set { } }

        internal int maxlight { get { return terrainRenderer.maxlight(); } }
        public void RedrawAllBlocks() { terrainRenderer.RedrawAllBlocks(); }
        public FontType Font;
    }

    public class GetBlockHeight_ : DelegateGetBlockHeight
    {
        public static GetBlockHeight_ Create(ManicDiggerGameWindow w_)
        {
            GetBlockHeight_ g = new GetBlockHeight_();
            g.w = w_;
            return g;
        }
        internal ManicDiggerGameWindow w;
        public override float GetBlockHeight(int x, int y, int z)
        {
            return w.getblockheight(x, y, z);
        }
    }

    public class IsBlockEmpty_ : DelegateIsBlockEmpty
    {
        public static IsBlockEmpty_ Create(ManicDiggerGameWindow w_)
        {
            IsBlockEmpty_ g = new IsBlockEmpty_();
            g.w = w_;
            return g;
        }
        ManicDiggerGameWindow w;
        public override bool IsBlockEmpty(int x, int y, int z)
        {
            return w.IsTileEmptyForPhysics(x, y, z);
        }
    }

    struct TextAndSize
    {
        public string text;
        public float size;
        public override int GetHashCode()
        {
            if (text == null)
            {
                return 0;
            }
            return text.GetHashCode() ^ size.GetHashCode();
        }
        public override bool Equals(object obj)
        {
            if (obj is TextAndSize)
            {
                TextAndSize other = (TextAndSize)obj;
                return this.text == other.text && this.size == other.size;
            }
            return base.Equals(obj);
        }
    }

    public interface IResetMap
    {
        void Reset(int sizex, int sizey, int sizez);
    }
}
