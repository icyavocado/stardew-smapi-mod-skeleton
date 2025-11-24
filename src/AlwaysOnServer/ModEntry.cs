using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;

using Always_On_Server.Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;
using SObject = StardewValley.Object;

using Newtonsoft.Json;

namespace Always_On_Server
{
    public static class F
    {
        public static string Dump(object obj)
        {
            return JsonConvert.SerializeObject(obj);
        }
    }

    public class ModEntry : Mod
    {
        /// <summary>The mod configuration from the player.</summary>
        private ModConfig Config;

        // private int skipTicks; //stores 1s game ticks for skip code
        private int gameClockTicks; //stores in game clock change 
        private bool IsEnabled;  //stores if the the server mod is enabled 
        public int bedX;
        public int bedY;
        public bool clientPaused;

        //debug tools
        private bool debug;
        private bool shippingMenuActive;

        private readonly Dictionary<string, int> PreviousFriendships = new Dictionary<string, int>();  //stores friendship values

        public int connectionsCount = 1;

        private bool eventCommandUsed;
        //
        //variables for timeout reset code
        private int timeOutTicksForReset;
        private int shippingMenuTimeoutTicks;

        //variables for current time and date
        int currentTime = Game1.timeOfDay;
        SDate today = SDate.Now();
        Dictionary<string, (string Name, int Start, int End, bool Available, int CountDown, int ResetTick, string Location)> importantDates = new Dictionary<string, (string, int, int, bool, int, int, string)>
        {
            { new SDate(13, "spring").ToLocaleString(false), ("eggFestival", 900, 1400, false, 0, 0, "Town") },
            { new SDate(24, "spring").ToLocaleString(false), ("flowerDance", 900, 1400, false, 0, 0, "Forest") },
            { new SDate(11, "summer").ToLocaleString(false), ("luau", 900, 1400, false, 0, 0, "Beach") },
            { new SDate(28, "summer").ToLocaleString(false), ("danceOfMoonlightJellies", 2200, 2400, false, 0, 0, "Beach") },
            { new SDate(16, "fall").ToLocaleString(false), ("stardewValleyFair", 900, 1500, false, 0, 0, "Town") },
            { new SDate(27, "fall").ToLocaleString(false), ("spiritsEve", 2200, 2350, false, 0, 0, "Town") },
            { new SDate(8, "winter").ToLocaleString(false), ("festivalOfIce", 900, 1400, false, 0, 0, "Forest") },
            { new SDate(25, "winter").ToLocaleString(false), ("feastOfWinterStar", 900, 1400, false, 0, 0, "Town") },
            { new SDate(1, "spring", 3).ToLocaleString(true), ("ghostOfGrandpa", 0, 0, false, 0, 0, "") }
        };

        Dictionary<string, int> skills = new Dictionary<string, int>
        {
            { "Farming", Farmer.getSkillNumberFromName("farming") },
            { "Mining", Farmer.getSkillNumberFromName("mining") },
            { "Foraging", Farmer.getSkillNumberFromName("foraging") },
            { "Fishing", Farmer.getSkillNumberFromName("fishing") },
            { "Combat", Farmer.getSkillNumberFromName("combat") }
        };

        Dictionary<string, int> skipTicks = new Dictionary<string, int>();

        private string saveDirectory;
        private const string LogPath = "Mods/AlwaysOnServer/logs.txt";
        StreamWriter logs;

        SDate currentDate = SDate.Now();

        SDate currentDateForReset = SDate.Now();
        SDate danceOfJelliesForReset = new SDate(28, "summer");
        SDate spiritsEveForReset = new SDate(27, "fall");
        //////////////////////////

        // Debug helper: logs variables when `debug` is true
        private void Debug(string where, params object[] variables)
        {
            if (!this.debug) return;
            try
            {
                var parts = variables?.Select(v => v == null ? "null" : JsonConvert.SerializeObject(v)) ?? Enumerable.Empty<string>();
                string msg = DateTime.UtcNow.ToString("s") + " | " + where + " | " + string.Join(", ", parts);
                // log to SMAPI console
                this.Monitor.Log(msg, LogLevel.Debug);

                // also append to local log file
                try
                {
                    var dir = Path.GetDirectoryName(LogPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.AppendAllText(LogPath, msg + Environment.NewLine);
                }
                catch (Exception) { /* don't let file IO break debug logging */ }
            }
            catch (Exception ex)
            {
                try { this.Monitor.Log(where + " | (debug serialization error: " + ex.Message + ")", LogLevel.Debug); } catch { }
            }
        }

        public override void Entry(IModHelper helper)
        {

            this.Debug("Entry - start", helper?.GetType().Name, this.Config);

            this.Config = this.Helper.ReadConfig<ModConfig>();

            helper.ConsoleCommands.Add("server", "Toggles headless server on/off", this.ServerToggle);
            helper.ConsoleCommands.Add("debug_server", "Turns debug mode on/off, lets server run when no players are connected", this.DebugToggle);

            helper.ConsoleCommands.Add("test", "Hi", this.Test);
            helper.ConsoleCommands.Add("call", "Hi", this.Call);

            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.GameLoop.Saving += this.OnSaving; // Shipping Menu handler
            helper.Events.GameLoop.OneSecondUpdateTicked += this.OnOneSecondUpdateTicked; //game tick event handler
            helper.Events.GameLoop.TimeChanged += this.OnTimeChanged; // Time of day change handler
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked; //handles various events that should occur as soon as they are available
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.Display.Rendered += this.OnRendered;
            helper.Events.Specialized.UnvalidatedUpdateTicked += OnUnvalidatedUpdateTick; //used bc only thing that gets throug save window
            this.Debug("Entry - events registered");
        }

        private void RestoreLandE(ModData saveContent) {
            this.Debug("RestoreLandE - start", saveContent);
            F.Dump("Hi");
            foreach (KeyValuePair<string, int> skill in skills)
            {
                // Game1.player.setSkillLevel(skill.Key, saveContent.Get($"{skill.Key}Level"));
                // Game1.player.experiencePoints[skill.Value] = saveContent.Get($"{skill.Key}Experience");
            }
        }

        private void BackUpLandE(ModData saveContent) {
            this.Debug("BackUpLandE - start", saveContent);
            F.Dump(saveContent);
            foreach (KeyValuePair<string, int> skill in skills)
            {
                F.Dump(saveContent);
                // saveContent.Set($"{skill.Key}Level", Game1.player.GetSkillLevel(skill.Value));
                // saveContent.Set($"{skill.Key}Experience", Game1.player.experiencePoints[skill.Value]);
            }
        }

        private void LoadLandE(ModData? saveContent = null)
        {
            this.Debug("LoadLandE - start", saveContent);
            saveDirectory = $"{Constants.SaveFolderName}.json";

            // Load save content if none provided
            if (saveContent == null)
            {
                try
                {
                    saveContent = this.Helper.Data.ReadJsonFile<ModData>(saveDirectory);
                }
                catch (Exception ex)
                {
                    Game1.chatBox.addInfoMessage($"Failed to load data. Abort. Error: {ex.Message}");
                }
            }

            if (saveContent == null)
            {
                this.Debug("LoadLandE - failed to load saveContent", saveContent, saveDirectory);
                Game1.chatBox.addInfoMessage("Failed to load save data. Abort.");
                return;
            }

            this.Debug("LoadLandE - calling RestoreLandE", saveContent);
            this.RestoreLandE(saveContent);
        }

        private void Test(string command, string[] args)
        {
            this.Debug("Test - start", command, args);
            if (Context.IsWorldReady)
            {
                this.debug = !debug;
                this.Monitor.Log($"Server Debug {(debug ? "On" : "Off")}", LogLevel.Info);
                this.Debug("Test - toggled debug", this.debug);
            }
        }

        private void Call(string command, string[] args)
        {
            this.Debug("Call - start", command, args);
            if (Context.IsWorldReady)
            {
                this.Log(command);
                this.Debug("Call - invoking CallByName", command);
                this.CallByName(command);
            }
        }

        private void Log(dynamic? obj)
        {
            string text = JsonConvert.SerializeObject(obj);
            this.Monitor.Log(text, LogLevel.Info);
        }

        private void SaveLandE(ModData? saveContent = null)
        {
            saveDirectory = $"{Constants.SaveFolderName}.json";
            this.Log(Constants.SaveFolderName);

            // Load existing container if none was provided
            if (saveContent == null)
            {
                try
                {
                    saveContent = this.Helper.Data.ReadJsonFile<ModData>(saveDirectory) ?? new ModData();
                }
                catch (Exception ex)
                {
                    Game1.chatBox.addInfoMessage($"Failed to load data. Abort. Error: {ex.Message}");
                }
            }

            if (saveContent == null)
            {
                Game1.chatBox.addInfoMessage("Failed to load save data. Abort.");
                return;
            }

            this.BackUpLandE(saveContent);

            try
            {
                this.Helper.Data.WriteJsonFile<ModData>(saveDirectory, saveContent);
            }
            catch (Exception ex)
            {
                Game1.chatBox.addInfoMessage($"Failed to save data. Abort. Error: {ex.Message}");
            }
        }

        private void SetLandEToMax() {
            this.Debug("SetLandEToMax - start");
            foreach (KeyValuePair<string, int> skill in skills)
            {
                Game1.player.setSkillLevel(skill.Key, 10);
                // Set experiencePoints to 1 to prevent leveling up at the end of the day
                Game1.player.experiencePoints[skill.Value] = 1;
            }
            this.Debug("SetLandEToMax - finished");
        }


        /// <summary>Raised after the player loads a save slot and the world is initialised.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            this.Debug("OnSaveLoaded - start", sender?.GetType().Name, e?.GetType().Name);
            if (!Game1.IsServer) return;

            this.SetLandEToMax();

            IsEnabled = true;

            Game1.chatBox.addInfoMessage("The Host is in Server Mode!");
            this.Monitor.Log("Server Mode On!", LogLevel.Info);

            // Ensure one-off event dictionary keys are initialized to safe defaults
            if (!oneOffEvent.ContainsKey("SewersUnlock")) oneOffEvent["SewersUnlock"] = Game1.player.hasRustyKey;
            if (!oneOffEvent.ContainsKey("CommunityCenterRun")) oneOffEvent["CommunityCenterRun"] = Game1.player.eventsSeen.Contains("191393");
            if (!oneOffEvent.ContainsKey("JojaMemberRun")) oneOffEvent["JojaMemberRun"] = Game1.player.eventsSeen.Contains("502261");
            if (!oneOffEvent.ContainsKey("FishingRod")) oneOffEvent["FishingRod"] = Game1.player.eventsSeen.Contains("739330");
            this.Debug("OnSaveLoaded - initialized oneOffEvent", oneOffEvent);
        }

        //debug for running with no one online
        private void DebugToggle(string command, string[] args)
        {
            this.Debug("DebugToggle - start", command, args);
            if (Context.IsWorldReady)
            {
                this.debug = !debug;
                this.Monitor.Log($"Server Debug {(debug ? "On" : "Off")}", LogLevel.Info);
                this.Debug("DebugToggle - toggled debug", this.debug);
            }
        }

        //draw textbox rules
        public static void DrawTextBox(int x, int y, SpriteFont font, string message, int align = 0, float colorIntensity = 1f)
        {
            SpriteBatch spriteBatch = Game1.spriteBatch;
            int width = (int)font.MeasureString(message).X + 32;
            int num = (int)font.MeasureString(message).Y + 21;
            switch (align)
            {
                case 0:
                    IClickableMenu.drawTextureBox(spriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60), x, y, width, num + 4, Color.White * colorIntensity);
                    Utility.drawTextWithShadow(spriteBatch, message, font, new Vector2(x + 16, y + 16), Game1.textColor);
                    break;
                case 1:
                    IClickableMenu.drawTextureBox(spriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60), x - width / 2, y, width, num + 4, Color.White * colorIntensity);
                    Utility.drawTextWithShadow(spriteBatch, message, font, new Vector2(x + 16 - width / 2, y + 16), Game1.textColor);
                    break;
                case 2:
                    IClickableMenu.drawTextureBox(spriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60), x - width, y, width, num + 4, Color.White * colorIntensity);
                    Utility.drawTextWithShadow(spriteBatch, message, font, new Vector2(x + 16 - width, y + 16), Game1.textColor);
                    break;
            }
        }

        /// <summary>Raised after the game draws to the sprite patch in a draw tick, just before the final sprite batch is rendered to the screen.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnRendered(object sender, RenderedEventArgs e)
        {
            this.Debug("OnRendered - start", sender?.GetType().Name, e?.GetType().Name);
            //draw a textbox in the top left corner saying Server On
            if (Game1.options.enableServer && IsEnabled && Game1.server != null)
            {
                int connectionsCount = Game1.server.connectionsCount;
                DrawTextBox(5, 100, Game1.dialogueFont, "Server Mode On");
                DrawTextBox(5, 180, Game1.dialogueFont, $"Press {this.Config.serverHotKey} On/Off");
                int profitMargin = this.Config.profitmargin;
                DrawTextBox(5, 260, Game1.dialogueFont, $"Profit Margin: {profitMargin}%");
                DrawTextBox(5, 340, Game1.dialogueFont, $"{connectionsCount} Players Online");
                if (Game1.server.getInviteCode() != null)
                {
                    string inviteCode = Game1.server.getInviteCode();
                    this.Debug("OnRendered - invite code", inviteCode);
                    DrawTextBox(5, 420, Game1.dialogueFont, $"Invite Code: {inviteCode}");
                }
            }
        }


        public void EnableServer()
        {
            this.Debug("EnableServer - start");
            Helper.ReadConfig<ModConfig>();
            IsEnabled = true;

            this.Monitor.Log("Server Mode On!", LogLevel.Info);
            Game1.chatBox.addInfoMessage("The Host is in Server Mode!");

            Game1.displayHUD = true;
            Game1.addHUDMessage(new HUDMessage("Server Mode On!"));

            Game1.options.pauseWhenOutOfFocus = false;

            this.SaveLandE();
            this.SetLandEToMax();

            Game1.addHUDMessage(new HUDMessage("Server Mode COMPLETE!"));
            this.Debug("EnableServer - complete", IsEnabled);
        }

        public void DisableServer()
        {
            this.Debug("DisableServer - start");
            IsEnabled = false;
            this.Monitor.Log("The server off!", LogLevel.Info);

            Game1.chatBox.addInfoMessage("The Host has returned!");

            Game1.displayHUD = true;

            this.LoadLandE();

            Game1.addHUDMessage(new HUDMessage("Server Mode Off!"));
            this.Debug("DisableServer - complete", IsEnabled);
        }

        // toggles server on/off with console command "server"
        private void ServerToggle(string? command = "", string[]? args = null)
        {
            if (!Context.IsWorldReady) return;
            this.CallByName(IsEnabled ? "DisableServer" : "EnableServer");
        }


        private void ResetPlayerLocation() {
        // warp farmer on button press
            this.Debug("ResetPlayerLocation - start", Game1.player?.currentLocation?.Name);
            if (Game1.player.currentLocation is FarmHouse)
            {
                Game1.warpFarmer("Farm", 64, 15, false);
                this.Debug("ResetPlayerLocation - warped to Farm");
            }
            else
            {
                this.GetBedCoordinates();
                Game1.warpFarmer("Farmhouse", bedX, bedY, false);
                this.Debug("ResetPlayerLocation - warped to Farmhouse", bedX, bedY);
            }
        }
        /// <summary>Raised after the player presses a button on the keyboard, controller, or mouse.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || e.Button != this.Config.serverHotKey) return;
            //toggles server on/off with configurable hotkey
            this.ServerToggle();

            this.ResetPlayerLocation();
        }

        /// <summary>Raised once per second after the game state is updated.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnOneSecondUpdateTicked(object sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!IsEnabled)
            {
                Game1.netWorldState.Value.IsPaused = false;
                return;
            }

            // Pause the game if noone is connected
            if (Game1.otherFarmers.Count <= 0 || clientPaused) Game1.netWorldState.Value.IsPaused = true;

            this.ProcessCommand();

            //Invite Code Copier
            if (this.Config.copyInviteCodeToClipboard)
            {
                string id = "logs_invite";
                skipTicks[id] = skipTicks.ContainsKey(id) ? skipTicks[id] + 1 : 0;
                if (skipTicks[id] >= 3)
                {
                    try
                    {
                        using (var writer = new StreamWriter(LogPath, append: true))
                        {
                            writer.WriteLine($"Invite Code: {Game1.server.getInviteCode()}\nPlayer Count: {Game1.server.connectionsCount}");
                        }
                    }
                    catch (Exception ex)
                    {
                        this.SendChatMessage(ex.Message);
                    }
                    skipTicks[id] = 0;
                }
            }

            if (Game1.activeClickableMenu is DialogueBox) Game1.activeClickableMenu.receiveLeftClick(10, 10);
            if (Game1.CurrentEvent != null && Game1.CurrentEvent.skippable)
            {
                string id = Game1.CurrentEvent.id;
                skipTicks[id] = skipTicks.ContainsKey(id) ? skipTicks[id] + 1 : 0;
                if (skipTicks[id] >= 3) {
                    Game1.CurrentEvent.skipEvent();
                    skipTicks[id] = 0;
                }
            }


            if (this.PreviousFriendships.Any())
            {
                foreach (string key in Game1.player.friendshipData.Keys)
                {
                    Friendship friendship = Game1.player.friendshipData[key];
                    if (this.PreviousFriendships.TryGetValue(key, out int oldPoints) && oldPoints > friendship.Points)
                        friendship.Points = oldPoints;
                }
            }

            this.PreviousFriendships.Clear();
            foreach (var pair in Game1.player.friendshipData.FieldDict)
                this.PreviousFriendships[pair.Key] = pair.Value.Value.Points;


            if (Game1.CurrentEvent != null && Game1.CurrentEvent.isFestival) this.FestivalLoop();
        }

        private void FestivalLoop()
        {
            this.Debug("FestivalLoop - start", currentDate.ToLocaleString(false), currentTime);
            string key = currentDate.ToLocaleString(false);
            if (!importantDates.TryGetValue(key, out var festivalInfo)) { this.Debug("FestivalLoop - no festival entry for key", key); return; }
            if (string.IsNullOrEmpty(festivalInfo.Name)) { this.Debug("FestivalLoop - festival name empty"); return; }

            dynamic config = this.Config;

            int configCD = config[$"{festivalInfo.Name}CountDownConfig"];
            if (eventCommandUsed)
            {
                festivalInfo.CountDown = configCD;
                eventCommandUsed = false;
                this.CallByName($"{festivalInfo.Name}EventCmd");
            }
            festivalInfo.CountDown += 1;

            float notificationTime = configCD / 60f;

            if (festivalInfo.CountDown == 1)
            {
                this.SendChatMessage($"The {festivalInfo.Name} will begin in {notificationTime:0.#} minutes.");
                this.CallByName($"{festivalInfo.Name}EventCmd");
            }
            else if (festivalInfo.CountDown == configCD + 1)
            {
                this.CallByName($"{festivalInfo.Name}Action");
            }
            else if (festivalInfo.CountDown == configCD + 5)
            {
                festivalInfo.ResetTick += 1;
            }

            int configTimeOut = config[$"{festivalInfo.Name}TimeOutConfig"];
            if (festivalInfo.ResetTick >= configTimeOut + 180)
            {
                Game1.options.setServerMode("offline");
            }

            if (festivalInfo.CountDown >= configTimeOut) this.LeaveFestival();

            // save changes back to dictionary (tuples are value types)
            importantDates[key] = festivalInfo;
            this.Debug("FestivalLoop - end", festivalInfo);
        }

        private void eggFestivalAction()
        {
            this.Helper.Reflection.GetMethod(Game1.CurrentEvent, "answerDialogueQuestion").Invoke(Game1.getCharacterFromName("Lewis"), "yes");
        }

        private void flowerDanceAction()
        {
            this.Helper.Reflection.GetMethod(Game1.CurrentEvent, "answerDialogueQuestion").Invoke(Game1.getCharacterFromName("Lewis"), "yes");
        }

        private void luauEventCmd()
        {
            var item = new SObject("Starfruit", 1, false, -1, 3);
            this.Helper.Reflection.GetMethod(new Event(), "addItemToLuauSoup").Invoke(item, Game1.player);
        }

        private void luauAction()
        {
            this.Helper.Reflection.GetMethod(Game1.CurrentEvent, "answerDialogueQuestion").Invoke(Game1.getCharacterFromName("Lewis"), "yes");
        }

        private void danceOfMoonlightJelliesAction()
        {
            this.Helper.Reflection.GetMethod(Game1.CurrentEvent, "answerDialogueQuestion").Invoke(Game1.getCharacterFromName("Lewis"), "yes");
        }

        private void stardewValleyFairAction()
        {
            this.Helper.Reflection.GetMethod(Game1.CurrentEvent, "answerDialogueQuestion").Invoke(Game1.getCharacterFromName("Lewis"), "yes");
        }

        private void festivalOfIceAction()
        {
            this.Helper.Reflection.GetMethod(Game1.CurrentEvent, "answerDialogueQuestion").Invoke(Game1.getCharacterFromName("Lewis"), "yes");
        }

        private void ProcessCommand() {
            this.Debug("ProcessCommand - start", IsEnabled, Context.IsWorldReady);
            if (!IsEnabled || !Context.IsWorldReady) return;

            List<ChatMessage> messages = this.Helper.Reflection.GetField<List<ChatMessage>>(Game1.chatBox, "messages").GetValue();

            if (messages.Count <= 0) return;

            var messagetoconvert = messages[messages.Count - 1].message;
            string actualmessage = ChatMessage.makeMessagePlaintext(messagetoconvert, true);
            string[] parts = actualmessage?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            string lastFragment = parts.Length >= 2 ? parts[1] : null;

            if (lastFragment == null || lastFragment.Length == 0 || lastFragment[0] != '!') return;

            switch (lastFragment)
            {
                case "!sleep":
                    bool isTimeToSleep = currentTime >=this.Config.timeOfDayToSleep;
                    if (isTimeToSleep) this.GoToBed();
                    this.SendChatMessage(isTimeToSleep ? "Going to bed!" : $"Too early. Try after {this.Config.timeOfDayToSleep}.");
                    break;
                case "!sleepnow":
                    this.GoToBed();
                    this.SendChatMessage("Going to bed.");
                    break;
                case "!festival":
                    this.GoToFestival();
                    var festivalInfo = importantDates[currentDate.ToLocaleString(false)];
                    if (festivalInfo.Name != "") {
                        this.SendChatMessage(currentTime < festivalInfo.End + 10 ? $"Going to festival {festivalInfo.Name}" : $"Ending festival {festivalInfo.Name}. Go to bed.");
                    } else {
                        this.SendChatMessage("No festival today. Abort.");
                    }
                    break;
                case "!event":
                    if (Game1.CurrentEvent == null || !Game1.CurrentEvent.isFestival)
                    {
                        this.SendChatMessage("Not a festival or event. Abort.");
                        break;
                    }

                    festivalInfo = importantDates[currentDate.ToLocaleString(false)];
                    if (festivalInfo.Name != "")
                    {
                        eventCommandUsed = true;
                        festivalInfo.Available = true;
                    }
                    break;
                case "!leave":
                    if (Game1.CurrentEvent == null || !Game1.CurrentEvent.isFestival)
                    {
                        this.SendChatMessage("Not a festival or event. Abort.");
                        break;
                    }
                    this.LeaveFestival();
                    break;
                case "!unstick":
                    this.ResetPlayerLocation();
                    break;
                case "!pause":
                    if (!this.Config.clientsCanPause) {
                        this.SendChatMessage("You shall not pause the game.");
                        break;
                    }
                    Game1.netWorldState.Value.IsPaused = true;
                    clientPaused = true;
                    this.SendChatMessage("Game Paused");
                    break;
                case "!unpause":
                    if (!this.Config.clientsCanPause) {
                        this.SendChatMessage("You shall not un-pause the game.");
                        break;
                    }
                    Game1.netWorldState.Value.IsPaused = false;
                    clientPaused = false;
                    this.SendChatMessage("Game UnPaused");
                    break;
                case "!invite":
                    if (!Game1.options.enableServer) {
                        this.SendChatMessage("Server not enabled. No invite code generated.");
                        break;
                    }
                    string inviteCode = Game1.server.getInviteCode();
                    string outMsg = $"Invite Code: {inviteCode}";
                    if (this.Config.copyInviteCodeToClipboard) {
                        DesktopClipboard.SetText(inviteCode);
                        outMsg += " added to your clipboard";
                    }
                    this.Debug("ProcessCommand - invite", inviteCode, this.Config.copyInviteCodeToClipboard);
                    this.SendChatMessage($"{outMsg}.");
                    break;
                default:
                    this.SendChatMessage($"Command not supported. Abort. Command receieved: {lastFragment}.");
                    break;
            }
        }

        private void GoToFestival() {
            this.Debug("GoToFestival - start", currentDate.ToLocaleString(false), currentTime);
            string key = currentDate.ToLocaleString(false);
            if (!importantDates.TryGetValue(key, out var festivalInfo)) { this.Debug("GoToFestival - no festival entry", key); return; }
            if (string.IsNullOrEmpty(festivalInfo.Name)) { this.Debug("GoToFestival - no festival today"); return; }
            if (currentTime >= festivalInfo.Start && currentTime <= festivalInfo.End)
            {
                Game1.netReady.SetLocalReady("festivalStart", true);
                Game1.activeClickableMenu = new ReadyCheckDialog("festivalStart", true, who =>
                {
                    Game1.exitActiveMenu();
                    if (!string.IsNullOrEmpty(festivalInfo.Location)) Game1.warpFarmer(festivalInfo.Location, 1, 20, 1);
                }
                );
                festivalInfo.Available = true;
            }
            else if (currentTime >= festivalInfo.End + 10)
            {
                festivalInfo.Available = false;
                Game1.options.setServerMode("online");
                festivalInfo.CountDown = 0;
                this.GoToBed();
            }

            importantDates[key] = festivalInfo;
            this.Debug("GoToFestival - end", festivalInfo);
        }

        private bool IsClientConnected()
        {
            this.Debug("IsClientConnected - start", Game1.otherFarmers?.Count);
            bool connected = Game1.otherFarmers.Count >= 1;
            this.Debug("IsClientConnected - result", connected);
            return connected;
        }

        private void LockPlayerChests()
        {
            this.Debug("LockPlayerChests - start");
            foreach (Farmer farmer in Game1.getOnlineFarmers())
            {
                if (farmer.currentLocation is Cabin cabin && farmer != cabin.owner)
                {
                    //locks player inventories
                    NetMutex playerinventory = this.Helper.Reflection.GetField<NetMutex>(cabin, "inventoryMutex").GetValue();
                    playerinventory.RequestLock();

                    //locks all chests
                    foreach (SObject x in cabin.objects.Values)
                    {
                        if (x is Chest chest) chest.mutex.RequestLock();
                    }
                    //locks fridge
                    cabin.fridge.Value.mutex.RequestLock();
                }
            }
            this.Debug("LockPlayerChests - end");
        }

        /// <summary>Raised after the game state is updated (≈60 times per second).</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {

            // High-frequency update (≈60Hz). Avoid debug logging here to prevent spam.
            if (!IsEnabled) return;

            //lockPlayerChests
            if (this.Config.lockPlayerChests) this.LockPlayerChests();

            //petchoice
            if (!Game1.player.hasPet())
            {
                this.Helper.Reflection.GetMethod(new Event(), "namePet").Invoke(this.Config.petname.Substring(0));
            }
            if (Game1.player.hasPet() && Game1.getCharacterFromName(Game1.player.getPetName()) is Pet pet)
            {
                pet.Name = this.Config.petname.Substring(0);
                pet.displayName = this.Config.petname.Substring(0);
            }

            //cave choice unlock
            if (!Game1.player.eventsSeen.Contains("65"))
            {
                Game1.player.eventsSeen.Add("65");


                if (this.Config.farmcavechoicemushrooms)
                {
                    Game1.MasterPlayer.caveChoice.Value = 2;
                    (Game1.getLocationFromName("FarmCave") as FarmCave).setUpMushroomHouse();
                }
                else
                {
                    Game1.MasterPlayer.caveChoice.Value = 1;
                }
            }

            //community center unlock
            if (!Game1.player.eventsSeen.Contains("611439"))
            {
                Game1.player.eventsSeen.Add("611439");
                Game1.MasterPlayer.mailReceived.Add("ccDoorUnlock");
            }

            if (this.Config.upgradeHouse != 0 && Game1.player.HouseUpgradeLevel != this.Config.upgradeHouse)
            {
                Game1.player.HouseUpgradeLevel = this.Config.upgradeHouse;
            }

            // just turns off server mod if the game gets exited back to title screen
            if (Game1.activeClickableMenu is TitleMenu) IsEnabled = false;
        }

        private void FestivalNotification()
        {
            this.Debug("FestivalNotification - start", IsEnabled, Game1.otherFarmers.Count);
            if (!IsEnabled) { this.Debug("FestivalNotification - not enabled"); return; }
            if (!this.IsClientConnected()) { this.Debug("FestivalNotification - no clients"); return; }

            gameClockTicks += 1;
            if (gameClockTicks < 3) { this.Debug("FestivalNotification - waiting for ticks", gameClockTicks); return; }

            var festivalInfo = importantDates[currentDate.ToLocaleString(false)];
            if (string.IsNullOrEmpty(festivalInfo.Name)) { this.Debug("FestivalNotification - no festival"); return; }

            // skip notification outside of the expected range (not between 600 and 630)
            if (currentTime < 600 || currentTime > 630) { this.Debug("FestivalNotification - outside time window", currentTime); return; }

            this.SendChatMessage($"{festivalInfo.Name} Today!");
            this.SendChatMessage($"I will not be in bed until after . {festivalInfo.End}");
            this.FestivalLoop();

            gameClockTicks = 0;
            this.Debug("FestivalNotification - end");
        }

        private void DailyLoop()
        {
            this.Debug("DailyLoop - start", IsEnabled, Game1.otherFarmers.Count);
            if (!IsEnabled) { this.Debug("DailyLoop - not enabled"); return; }
            if (!this.IsClientConnected()) { this.Debug("DailyLoop - no clients"); return; }

            var festivalInfo = importantDates[currentDate.ToLocaleString(false)];
            if (!string.IsNullOrEmpty(festivalInfo.Name)) { this.Debug("DailyLoop - festival active, skipping"); return; }

            // Check Mail
            if (currentTime == 620)
            {
                for (int i = 0; i < 10; i++)
                {
                    this.Helper.Reflection.GetMethod(Game1.currentLocation, "mailbox").Invoke();
                }
            }

            //go outside
            if (currentTime == 640) Game1.warpFarmer("Farm", 1, 1, false);

            //get fishing rod (standard spam clicker will get through cutscene)
            if (currentTime == 900 && !Game1.player.eventsSeen.Contains("739330"))
            {
                Game1.player.increaseBackpackSize(1);
                Game1.warpFarmer("Beach", 1, 20, 1);
                this.Debug("DailyLoop - gave fishing rod");
            }
            this.Debug("DailyLoop - end");
        }

        private Dictionary<string, bool> oneOffEvent = new Dictionary<string, bool>();
        private void oneOffEventLoop()
        {
            this.Debug("oneOffEventLoop - start", currentTime);
            if (currentTime < 630) { this.Debug("oneOffEventLoop - too early", currentTime); return; }

            if (!oneOffEvent.TryGetValue("SewersUnlock", out var sewers) || !sewers)
            {
                oneOffEvent["SewersUnlock"] = Game1.player.hasRustyKey;
                if (Game1.player.hasRustyKey) return;
                int museumItemCount = Game1.netWorldState.Value.MuseumPieces.Length;
                this.Monitor.Log("Checking museum items: " + museumItemCount.ToString(), LogLevel.Info);
                if (museumItemCount < 60) return;
                Game1.player.eventsSeen.Add("295672");
                Game1.player.eventsSeen.Add("66");
                Game1.player.hasRustyKey = true;
                oneOffEvent["SewersUnlock"] = Game1.player.hasRustyKey;
            }

            if ((!oneOffEvent.TryGetValue("CommunityCenterRun", out var ccRun) || !ccRun) && this.Config.communitycenterrun)
            {
                oneOffEvent["CommunityCenterRun"] = Game1.player.eventsSeen.Contains("191393");
                if (oneOffEvent["CommunityCenterRun"]) return;

                CommunityCenter locationFromName = Game1.getLocationFromName("CommunityCenter") as CommunityCenter;
                for (int index = 0; index < locationFromName.areasComplete.Count; index++)
                {
                    locationFromName.areasComplete[index] = true;
                    Game1.player.eventsSeen.Add("191393");
                }
            }

            if ((!oneOffEvent.TryGetValue("JojaMemberRun", out var jojaRun) || !jojaRun) && !this.Config.communitycenterrun)
            {
                oneOffEvent["JojaMemberRun"] = Game1.player.eventsSeen.Contains("502261");
                if (oneOffEvent["JojaMemberRun"]) return;

                if (Game1.player.Money >= 10000 && !Game1.player.mailReceived.Contains("JojaMember"))
                {
                    Game1.player.Money -= 5000;
                    Game1.player.mailReceived.Add("JojaMember");
                    this.SendChatMessage("Buying Joja Membership");
                }

                if (Game1.player.Money >= 30000 && !Game1.player.mailReceived.Contains("jojaBoilerRoom"))
                {
                    Game1.player.Money -= 15000;
                    Game1.player.mailReceived.Add("ccBoilerRoom");
                    Game1.player.mailReceived.Add("jojaBoilerRoom");
                    this.SendChatMessage("Buying Joja Minecarts");
                }

                if (Game1.player.Money >= 40000 && !Game1.player.mailReceived.Contains("jojaFishTank"))
                {
                    Game1.player.Money -= 20000;
                    Game1.player.mailReceived.Add("ccFishTank");
                    Game1.player.mailReceived.Add("jojaFishTank");
                    this.SendChatMessage("Buying Joja Panning");
                }

                if (Game1.player.Money >= 50000 && !Game1.player.mailReceived.Contains("jojaCraftsRoom"))
                {
                    Game1.player.Money -= 25000;
                    Game1.player.mailReceived.Add("ccCraftsRoom");
                    Game1.player.mailReceived.Add("jojaCraftsRoom");
                    this.SendChatMessage("Buying Joja Bridge");
                }

                if (Game1.player.Money >= 70000 && !Game1.player.mailReceived.Contains("jojaPantry"))
                {
                    Game1.player.Money -= 35000;
                    Game1.player.mailReceived.Add("ccPantry");
                    Game1.player.mailReceived.Add("jojaPantry");
                    this.SendChatMessage("Buying Joja Greenhouse");
                }

                if (Game1.player.Money >= 80000 && !Game1.player.mailReceived.Contains("jojaVault"))
                {
                    Game1.player.Money -= 40000;
                    Game1.player.mailReceived.Add("ccVault");
                    Game1.player.mailReceived.Add("jojaVault");
                    this.SendChatMessage("Buying Joja Bus");
                    Game1.player.eventsSeen.Add("502261");
                }
            }

            if (!oneOffEvent.TryGetValue("FishingRod", out var fishingRod) || !fishingRod)
            {
                oneOffEvent["FishingRod"] = Game1.player.eventsSeen.Contains("739330");
                if (oneOffEvent["FishingRod"]) return;

                //get fishing rod (standard spam clicker will get through cutscene)
                if (currentTime == 900 && !Game1.player.eventsSeen.Contains("739330"))
                {
                    Game1.player.increaseBackpackSize(1);
                    Game1.warpFarmer("Beach", 1, 20, 1);
                    Game1.player.eventsSeen.Add("739330");
                    this.Debug("oneOffEventLoop - fishing rod given");
                }
            }
            this.Debug("oneOffEventLoop - end");
        }

        /// <summary>Raised after the in-game clock time changes.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        public void OnTimeChanged(object sender, TimeChangedEventArgs e)
        {
            this.Debug("OnTimeChanged - start", sender?.GetType().Name, e?.GetType().Name, IsEnabled);
            if (!IsEnabled) { this.Debug("OnTimeChanged - not enabled"); return; }

            currentTime = Game1.timeOfDay;
            currentDate = SDate.Now();

            this.Debug("OnTimeChanged - calling loops", currentTime, currentDate.ToLocaleString(false));
            this.FestivalNotification();

            this.DailyLoop();

            this.oneOffEventLoop();
            this.Debug("OnTimeChanged - end");
        }

        private void GetBedCoordinates()
        {
            int houseUpgradeLevel = Game1.player.HouseUpgradeLevel;
            this.Debug("GetBedCoordinates - start", houseUpgradeLevel);

            switch (houseUpgradeLevel)
            {
                case 0:
                    bedX = 9;
                    bedY = 9;
                    break;
                case 1:
                    bedX = 21;
                    bedY = 4;
                    break;
                default:
                    bedX = 27;
                    bedY = 13;
                    break;
            }
            this.Debug("GetBedCoordinates - result", bedX, bedY);
        }

        private void GoToBed()
        {
            this.Debug("GoToBed - start");
            this.GetBedCoordinates();
            Game1.warpFarmer("Farmhouse", bedX, bedY, false);

            this.Helper.Reflection.GetMethod(Game1.currentLocation, "startSleep").Invoke();
            Game1.displayHUD = true;
            this.Debug("GoToBed - warped to bed", bedX, bedY);
        }

        /// <summary>Raised before the game begins writes data to the save file (except the initial save creation).</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnSaving(object sender, SavingEventArgs e)
        {
            this.Debug("OnSaving - start", sender?.GetType().Name, IsEnabled);
            if (!IsEnabled) { this.Debug("OnSaving - not enabled"); return; }

            // shipping menu "OK" click through code
            this.Monitor.Log("This is the Shipping Menu");
            shippingMenuActive = true;
            if (Game1.activeClickableMenu is ShippingMenu)
            {
                this.Helper.Reflection.GetMethod(Game1.activeClickableMenu, "okClicked").Invoke();
                this.Debug("OnSaving - shipping menu okClicked invoked");
            }
        }

        /// <summary>Raised after the game state is updated (≈60 times per second), regardless of normal SMAPI validation. This event is not thread-safe and may be invoked while game logic is running asynchronously. Changes to game state in this method may crash the game or corrupt an in-progress save. Do not use this event unless you're fully aware of the context in which your code will be run. Mods using this event will trigger a stability warning in the SMAPI console.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnUnvalidatedUpdateTick(object sender, UnvalidatedUpdateTickedEventArgs e)
        {
            this.Debug("OnUnvalidatedUpdateTick - start", Game1.timeOfDay);
            //resets server connection after certain amount of time end of day
            if (Game1.timeOfDay >= this.Config.timeOfDayToSleep || (Game1.timeOfDay == 600 && currentDateForReset != danceOfJelliesForReset && currentDateForReset != spiritsEveForReset && this.Config.endofdayTimeOut != 0))
            {

                timeOutTicksForReset += 1;
                var countdowntoreset = (2600 - this.Config.timeOfDayToSleep) * .01 * 6 * 7 * 60;
                if (timeOutTicksForReset >= (countdowntoreset + (this.Config.endofdayTimeOut * 60)))
                {
                    Game1.options.setServerMode("offline");
                    this.Debug("OnUnvalidatedUpdateTick - set server offline due to timeout");
                }
            }
            if ((currentDateForReset == danceOfJelliesForReset || currentDateForReset == spiritsEveForReset) && this.Config.endofdayTimeOut != 0)
            {
                if (Game1.timeOfDay >= 2400 || Game1.timeOfDay == 600)
                {

                    timeOutTicksForReset += 1;
                    if (timeOutTicksForReset >= (5040 + (this.Config.endofdayTimeOut * 60)))
                    {
                        Game1.options.setServerMode("offline");
                        this.Debug("OnUnvalidatedUpdateTick - set server offline special dates");
                    }
                }

            }
            if (shippingMenuActive && this.Config.endofdayTimeOut != 0)
            {

                shippingMenuTimeoutTicks += 1;
                if (shippingMenuTimeoutTicks >= this.Config.endofdayTimeOut * 60)
                {
                    Game1.options.setServerMode("offline");
                    this.Debug("OnUnvalidatedUpdateTick - set server offline due to shipping menu timeout");
                }

            }

            if (Game1.timeOfDay == 610)
            {
                shippingMenuActive = false;
                Game1.player.difficultyModifier = this.Config.profitmargin * .01f;

                Game1.options.setServerMode("online");
                timeOutTicksForReset = 0;
                shippingMenuTimeoutTicks = 0;
                this.Debug("OnUnvalidatedUpdateTick - morning reset complete");
            }

            if (Game1.timeOfDay == 2600)
            {
                Game1.paused = false;
                this.Debug("OnUnvalidatedUpdateTick - time 2600 unpaused");
            }
        }

        /// <summary>Send a chat message.</summary>
        /// <param name="message">The message text.</param>
        private void SendChatMessage(string message)
        {
            this.Debug("SendChatMessage - start", message);
            Game1.chatBox.activate();
            Game1.chatBox.setText(message);
            Game1.chatBox.chatBox.RecieveCommandInput('\r');
            this.Debug("SendChatMessage - sent", message);
        }

        /// <summary>Leave the current festival, if any.</summary>
        private void LeaveFestival()
        {
            this.Debug("LeaveFestival - start");
            Game1.netReady.SetLocalReady("festivalEnd", true);
            Game1.activeClickableMenu = new ReadyCheckDialog("festivalEnd", true, who =>
                {
                this.GetBedCoordinates();
                Game1.exitActiveMenu();
                Game1.warpFarmer("Farmhouse", bedX, bedY, false);
                // Game1.timeOfDay = currentDate == spiritsEve ? 2400 : 2200;
                Game1.timeOfDay = 2200;
                Game1.shouldTimePass();
                }
                );
            this.Debug("LeaveFestival - end", bedX, bedY);
        }

        public object? CallByName(string name, params object[] args)
        {
            this.Debug("CallByName - start", name, args);
            var m = this.GetType().GetMethod(name);
            if (m == null) {
                this.Debug("CallByName - method not found", name);
                throw new MissingMethodException(name);
            }
            var result = m.Invoke(this, args);
            this.Debug("CallByName - invoked", name, result);
            return result;
        }
    }
}
