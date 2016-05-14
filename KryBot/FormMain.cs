﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using KryBot.lang;
using KryBot.Properties;
using RestSharp;

namespace KryBot
{
    public partial class FormMain : Form
    {
        public delegate void SubscribesContainer();

        private static Bot _bot = new Bot();
        private static Blacklist _blackList;

        private readonly Timer _timer = new Timer();
        private readonly Timer _timerTickCount = new Timer();
        private bool _farming;
        private int _interval;
        private int _loopsLeft;
        private bool _logActive;

        public Log LogBuffer;

        public FormMain()
        {
            InitializeComponent();
        }

        public event SubscribesContainer LogHide;
        public event SubscribesContainer LogUnHide;
        public event SubscribesContainer FormChangeLocation;
        public event SubscribesContainer LogChanged;
        public event SubscribesContainer LoadProfilesInfo;

        private void логToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_logActive)
            {
                HideLog();
            }
            else
            {
                UnHideLog();
            }
        }

        private async void FormMain_Load(object sender, EventArgs e)
        {
            if (Properties.Settings.Default.FirstExecute)
            {
                switch (MessageBox.Show(strings.Licens, strings.Agreement, MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information))
                {
                    case DialogResult.Yes:
                        Properties.Settings.Default.FirstExecute = false;
                        break;
                    case DialogResult.No:
                        Application.Exit();
                        break;
                }
            }

            if (Tools.CheckIeVersion(9))
            {
                switch (
                    MessageBox.Show(strings.IECheck, strings.Warning, MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Warning))
                {
                    case DialogResult.Yes:
                        Process.Start("http://windows.microsoft.com/ru-ru/internet-explorer/download-ie");
                        Application.Exit();
                        break;
                    case DialogResult.Cancel:
                        Application.Exit();
                        break;
                }
            }

            new Settings().Load();
            LoadProfilesInfo += ShowProfileInfo;
            _logActive = Properties.Settings.Default.LogActive;
            Design();
            _blackList = Tools.LoadBlackList();

            var version = await Updater.CheckForUpdates();
            WriteLog(version);

            if (version.Success)
            {
                var dr = MessageBox.Show($"{version.Content.Replace("\n", "")}. Обновить?", @"Обновление",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (dr == DialogResult.Yes)
                {
                    WriteLog(new Log($"{Messages.GetDateTime()} Обновление...", Color.White, true, true));

                    toolStripProgressBar1.Style = ProgressBarStyle.Marquee;
                    toolStripProgressBar1.Visible = true;

                    var log = await Updater.Update();
                    WriteLog(log);

                    if (log.Success)
                    {
                        Process.Start("KryBot.exe");
                        Application.Exit();
                    }

                    toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
                    toolStripProgressBar1.Visible = false;
                }
            }

            if (LoadProfile())
            {
                cbGMEnable.Checked = _bot.GameMiner.Enabled;
                cbSGEnable.Checked = _bot.SteamGifts.Enabled;
                cbSCEnable.Checked = _bot.SteamCompanion.Enabled;
                cbUGEnable.Checked = _bot.UseGamble.Enabled;
                cbSTEnable.Checked = _bot.SteamTrade.Enabled;
                cbPBEnabled.Checked = _bot.PlayBlink.Enabled;
                btnStart.Enabled = await LoginCheck();

                if (_bot.Steam.Enabled)
                {
                    await
                        Web.SteamJoinGroupAsync("http://steamcommunity.com/groups/krybot", "",
                            Generate.PostData_SteamGroupJoin(_bot.Steam.Cookies.Sessid), Generate.Cookies_Steam(_bot),
                            new List<HttpHeader>());
                }
            }
            else
            {
                btnGMLogin.Visible = true;
                btnSGLogin.Visible = true;
                btnSCLogin.Visible = true;
                btnUGLogin.Visible = true;
                btnSTLogin.Visible = true;
                btnPBLogin.Visible = true;
                btnSteamLogin.Visible = true;
                btnStart.Enabled = false;
            }
        }

        private void FormMain_LocationChanged(object sender, EventArgs e)
        {
            FormChangeLocation?.Invoke();
        }

        private async void btnStart_Click(object sender, EventArgs e)
        {
            if (Properties.Settings.Default.Timer)
            {
                if (!_farming)
                {
                    _loopsLeft = Properties.Settings.Default.TimerLoops;
                    _timer.Interval = Properties.Settings.Default.TimerInterval;
                    _interval = _timer.Interval;
                    _timerTickCount.Interval = 1000;
                    _timer.Tick += timer_Tick;
                    _timerTickCount.Tick += TimerTickCountOnTick;
                    _timer.Start();
                    _timerTickCount.Start();
                    btnStart.Text =
                        $"{strings.FormMain_btnStart_Click_Stop} ({TimeSpan.FromMilliseconds(_timer.Interval)})";
                    if (Properties.Settings.Default.ShowFarmTip)
                    {
                        ShowBaloolTip(
                            $"{strings.FormMain_btnStart_Click_FarmBeginWithInterval} {_interval/60000} {strings.FormMain_btnStart_Click_M}",
                            5000, ToolTipIcon.Info);
                    }
                    await DoFarm();
                    if (Properties.Settings.Default.ShowFarmTip)
                    {
                        ShowBaloolTip(strings.FormMain_btnStart_Click_FarmFinish, 5000, ToolTipIcon.Info);
                    }
                }
                else
                {
                    _timer.Stop();
                    _timerTickCount.Stop();
                    btnStart.Text = strings.FormMain_btnStart_Click_Старт;
                    btnStart.Enabled = false;
                }
            }
            else
            {
                if (_timer.Enabled)
                {
                    _timer.Stop();
                }

                if (_timerTickCount.Enabled)
                {
                    _timerTickCount.Stop();
                }

                if (!_farming)
                {
                    btnStart.Enabled = false;
                    btnStart.Text = strings.FormMain_btnStart_Click_TaskBegin;

                    if (Properties.Settings.Default.ShowFarmTip)
                    {
                        ShowBaloolTip(strings.FormMain_btnStart_Click_FarmBegin, 5000, ToolTipIcon.Info);
                    }

                    await DoFarm();

                    if (Properties.Settings.Default.ShowFarmTip)
                    {
                        ShowBaloolTip(strings.FormMain_btnStart_Click_FarmFinish, 5000, ToolTipIcon.Info);
                    }

                    btnStart.Text = strings.FormMain_btnStart_Click_Старт;
                    btnStart.Enabled = true;
                }
                else
                {
                    WriteLog(new Log($"{Messages.GetDateTime()} {strings.FormMain_btnStart_Click_FarmSkip}", Color.Red,
                        false, true));
                    btnStart.Text = strings.FormMain_btnStart_Click_Старт;
                }
            }
        }

        private void TimerTickCountOnTick(object sender, EventArgs eventArgs)
        {
            _interval += -1000;
            btnStart.Text = $"{strings.FormMain_btnStart_Click_Stop} ({TimeSpan.FromMilliseconds(_interval)})";
        }

        private void Design()
        {
            Text = $"{Application.ProductName} [{Application.ProductVersion}]";
            Icon = Resources.KryBotPresent_256b;

            btnStart.Enabled = false;
            btnGMLogin.Visible = false;
            btnSGLogin.Visible = false;
            btnSCLogin.Visible = false;
            btnUGLogin.Visible = false;
            btnSTLogin.Visible = false;
            btnPBLogin.Visible = false;
            btnSteamLogin.Visible = false;
            btnGMExit.Visible = false;
            btnSGExit.Visible = false;
            btnSCExit.Visible = false;
            btnUGExit.Visible = false;
            btnSTExit.Visible = false;
            btnPBExit.Visible = false;
            btnSteamExit.Visible = false;

            toolStripProgressBar1.Visible = false;
            toolStripStatusLabel1.Image = null;
            toolStripStatusLabel1.Text = "";

            pbGMReload.Visible = false;
            pbSGReload.Visible = false;
            pbSCReload.Visible = false;
            pbUGReload.Visible = false;
            pbSTreload.Visible = false;
            pbPBRefresh.Visible = false;

            if (_logActive)
            {
                OpenLog();
            }
            else
            {
                OpenLog();
                HideLog();
            }
        }

        private bool LoadProfile()
        {
            if (File.Exists("profile.xml"))
            {
                _bot = Tools.LoadProfile("");
                if (_bot == null)
                {
                    var message = Messages.FileLoadFailed("profile.xml");
                    message.Content += "\n";
                    WriteLog(message);

                    MessageBox.Show(message.Content.Split(']')[1], strings.Error, MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return false;
                }
                WriteLog(Messages.ProfileLoaded());
                return true;
            }

            _bot = new Bot();
            return false;
        }

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            Properties.Settings.Default.LogActive = _logActive;
            _bot.Save();
            new Settings().Save();
            Properties.Settings.Default.JoinsPerSession = 0;
            Properties.Settings.Default.JoinsLoops = 0;
            Properties.Settings.Default.Save();
        }

        private void OpenLog()
        {
            логToolStripMenuItem.Text = $"{strings.Log} <<";
            var form = new FormLog(Location.X + Width - 15, Location.Y) {Owner = this};

            LogHide += form.FormHide;
            LogUnHide += form.FormUnHide;
            FormChangeLocation += form.FormChangeLocation;
            LogChanged += form.LogChanged;

            LogBuffer = Messages.Start();
            form.Show();
            _logActive = true;
        }

        private void UnHideLog()
        {
            LogUnHide?.Invoke();
            логToolStripMenuItem.Text = $"{strings.Log} <<";
            _logActive = true;
        }

        private void HideLog()
        {
            LogHide?.Invoke();
            логToolStripMenuItem.Text = $"{strings.Log} >>";
            _logActive = false;
        }

        private async void timer_Tick(object sender, EventArgs e)
        {
            _interval = _timer.Interval;
            if (!_farming)
            {
                if (_loopsLeft > 0)
                {
                    await DoFarm();
                    _loopsLeft += -1;
                }
                else
                {
                    await DoFarm();
                }
            }
        }

        private async Task<bool> DoFarm()
        {
            _farming = true;
            WriteLog(Messages.DoFarm_Start());

            toolStripProgressBar1.Value = 0;
            toolStripProgressBar1.Maximum = 5;
            toolStripProgressBar1.Visible = true;
            toolStripStatusLabel1.Image = Resources.load;
            toolStripStatusLabel1.Text = strings.FormMain_DoFarm_Farn;

            var stopWatch = new Stopwatch();
            stopWatch.Start();

            if (_bot.GameMiner.Enabled)
            {
                var profile = await Parse.GameMinerGetProfileAsync(_bot);
                if (profile.Echo)
                {
                    WriteLog(profile);
                }
                LoadProfilesInfo?.Invoke();

                if (profile.Success)
                {
                    var won = await Parse.GameMinerWonParseAsync(_bot);
                    if (won != null && won.Content != "\n")
                    {
                        WriteLog(won);
                        if (Properties.Settings.Default.ShowWonTip)
                        {
                            ShowBaloolTip(won.Content.Split(']')[1], 5000, ToolTipIcon.Info);
                        }
                    }

                    var giveaways = await Parse.GameMinerLoadGiveawaysAsync(_bot, _bot.GameMiner.Giveaways, _blackList);
                    if (giveaways != null && giveaways.Content != "\n")
                    {
                        WriteLog(giveaways);
                    }

                    if (_bot.GameMiner.Giveaways?.Count > 0)
                    {
                        if (Properties.Settings.Default.Sort)
                        {
                            if (Properties.Settings.Default.SortToMore)
                            {
                                _bot.GameMiner.Giveaways.Sort((a, b) => b.Price.CompareTo(a.Price));
                            }
                            else
                            {
                                _bot.GameMiner.Giveaways.Sort((a, b) => a.Price.CompareTo(b.Price));
                            }
                        }

                        await JoinGiveaways(_bot.GameMiner.Giveaways);
                    }
                }
                else
                {
                    BlockTabpage(tabPageGM, false);
                    btnGMLogin.Enabled = true;
                    btnGMLogin.Visible = true;
                    linkLabel1.Enabled = true;
                    lblGMStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                }
            }
            toolStripProgressBar1.Value++;

            if (_bot.SteamGifts.Enabled)
            {
                var profile = await Parse.SteamGiftsGetProfileAsync(_bot);
                if (profile.Echo)
                {
                    WriteLog(profile);
                }
                LoadProfilesInfo?.Invoke();

                if (profile.Success)
                {
                    var won = await Parse.SteamGiftsWonParseAsync(_bot);
                    if (won != null && won.Content != "\n")
                    {
                        WriteLog(won);
                        if (Properties.Settings.Default.ShowWonTip)
                        {
                            ShowBaloolTip(won.Content.Split(']')[1], 5000, ToolTipIcon.Info);
                        }
                    }

                    if (_bot.SteamGifts.Points > 0)
                    {
                        var giveaways =
                            await
                                Parse.SteamGiftsLoadGiveawaysAsync(_bot, _bot.SteamGifts.Giveaways,
                                    _bot.SteamGifts.WishlistGiveaways, _blackList);
                        if (giveaways != null && giveaways.Content != "\n")
                        {
                            WriteLog(giveaways);
                        }

                        if (_bot.SteamGifts.WishlistGiveaways.Count > 0)
                        {
                            if (Properties.Settings.Default.Sort)
                            {
                                if (Properties.Settings.Default.SortToMore)
                                {
                                    if (!Properties.Settings.Default.WishlistNotSort)
                                    {
                                        _bot.SteamGifts.WishlistGiveaways.Sort((a, b) => b.Price.CompareTo(a.Price));
                                    }
                                }
                                else
                                {
                                    if (!Properties.Settings.Default.WishlistNotSort)
                                    {
                                        _bot.SteamGifts.WishlistGiveaways.Sort((a, b) => a.Price.CompareTo(b.Price));
                                    }
                                }
                            }

                            if (_bot.SteamGifts.SortToLessLevel)
                            {
                                if (!Properties.Settings.Default.WishlistNotSort)
                                {
                                    _bot.SteamGifts.WishlistGiveaways.Sort((a, b) => b.Level.CompareTo(a.Level));
                                }
                            }

                            await JoinGiveaways(_bot.SteamGifts.WishlistGiveaways, true);
                        }

                        if (_bot.SteamGifts.Giveaways.Count > 0)
                        {
                            if (Properties.Settings.Default.Sort)
                            {
                                if (Properties.Settings.Default.SortToMore)
                                {
                                    _bot.SteamGifts.Giveaways.Sort((a, b) => b.Price.CompareTo(a.Price));
                                }
                                else
                                {
                                    _bot.SteamGifts.Giveaways.Sort((a, b) => a.Price.CompareTo(b.Price));
                                }
                            }

                            if (_bot.SteamGifts.SortToLessLevel)
                            {
                                _bot.SteamGifts.Giveaways.Sort((a, b) => b.Level.CompareTo(a.Level));
                            }

                            await JoinGiveaways(_bot.SteamGifts.Giveaways, false);
                        }
                    }
                }
                else
                {
                    BlockTabpage(tabPageSG, false);
                    btnSGLogin.Enabled = true;
                    btnSGLogin.Visible = true;
                    linkLabel2.Enabled = true;
                    lblSGStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                }
            }
            toolStripProgressBar1.Value++;

            if (_bot.SteamCompanion.Enabled)
            {
                var profile = await Parse.SteamCompanionGetProfileAsync(_bot);
                if (profile.Echo)
                {
                    WriteLog(profile);
                }
                LoadProfilesInfo?.Invoke();
                if (profile.Success)
                {
                    var won = await Parse.SteamCompanionWonParseAsync(_bot);
                    if (won != null)
                    {
                        WriteLog(won);
                        if (Properties.Settings.Default.ShowWonTip)
                        {
                            ShowBaloolTip(won.Content.Split(']')[1], 5000, ToolTipIcon.Info);
                        }
                    }

                    var giveaways =
                        await
                            Parse.SteamCompanionLoadGiveawaysAsync(_bot, _bot.SteamCompanion.Giveaways,
                                _bot.SteamCompanion.WishlistGiveaways);
                    if (giveaways != null && giveaways.Content != "\n")
                    {
                        WriteLog(giveaways);
                    }

                    if (_bot.SteamCompanion.WishlistGiveaways.Count > 0)
                    {
                        if (Properties.Settings.Default.Sort)
                        {
                            if (Properties.Settings.Default.SortToMore)
                            {
                                if (!Properties.Settings.Default.WishlistNotSort)
                                {
                                    _bot.SteamCompanion.WishlistGiveaways.Sort((a, b) => b.Price.CompareTo(a.Price));
                                }
                            }
                            else
                            {
                                if (!Properties.Settings.Default.WishlistNotSort)
                                {
                                    _bot.SteamCompanion.WishlistGiveaways.Sort((a, b) => a.Price.CompareTo(b.Price));
                                }
                            }
                        }

                        await JoinGiveaways(_bot.SteamCompanion.WishlistGiveaways, true);
                    }

                    if (_bot.SteamCompanion.Giveaways.Count > 0)
                    {
                        if (Properties.Settings.Default.Sort)
                        {
                            if (Properties.Settings.Default.SortToMore)
                            {
                                _bot.SteamCompanion.Giveaways.Sort((a, b) => b.Price.CompareTo(a.Price));
                                if (!Properties.Settings.Default.WishlistNotSort)
                                {
                                    _bot.SteamCompanion.WishlistGiveaways.Sort((a, b) => b.Price.CompareTo(a.Price));
                                }
                            }
                            else
                            {
                                _bot.SteamCompanion.Giveaways.Sort((a, b) => a.Price.CompareTo(b.Price));
                                if (!Properties.Settings.Default.WishlistNotSort)
                                {
                                    _bot.SteamCompanion.WishlistGiveaways.Sort((a, b) => a.Price.CompareTo(b.Price));
                                }
                            }
                        }
                    }

                    await JoinGiveaways(_bot.SteamCompanion.Giveaways, false);

                    var async = await Web.SteamCompanionSyncAccountAsync(_bot);
                    if (async != null)
                    {
                        WriteLog(async);
                    }
                }
                else
                {
                    BlockTabpage(tabPageSC, false);
                    btnSCLogin.Enabled = true;
                    btnSCLogin.Visible = true;
                    linkLabel3.Enabled = true;
                    lblSCStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                }
            }

            if (_bot.UseGamble.Enabled)
            {
                var profile = await Parse.UseGambleGetProfileAsync(_bot);
                if (profile.Echo)
                {
                    WriteLog(profile);
                }
                LoadProfilesInfo?.Invoke();
                if (profile.Success)
                {
                    var won = await Parse.UseGambleWonParsAsync(_bot);
                    if (won != null)
                    {
                        WriteLog(won);
                        if (Properties.Settings.Default.ShowWonTip)
                        {
                            ShowBaloolTip(won.Content.Split(']')[1], 5000, ToolTipIcon.Info);
                        }
                    }

                    if (_bot.UseGamble.Points > 0)
                    {
                        var giveaways =
                            await Parse.UseGambleLoadGiveawaysAsync(_bot, _bot.UseGamble.Giveaways, _blackList);
                        if (giveaways != null && giveaways.Content != "\n")
                        {
                            WriteLog(giveaways);
                        }

                        if (_bot.UseGamble.Giveaways?.Count > 0)
                        {
                            if (Properties.Settings.Default.Sort)
                            {
                                if (Properties.Settings.Default.SortToMore)
                                {
                                    _bot.UseGamble.Giveaways.Sort((a, b) => b.Price.CompareTo(a.Price));
                                }
                                else
                                {
                                    _bot.UseGamble.Giveaways.Sort((a, b) => a.Price.CompareTo(b.Price));
                                }
                            }

                            await JoinGiveaways(_bot.UseGamble.Giveaways);
                        }
                    }
                }
                else
                {
                    BlockTabpage(tabPageUG, false);
                    btnUGLogin.Enabled = true;
                    btnUGLogin.Visible = true;
                    linkLabel4.Enabled = true;
                    lblUGStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                }
            }

            if (_bot.SteamTrade.Enabled)
            {
                var profile = await Parse.SteamTradeGetProfileAsync(_bot);
                if (profile.Echo)
                {
                    WriteLog(profile);
                }
                LoadProfilesInfo?.Invoke();
                if (profile.Success)
                {
                    var giveaways = await Parse.SteamTradeLoadGiveawaysAsync(_bot, _bot.SteamTrade.Giveaways, _blackList);
                    if (giveaways != null && giveaways.Content != "\n")
                    {
                        WriteLog(giveaways);
                    }

                    if (_bot.SteamTrade.Giveaways?.Count > 0)
                    {
                        await JoinGiveaways(_bot.SteamTrade.Giveaways);
                    }
                }
                else
                {
                    BlockTabpage(tabPageSteam, false);
                    btnSteamLogin.Enabled = true;
                    btnSteamLogin.Visible = true;
                    linkLabel6.Enabled = true;
                    lblSteamStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                }
            }

            if (_bot.PlayBlink.Enabled)
            {
                var profile = await Parse.PlayBlinkGetProfileAsync(_bot);
                if (profile.Echo)
                {
                    WriteLog(profile);
                }
                LoadProfilesInfo?.Invoke();

                if (profile.Success)
                {
                    if (_bot.PlayBlink.Points > 0)
                    {
                        var giveaways = await Parse.PlayBlinkLoadGiveawaysAsync(_bot, _bot.PlayBlink.Giveaways, _blackList);
                        if (giveaways != null && giveaways.Content != "\n")
                        {
                            WriteLog(giveaways);
                        }

                        if (_bot.PlayBlink.Giveaways?.Count > 0)
                        {
                            if (Properties.Settings.Default.Sort)
                            {
                                if (Properties.Settings.Default.SortToMore)
                                {
                                    _bot.PlayBlink.Giveaways.Sort((a, b) => b.Price.CompareTo(a.Price));
                                }
                                else
                                {
                                    _bot.PlayBlink.Giveaways.Sort((a, b) => a.Price.CompareTo(b.Price));
                                }
                            }

                            await JoinGiveaways(_bot.PlayBlink.Giveaways);
                        }
                    }
                }
                else
                {
                    BlockTabpage(tabPagePB, false);
                    btnPBLogin.Enabled = true;
                    btnPBLogin.Visible = true;
                    linkLabel7.Enabled = true;
                    lblPBStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                }
            }
            toolStripProgressBar1.Value++;

            stopWatch.Stop();
            var ts = stopWatch.Elapsed;
            string elapsedTime = $"{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
            WriteLog(Messages.DoFarm_Finish(elapsedTime));
            LoadProfilesInfo?.Invoke();

            if (_loopsLeft > 0)
            {
                WriteLog(new Log($"{Messages.GetDateTime()} {strings.FormMain_timer_Tick_LoopsLeft}: {_loopsLeft - 1}",
                    Color.White, true, true));
                _loopsLeft += -1;
            }

            _bot.ClearGiveawayList();

            toolStripProgressBar1.Visible = false;
            toolStripStatusLabel1.Image = null;
            toolStripStatusLabel1.Text = strings.StatusBar_End;
            _farming = false;
            btnStart.Enabled = true;

            Properties.Settings.Default.JoinsLoops += 1;
            Properties.Settings.Default.JoinsLoopsTotal += 1;
            Properties.Settings.Default.Save();

            return true;
        }

        private void ShowProfileInfo()
        {
            lblGMCoal.Text = $"{strings.Coal}: {_bot.GameMiner.Coal}";
            lblGMLevel.Text = $"{strings.Level}: {_bot.GameMiner.Level}";

            lblSGPoints.Text = $"{strings.Points}: {_bot.SteamGifts.Points}";
            lblSGLevel.Text = $"{strings.Level}: {_bot.SteamGifts.Level}";

            lblSCPoints.Text = $"{strings.Points}: {_bot.SteamCompanion.Points}";
            lblSCLevel.Text = $"{strings.Level}: -";

            lblUGPoints.Text = $"{strings.Points}: {_bot.UseGamble.Points}";
            lblUGLevel.Text = $"{strings.Level}: -";

            lblSTPoints.Text = $"{strings.Points}: -";
            lblSTLevel.Text = $"{strings.Level}: -";

            lblPBPoints.Text = $"{strings.Points}: {_bot.PlayBlink.Points}";
            lblPBLevel.Text = $"{strings.Level}: {_bot.PlayBlink.Level}";
        }

        private async Task<bool> LoginCheck()
        {
            toolStripProgressBar1.Value = 0;
            toolStripProgressBar1.Maximum = 7;
            toolStripProgressBar1.Visible = true;
            toolStripStatusLabel1.Image = Resources.load;
            toolStripStatusLabel1.Text = strings.TryLogin;
            var login = false;

            if (_bot.GameMiner.Enabled)
            {
                if (await CheckLoginGm())
                {
                    var won = await Parse.GameMinerWonParseAsync(_bot);
                    if (won != null && won.Content != "\n")
                    {
                        WriteLog(won);
                        if (Properties.Settings.Default.ShowWonTip)
                        {
                            ShowBaloolTip(won.Content.Split(']')[1], 5000, ToolTipIcon.Info);
                        }
                    }

                    LoadProfilesInfo?.Invoke();

                    login = true;
                    btnGMLogin.Enabled = false;
                    btnGMLogin.Visible = false;
                    lblGMStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                    LoadProfilesInfo?.Invoke();
                    pbGMReload.Visible = true;
                    btnGMExit.Visible = true;
                }
                else
                {
                    BlockTabpage(tabPageGM, false);
                    btnGMLogin.Enabled = true;
                    btnGMLogin.Visible = true;
                    lblGMStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                }
            }
            else
            {
                BlockTabpage(tabPageGM, false);
                btnGMLogin.Enabled = true;
                btnGMLogin.Visible = true;
            }
            toolStripProgressBar1.Value++;

            if (_bot.SteamGifts.Enabled)
            {
                if (await CheckLoginSg())
                {
                    var won = await Parse.SteamGiftsWonParseAsync(_bot);
                    if (won != null && won.Content != "\n")
                    {
                        WriteLog(won);
                        if (Properties.Settings.Default.ShowWonTip)
                        {
                            ShowBaloolTip(won.Content.Split(']')[1], 5000, ToolTipIcon.Info);
                        }
                    }
                    LoadProfilesInfo?.Invoke();

                    login = true;
                    btnSGLogin.Enabled = false;
                    btnSGLogin.Visible = false;
                    lblSGStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                    pbSGReload.Visible = true;
                    btnSGExit.Visible = true;
                }
                else
                {
                    BlockTabpage(tabPageSG, false);
                    btnSGLogin.Enabled = true;
                    btnSGLogin.Visible = true;
                    lblSGStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                }
            }
            else
            {
                BlockTabpage(tabPageSG, false);
                btnSGLogin.Enabled = true;
                btnSGLogin.Visible = true;
            }
            toolStripProgressBar1.Value++;

            if (_bot.SteamCompanion.Enabled)
            {
                if (await CheckLoginSc())
                {
                    var won = await Parse.SteamCompanionWonParseAsync(_bot);
                    if (won != null && won.Content != "\n")
                    {
                        WriteLog(won);
                        if (Properties.Settings.Default.ShowWonTip)
                        {
                            ShowBaloolTip(won.Content.Split(']')[1], 5000, ToolTipIcon.Info);
                        }
                    }
                    LoadProfilesInfo?.Invoke();

                    login = true;
                    btnSCLogin.Enabled = false;
                    btnSCLogin.Visible = false;
                    lblSCStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                    pbSCReload.Visible = true;
                    btnSCExit.Visible = true;
                }
                else
                {
                    BlockTabpage(tabPageSC, false);
                    btnSCLogin.Enabled = true;
                    btnSCLogin.Visible = true;
                    lblSCStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                }
            }
            else
            {
                BlockTabpage(tabPageSC, false);
                btnSCLogin.Enabled = true;
                btnSCLogin.Visible = true;
            }
            toolStripProgressBar1.Value++;

            if (_bot.UseGamble.Enabled)
            {
                if (await CheckLoginSp())
                {
                    var won = await Parse.UseGambleWonParsAsync(_bot);
                    if (won != null)
                    {
                        WriteLog(won);
                        if (Properties.Settings.Default.ShowWonTip)
                        {
                            ShowBaloolTip(won.Content.Split(']')[1], 5000, ToolTipIcon.Info);
                        }
                    }

                    login = true;
                    btnUGLogin.Enabled = false;
                    btnUGLogin.Visible = false;
                    lblUGStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                    pbUGReload.Visible = true;
                    btnUGExit.Visible = true;
                }
                else
                {
                    BlockTabpage(tabPageUG, false);
                    btnUGLogin.Enabled = true;
                    btnUGLogin.Visible = true;
                    lblUGStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                }
            }
            else
            {
                BlockTabpage(tabPageUG, false);
                btnUGLogin.Enabled = true;
                btnUGLogin.Visible = true;
            }
            toolStripProgressBar1.Value++;

            if (_bot.SteamTrade.Enabled)
            {
                if (await CheckLoginSt())
                {
                    login = true;
                    btnSTLogin.Enabled = false;
                    btnSTLogin.Visible = false;
                    lblSTStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                    pbSTreload.Visible = true;
                    btnSTExit.Visible = true;
                }
                else
                {
                    BlockTabpage(tabPageST, false);
                    btnSTLogin.Enabled = true;
                    btnSTLogin.Visible = true;
                    lblSTStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                }
            }
            else
            {
                BlockTabpage(tabPageST, false);
                btnSTLogin.Enabled = true;
                btnSTLogin.Visible = true;
            }
            toolStripProgressBar1.Value++;

            if (_bot.PlayBlink.Enabled)
            {
                if (await CheckLoginPb())
                {
                    LoadProfilesInfo?.Invoke();

                    login = true;
                    btnPBLogin.Enabled = false;
                    btnPBLogin.Visible = false;
                    lblPBStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                    LoadProfilesInfo?.Invoke();
                    pbPBRefresh.Visible = true;
                    btnPBExit.Visible = true;
                }
                else
                {
                    BlockTabpage(tabPagePB, false);
                    btnPBLogin.Enabled = true;
                    btnPBLogin.Visible = true;
                    lblPBStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                }
            }
            else
            {
                BlockTabpage(tabPagePB, false);
                btnPBLogin.Enabled = true;
                btnPBLogin.Visible = true;
            }
            toolStripProgressBar1.Value++;

            if (_bot.Steam.Enabled)
            {
                if (await CheckLoginSteam())
                {
                    btnSteamLogin.Enabled = false;
                    btnSteamLogin.Visible = false;
                    lblSteamStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                    btnSteamExit.Visible = true;
                }
                else
                {
                    BlockTabpage(tabPageSteam, false);
                    btnSteamLogin.Enabled = true;
                    btnSteamLogin.Visible = true;
                    lblSteamStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                }
            }
            else
            {
                BlockTabpage(tabPageSteam, false);
                btnSteamLogin.Enabled = true;
                btnSteamLogin.Visible = true;
            }
            toolStripProgressBar1.Value++;

            toolStripProgressBar1.Visible = false;
            toolStripStatusLabel1.Image = null;
            toolStripStatusLabel1.Text = strings.StatusBar_End;
            return login;
        }

        private void BlockTabpage(TabPage tabPage, bool state)
        {
            foreach (Control control in tabPage.Controls)
            {
                control.Enabled = control.GetType().FullName == "System.Windows.Forms.LinkLabel" || state;
            }
        }

        private async void btnSTLogin_Click(object sender, EventArgs e)
        {
            btnSTLogin.Enabled = false;
            var first = Web.Get("http://steamtrade.info/", "", new List<Parameter>(), new CookieContainer(),
                new List<HttpHeader>());
            var getLoginHref = Web.SteamTradeDoAuth("http://steamtrade.info/", "reg.php?login",
                Generate.LoginData_SteamTrade(), first.Cookies, new List<HttpHeader>());
            var location = Tools.GetLocationInresponse(getLoginHref.RestResponse);
            var cookie = Tools.GetSessCookieInresponse(getLoginHref.Cookies, "steamtrade.info", "PHPSESSID");

            BrowserStart(location, "http://steamtrade.info/", "SteamTrade - Login", cookie);
            _bot.Save();

            toolStripStatusLabel1.Image = Resources.load;
            toolStripStatusLabel1.Text = strings.StatusBar_Login;
            var login = await CheckLoginSt();
            if (login)
            {
                BlockTabpage(tabPageST, true);
                btnSTLogin.Enabled = false;
                btnSTLogin.Visible = false;
                lblSTStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                LoadProfilesInfo?.Invoke();
                btnStart.Enabled = true;
                pbSTreload.Visible = true;
                btnSTExit.Visible = true;
                btnSTExit.Enabled = true;
                cbSTEnable.Checked = true;
                _bot.SteamTrade.Enabled = true;
            }
            else
            {
                lblSTStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                BlockTabpage(tabPageST, false);
                btnSTLogin.Enabled = true;
                btnSTLogin.Visible = true;
            }
            toolStripStatusLabel1.Image = null;
            toolStripStatusLabel1.Text = strings.StatusBar_End;
        }

        private static void BrowserStart(string startPage, string endPage, string title, string phpSessId)
        {
            Form form = new Browser(_bot, startPage, endPage, title, phpSessId);
            form.Height = Screen.PrimaryScreen.Bounds.Height/2;
            form.Width = Screen.PrimaryScreen.Bounds.Width/2;
            form.Name = "Browser";
            form.ShowDialog();
        }

        private async void btnUGLogin_Click(object sender, EventArgs e)
        {
            btnUGLogin.Enabled = false;
            BrowserStart("http://usegamble.com/page/steam", "http://usegamble.com/", "UseGamble - Login", "");
            _bot.Save();

            toolStripStatusLabel1.Image = Resources.load;
            toolStripStatusLabel1.Text = strings.StatusBar_Login;
            var login = await CheckLoginSp();
            if (login)
            {
                var won = await Parse.UseGambleWonParsAsync(_bot);
                if (won != null && won.Content != "\n")
                {
                    WriteLog(won);
                }

                BlockTabpage(tabPageUG, true);
                btnUGLogin.Enabled = false;
                btnUGLogin.Visible = false;
                lblUGStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                LoadProfilesInfo?.Invoke();
                btnStart.Enabled = true;
                pbUGReload.Visible = true;
                btnUGExit.Visible = true;
                btnUGExit.Enabled = true;
                cbUGEnable.Checked = true;
                _bot.UseGamble.Enabled = true;
            }
            else
            {
                lblUGStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                BlockTabpage(tabPageUG, false);
                btnUGLogin.Enabled = true;
                btnUGLogin.Visible = true;
            }
            toolStripStatusLabel1.Image = null;
            toolStripStatusLabel1.Text = strings.StatusBar_End;
        }

        private async void btnSCLogin_Click(object sender, EventArgs e)
        {
            btnSCLogin.Enabled = false;
            BrowserStart("https://steamcompanion.com/login", "https://steamcompanion.com/", "SteamCompanion - Login", "");
            _bot.Save();

            toolStripStatusLabel1.Image = Resources.load;
            toolStripStatusLabel1.Text = strings.StatusBar_Login;
            var login = await CheckLoginSc();
            if (login)
            {
                var won = await Parse.SteamCompanionWonParseAsync(_bot);
                if (won != null && won.Content != "\n")
                {
                    WriteLog(won);
                }

                BlockTabpage(tabPageSC, true);
                btnSCLogin.Enabled = false;
                btnSCLogin.Visible = false;
                lblSCStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                LoadProfilesInfo?.Invoke();
                btnStart.Enabled = true;
                pbSCReload.Visible = true;
                btnSCExit.Visible = true;
                btnSCExit.Enabled = true;
                cbSCEnable.Checked = true;
                _bot.SteamCompanion.Enabled = true;
            }
            else
            {
                lblSCStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                BlockTabpage(tabPageSC, false);
                btnSCLogin.Enabled = true;
                btnSCLogin.Visible = true;
            }
            toolStripStatusLabel1.Image = null;
            toolStripStatusLabel1.Text = strings.StatusBar_End;
        }

        private async void btnSGLogin_Click(object sender, EventArgs e)
        {
            btnSGLogin.Enabled = false;
            BrowserStart("https://www.steamgifts.com/?login", "https://www.steamgifts.com/", "SteamGifts - Login", "");
            _bot.Save();

            toolStripStatusLabel1.Image = Resources.load;
            toolStripStatusLabel1.Text = strings.StatusBar_Login;
            var login = await CheckLoginSg();
            if (login)
            {
                var won = await Parse.SteamGiftsWonParseAsync(_bot);
                if (won != null && won.Content != "\n")
                {
                    WriteLog(won);
                }
                BlockTabpage(tabPageSG, true);
                btnSGLogin.Enabled = false;
                btnSGLogin.Visible = false;
                lblSGStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                LoadProfilesInfo?.Invoke();
                btnStart.Enabled = true;
                pbSGReload.Visible = true;
                btnSGExit.Visible = true;
                btnSGExit.Enabled = true;
                cbSGEnable.Checked = true;
                _bot.SteamGifts.Enabled = true;
            }
            else
            {
                lblSGStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                BlockTabpage(tabPageSG, false);
                btnSGLogin.Enabled = true;
                btnSGLogin.Visible = true;
            }
            toolStripStatusLabel1.Image = null;
            toolStripStatusLabel1.Text = strings.StatusBar_End;
        }

        private async void btnGMLogin_Click(object sender, EventArgs e)
        {
            btnGMLogin.Enabled = false;
            BrowserStart(
                "http://gameminer.net/login/steam?backurl=http%3A%2F%2Fgameminer.net%2F%3Flang%3D" +
                Properties.Settings.Default.Lang + @"&agree=True",
                "http://gameminer.net/?lang=" + Properties.Settings.Default.Lang, "GameMiner - Login", "");

            if (string.IsNullOrEmpty(_bot.GameMiner.UserAgent))
            {
                _bot.GameMiner.UserAgent = Tools.UserAgent();
            }
            _bot.Save();

            toolStripStatusLabel1.Image = Resources.load;
            toolStripStatusLabel1.Text = strings.StatusBar_Login;
            var login = await CheckLoginGm();
            if (login)
            {
                var won = await Parse.GameMinerWonParseAsync(_bot);
                if (won != null && won.Content != "\n")
                {
                    WriteLog(won);
                }
                BlockTabpage(tabPageGM, true);
                btnGMLogin.Enabled = false;
                btnGMLogin.Visible = false;
                lblGMStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                LoadProfilesInfo?.Invoke();
                btnStart.Enabled = true;
                pbGMReload.Visible = true;
                btnGMExit.Visible = true;
                btnGMExit.Enabled = true;
                cbGMEnable.Checked = true;
                _bot.GameMiner.Enabled = true;
            }
            else
            {
                lblGMStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                BlockTabpage(tabPageGM, false);
                btnGMLogin.Enabled = true;
                btnGMLogin.Visible = true;
            }
            toolStripStatusLabel1.Image = null;
            toolStripStatusLabel1.Text = strings.StatusBar_End;
        }

        private async Task<bool> CheckLoginGm()
        {
            Message_TryLogin("GameMiner");
            var login = await Parse.GameMinerGetProfileAsync(_bot);
            WriteLog(login);
            return login.Success;
        }

        private async Task<bool> CheckLoginSg()
        {
            Message_TryLogin("SteamGifts");
            var login = await Parse.SteamGiftsGetProfileAsync(_bot);
            WriteLog(login);
            return login.Success;
        }

        private async Task<bool> CheckLoginSc()
        {
            Message_TryLogin("SteamCompanion");
            var login = await Parse.SteamCompanionGetProfileAsync(_bot);
            WriteLog(login);
            return login.Success;
        }

        private async Task<bool> CheckLoginSp()
        {
            Message_TryLogin("UseGamble");
            var login = await Parse.UseGambleGetProfileAsync(_bot);
            WriteLog(login);
            return login.Success;
        }

        private async Task<bool> CheckLoginSt()
        {
            Message_TryLogin("SteamTrade");
            var login = await Parse.SteamTradeGetProfileAsync(_bot);
            WriteLog(login);
            return login.Success;
        }

        private async Task<bool> CheckLoginSteam()
        {
            Message_TryLogin("Steam");
            var login = await Parse.SteamGetProfileAsync(_bot);
            WriteLog(login);
            return login.Success;
        }

        private async Task<bool> CheckLoginPb()
        {
            Message_TryLogin("PlayBlink");
            var login = await Parse.PlayBlinkGetProfileAsync(_bot);
            WriteLog(login);
            return login.Success;
        }

        private async void pbGMReload_Click(object sender, EventArgs e)
        {
            btnGMExit.Enabled = false;
            pbGMReload.Image = Resources.load;
            SetStatusPanel("Обновление информации о GameMiner", Resources.load);

            if (await CheckLoginGm())
            {
                LoadProfilesInfo?.Invoke();
                var won = await Parse.GameMinerWonParseAsync(_bot);
                if (won != null && won.Content != "\n")
                {
                    WriteLog(won);
                }

                var async = await Web.GameMinerSyncAccountAsync(_bot);
                if (async != null)
                {
                    WriteLog(async);
                }

                btnGMLogin.Enabled = false;
                btnGMLogin.Visible = false;
                lblGMStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                LoadProfilesInfo?.Invoke();
                BlockTabpage(tabPageGM, true);
            }
            else
            {
                BlockTabpage(tabPageGM, false);
                btnGMLogin.Enabled = true;
                btnGMLogin.Visible = true;
                lblGMStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
            }

            SetStatusPanel(strings.Finish, null);
            pbGMReload.Image = Resources.refresh;
            btnGMExit.Enabled = true;
        }

        private async void pbSGReload_Click(object sender, EventArgs e)
        {
            btnGMExit.Enabled = false;
            pbSGReload.Image = Resources.load;
            SetStatusPanel("Обновление информации о SteamGifts", Resources.load);

            if (await CheckLoginSg())
            {
                LoadProfilesInfo?.Invoke();
                var won = await Parse.SteamGiftsWonParseAsync(_bot);
                if (won != null)
                {
                    WriteLog(won);
                }

                var async = await Web.SteamGiftsSyncAccountAsync(_bot);
                if (async != null)
                {
                    WriteLog(async);
                }

                btnSGLogin.Enabled = false;
                btnSGLogin.Visible = false;
                lblSGStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                BlockTabpage(tabPageSG, true);
            }
            else
            {
                BlockTabpage(tabPageSG, false);
                btnSGLogin.Enabled = true;
                btnSGLogin.Visible = true;
                lblSGStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
            }

            SetStatusPanel(strings.Finish, null);
            pbSGReload.Image = Resources.refresh;
            btnGMExit.Enabled = true;
        }

        private async void pbSCReload_Click(object sender, EventArgs e)
        {
            btnGMExit.Enabled = false;
            pbSCReload.Image = Resources.load;
            SetStatusPanel("Обновление информации о SteamCompanion", Resources.load);

            if (await CheckLoginSc())
            {
                LoadProfilesInfo?.Invoke();
                var won = await Parse.SteamCompanionWonParseAsync(_bot);
                if (won != null)
                {
                    WriteLog(won);
                }
                btnSCLogin.Enabled = false;
                btnSCLogin.Visible = false;
                lblSCStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                BlockTabpage(tabPageSC, true);

                var async = await Web.SteamCompanionSyncAccountAsync(_bot);
                if (async != null)
                {
                    WriteLog(async);
                }
            }
            else
            {
                BlockTabpage(tabPageSC, false);
                btnSCLogin.Enabled = true;
                btnSCLogin.Visible = true;
                lblSCStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
            }

            SetStatusPanel(strings.Finish, null);
            pbSCReload.Image = Resources.refresh;
            btnGMExit.Enabled = true;
        }

        private async void pbUGReload_Click(object sender, EventArgs e)
        {
            btnGMExit.Enabled = false;
            pbUGReload.Image = Resources.load;
            SetStatusPanel("Обновление информации о UseGamble", Resources.load);

            if (await CheckLoginSp())
            {
                LoadProfilesInfo?.Invoke();
                btnUGLogin.Enabled = false;
                btnUGLogin.Visible = false;
                lblUGStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                BlockTabpage(tabPageUG, true);
            }
            else
            {
                BlockTabpage(tabPageUG, false);
                btnUGLogin.Enabled = true;
                btnUGLogin.Visible = true;
                lblUGStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
            }

            pbUGReload.Image = Resources.refresh;
            SetStatusPanel(strings.Finish, null);
            btnGMExit.Enabled = true;
        }

        private async void pbSTreload_Click(object sender, EventArgs e)
        {
            btnGMExit.Enabled = false;
            pbSTreload.Image = Resources.load;
            SetStatusPanel("Обновление информации о SteamTrade", Resources.load);

            if (await CheckLoginSt())
            {
                LoadProfilesInfo?.Invoke();
                btnSTLogin.Enabled = false;
                btnSTLogin.Visible = false;
                lblSTStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                BlockTabpage(tabPageST, true);
            }
            else
            {
                BlockTabpage(tabPageST, false);
                btnSTLogin.Enabled = true;
                btnSTLogin.Visible = true;
                lblSTStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
            }

            SetStatusPanel(strings.Finish, null);
            pbSTreload.Image = Resources.refresh;
            btnGMExit.Enabled = true;
        }

        private void notifyIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            notifyIcon.Visible = false;
            Show();
            WindowState = FormWindowState.Normal;
            _hided = false;
            if (_logActive)
            {
                LogUnHide?.Invoke();
            }
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("http://gameminer.net/");
        }

        private void linkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://www.steamgifts.com/");
        }

        private void linkLabel3_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://steamcompanion.com/");
        }

        private void linkLabel4_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("http://usegamble.com/");
        }

        private void linkLabel5_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("http://steamtrade.info/");
        }

        private void статстикаToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var form = new FormStatistic();
            form.ShowDialog();
        }

        private void оПрограммеToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            var form = new FormAbout();
            form.ShowDialog();
        }

        private void сохранитьToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_bot.Save())
            {
                WriteLog(new Log(Messages.GetDateTime() + " Настройки сохранены в profile.xml", Color.White, true, true));
            }

            Properties.Settings.Default.Save();
        }

        private void сохранитьКакToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var saveFileDialog1 = new SaveFileDialog
            {
                Filter = @"XML|*.xml",
                Title = @"Сохранить профиль"
            };
            saveFileDialog1.ShowDialog();

            if (saveFileDialog1.FileName != "")
            {
                if (_bot.Save(saveFileDialog1.FileName))
                {
                    WriteLog(new Log(Messages.GetDateTime() + " Файл сохранен по пути " + saveFileDialog1.FileName,
                        Color.White, true, true));
                }
                else
                {
                    WriteLog(new Log(Messages.GetDateTime() + " Файл НЕ сохранен", Color.Red, true, true));
                }
            }
        }

        private void загрузитьToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (lblSTStatus.Enabled)
            {
                BlockTabpage(tabPageST, false);
                cbSTEnable.Enabled = true;
                _bot.SteamTrade.Enabled = false;
            }
            else
            {
                BlockTabpage(tabPageST, true);
                _bot.SteamTrade.Enabled = true;
            }
        }

        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {
            if (lblSGStatus.Enabled)
            {
                BlockTabpage(tabPageSG, false);
                cbSGEnable.Enabled = true;
                _bot.SteamGifts.Enabled = false;
            }
            else
            {
                BlockTabpage(tabPageSG, true);
                _bot.SteamGifts.Enabled = true;
            }
        }

        private void cbGMEnable_CheckedChanged(object sender, EventArgs e)
        {
            if (lblGMStatus.Enabled)
            {
                BlockTabpage(tabPageGM, false);
                cbGMEnable.Enabled = true;
                _bot.GameMiner.Enabled = false;
            }
            else
            {
                BlockTabpage(tabPageGM, true);
                _bot.GameMiner.Enabled = true;
            }
        }

        private void cbUGEnable_CheckedChanged(object sender, EventArgs e)
        {
            if (lblUGStatus.Enabled)
            {
                BlockTabpage(tabPageUG, false);
                cbUGEnable.Enabled = true;
                _bot.UseGamble.Enabled = false;
            }
            else
            {
                BlockTabpage(tabPageUG, true);
                _bot.UseGamble.Enabled = true;
            }
        }

        private void cbSCEnable_CheckedChanged(object sender, EventArgs e)
        {
            if (lblSCStatus.Enabled)
            {
                BlockTabpage(tabPageSC, false);
                cbSCEnable.Enabled = true;
                _bot.SteamCompanion.Enabled = false;
            }
            else
            {
                BlockTabpage(tabPageSC, true);
                _bot.SteamCompanion.Enabled = true;
            }
        }

        private void linkLabel6_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("http://steamcommunity.com/");
        }

        private async void btnSteamLogin_Click(object sender, EventArgs e)
        {
            BrowserStart("https://steamcommunity.com/login/home/?goto=0", "http://steamcommunity.com/id/",
                "Steam - Login", "");
            _bot.Save();

            toolStripStatusLabel1.Image = Resources.load;
            toolStripStatusLabel1.Text = strings.StatusBar_Login;
            var login = await CheckLoginSteam();
            if (login)
            {
                await
                    Web.SteamJoinGroupAsync("http://steamcommunity.com/groups/krybot", "",
                        Generate.PostData_SteamGroupJoin(_bot.Steam.Cookies.Sessid), Generate.Cookies_Steam(_bot),
                        new List<HttpHeader>());

                BlockTabpage(tabPageSteam, true);
                btnSteamLogin.Enabled = false;
                btnSteamLogin.Visible = false;
                btnSteamExit.Enabled = true;
                btnSteamExit.Visible = true;

                lblSteamStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                LoadProfilesInfo?.Invoke();
            }
            else
            {
                lblSteamStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                BlockTabpage(tabPageSteam, false);
                btnSteamLogin.Enabled = true;
                btnSteamLogin.Visible = true;
            }
            toolStripStatusLabel1.Image = null;
            toolStripStatusLabel1.Text = strings.StatusBar_End;
        }

        private void FormMain_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                notifyIcon.Icon = Resources.KryBotPresent_256b;
                notifyIcon.Visible = true;
                _hided = true;
                if (_logActive)
                {
                    LogHide?.Invoke();
                }
                Hide();
            }
        }

        private void ShowBaloolTip(string content, int interval, ToolTipIcon icon)
        {
            notifyIcon.BalloonTipIcon = icon;
            notifyIcon.BalloonTipText = content;
            notifyIcon.BalloonTipTitle = Application.ProductName;
            notifyIcon.Tag = "";
            notifyIcon.BalloonTipClicked += NotifyIconOnBalloonTipClicked;
            notifyIcon.ShowBalloonTip(interval);
        }

        private void NotifyIconOnBalloonTipClicked(object sender, EventArgs eventArgs)
        {
            var tag = (string) notifyIcon.Tag;
            if (tag == "")
            {
                notifyIcon.Visible = false;
                Show();
                WindowState = FormWindowState.Normal;
                _hided = false;
            }
            else
            {
                Process.Start((string) notifyIcon.Tag);
            }
        }

        private void toolStripMenuItem_Show_Click(object sender, EventArgs e)
        {
            notifyIcon.Visible = false;
            Show();
            WindowState = FormWindowState.Normal;
            _hided = false;

            if (_logActive)
            {
                LogUnHide?.Invoke();
            }
        }

        private void toolStripMenuItem_Farm_Click(object sender, EventArgs e)
        {
            btnStart_Click(sender, e);
        }

        private void toolStripMenuItem_Exit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void донатToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var form = new FormDonate();
            form.ShowDialog();
        }

        private void btnSteamExit_Click(object sender, EventArgs e)
        {
            _bot.Steam.Logout();
            BlockTabpage(tabPageSteam, false);
            btnSteamLogin.Visible = true;
            btnSteamLogin.Enabled = true;
            btnSteamExit.Visible = false;
            _bot.Save();
        }

        private void btnSTExit_Click(object sender, EventArgs e)
        {
            _bot.SteamTrade.Logout();
            BlockTabpage(tabPageST, false);
            btnSTLogin.Visible = true;
            btnSTExit.Visible = false;
            btnSTLogin.Enabled = true;
            _bot.Save();
        }

        private void btnUGExit_Click(object sender, EventArgs e)
        {
            _bot.UseGamble.Logout();
            BlockTabpage(tabPageUG, false);
            btnUGLogin.Visible = true;
            btnUGLogin.Enabled = true;
            btnUGExit.Visible = false;
            _bot.Save();
        }

        private void btnSCExit_Click(object sender, EventArgs e)
        {
            _bot.SteamCompanion.Logout();
            BlockTabpage(tabPageSC, false);
            btnSCLogin.Visible = true;
            btnSCLogin.Enabled = true;
            btnSCExit.Visible = false;
            _bot.Save();
        }

        private void btnSGExit_Click(object sender, EventArgs e)
        {
            _bot.SteamGifts.Logout();
            BlockTabpage(tabPageSG, false);
            btnSGLogin.Visible = true;
            btnSGLogin.Enabled = true;
            btnSGExit.Visible = false;
            _bot.Save();
        }

        private void btnGMExit_Click(object sender, EventArgs e)
        {
            _bot.GameMiner.Logout();
            BlockTabpage(tabPageGM, false);
            btnGMLogin.Enabled = true;
            btnGMLogin.Visible = true;
            btnGMExit.Visible = false;
            _bot.Save();
        }

        private void вПапкуСБотомToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("explorer", Environment.CurrentDirectory);
        }

        private void черныйСписокToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var form = new FormBlackList(_bot);
            form.ShowDialog();
            _blackList = Tools.LoadBlackList();
        }

        private void настройкиToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            var form = new FormSettings(_bot);
            form.ShowDialog();
        }

        private async void btnPBLogin_Click(object sender, EventArgs e)
        {
            btnPBLogin.Enabled = false;
            BrowserStart("http://playblink.com/?do=login&act=signin", "http://playblink.com/", "PlayBlink - Login", "");
            _bot.Save();

            toolStripStatusLabel1.Image = Resources.load;
            toolStripStatusLabel1.Text = strings.StatusBar_Login;
            var login = await CheckLoginPb();
            if (login)
            {
                BlockTabpage(tabPagePB, true);
                btnPBLogin.Enabled = false;
                btnPBLogin.Visible = false;
                lblPBStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                LoadProfilesInfo?.Invoke();
                btnStart.Enabled = true;
                pbPBRefresh.Visible = true;
                btnPBExit.Visible = true;
                btnPBExit.Enabled = true;
                cbPBEnabled.Checked = true;
                _bot.PlayBlink.Enabled = true;
            }
            else
            {
                lblPBStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
                BlockTabpage(tabPagePB, false);
                btnPBLogin.Enabled = true;
                btnPBLogin.Visible = true;
            }
            toolStripStatusLabel1.Image = null;
            toolStripStatusLabel1.Text = strings.StatusBar_End;
        }

        private void btnPBExit_Click(object sender, EventArgs e)
        {
            _bot.PlayBlink.Logout();
            BlockTabpage(tabPagePB, false);
            btnPBLogin.Enabled = true;
            btnPBLogin.Visible = true;
            btnPBExit.Visible = false;
            _bot.Save();
        }

        private void linkLabel7_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("http://playblink.com/");
        }

        private async void pbPBRefresh_Click(object sender, EventArgs e)
        {
            btnPBExit.Enabled = false;
            pbPBRefresh.Image = Resources.load;
            SetStatusPanel("Обновление информации о PlayBlink", Resources.load);

            if (await CheckLoginPb())
            {
                LoadProfilesInfo?.Invoke();
                btnPBLogin.Enabled = false;
                btnPBLogin.Visible = false;
                lblPBStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginSuccess}";
                BlockTabpage(tabPagePB, true);
            }
            else
            {
                BlockTabpage(tabPagePB, false);
                btnPBLogin.Enabled = true;
                btnPBLogin.Visible = true;
                lblPBStatus.Text = $"{strings.FormMain_Label_Status}: {strings.LoginFaild}";
            }

            SetStatusPanel(strings.Finish, null);
            pbPBRefresh.Image = Resources.refresh;
            btnPBExit.Enabled = true;
        }

        private async Task<bool> JoinGiveaways(List<GameMiner.GmGiveaway> giveaways)
        {
            foreach (var giveaway in giveaways)
            {
                if (giveaway.Price <= _bot.GameMiner.JoinCoalLimit && giveaway.Price <= _bot.GameMiner.Coal)
                {
                    if (_bot.GameMiner.CoalReserv <= _bot.GameMiner.Coal - giveaway.Price || giveaway.Price == 0)
                    {
                        var data = await Web.GameMinerJoinGiveawayAsync(_bot, giveaway);
                        if (data != null && data.Content != "\n")
                        {
                            if (Properties.Settings.Default.FullLog)
                            {
                                WriteLog(data);
                            }
                            else
                            {
                                if (data.Color != Color.Yellow && data.Color != Color.Red)
                                {
                                    WriteLog(data);
                                }
                            }
                        }
                    }
                }
            }
            return true;
        }

        private async Task<bool> JoinGiveaways(List<SteamGifts.SgGiveaway> giveaways, bool wishlist)
        {
            foreach (var giveaway in giveaways)
            {
                if (wishlist)
                {
                    if (giveaway.Price <= _bot.SteamGifts.Points)
                    {
                        var data = await Web.SteamGiftsJoinGiveawayAsync(_bot, giveaway);
                        if (data != null && data.Content != "\n")
                        {
                            if (Properties.Settings.Default.FullLog)
                            {
                                WriteLog(data);
                            }
                            else
                            {
                                if (data.Color != Color.Yellow && data.Color != Color.Red)
                                {
                                    WriteLog(data);
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (giveaway.Price <= _bot.SteamGifts.Points &&
                        _bot.SteamGifts.PointsReserv <= _bot.SteamGifts.Points - giveaway.Price)
                    {
                        var data = await Web.SteamGiftsJoinGiveawayAsync(_bot, giveaway);
                        if (data != null && data.Content != "\n")
                        {
                            if (Properties.Settings.Default.FullLog)
                            {
                                WriteLog(data);
                            }
                            else
                            {
                                if (data.Color != Color.Yellow && data.Color != Color.Red)
                                {
                                    WriteLog(data);
                                }
                            }
                        }
                    }
                }
            }
            return true;
        }

        private async Task<bool> JoinGiveaways(List<SteamCompanion.ScGiveaway> giveaways, bool wishlist)
        {
            foreach (var giveaway in giveaways)
            {
                if (wishlist)
                {
                    if (giveaway.Price <= _bot.SteamCompanion.Points)
                    {
                        var data = await Web.SteamCompanionJoinGiveawayAsync(_bot, giveaway);
                        if (data != null && data.Content != "\n")
                        {
                            if (Properties.Settings.Default.FullLog)
                            {
                                WriteLog(data);
                            }
                            else
                            {
                                if (data.Color != Color.Yellow && data.Color != Color.Red)
                                {
                                    WriteLog(data);
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (giveaway.Price <= _bot.SteamCompanion.Points &&
                        _bot.SteamCompanion.PointsReserv <= _bot.SteamCompanion.Points - giveaway.Price)
                    {
                        var data = await Web.SteamCompanionJoinGiveawayAsync(_bot, giveaway);
                        if (data != null && data.Content != "\n")
                        {
                            if (Properties.Settings.Default.FullLog)
                            {
                                WriteLog(data);
                            }
                            else
                            {
                                if (data.Color != Color.Yellow && data.Color != Color.Red)
                                {
                                    WriteLog(data);
                                }
                            }
                        }
                    }
                }
            }
            return true;
        }

        private async Task<bool> JoinGiveaways(List<UseGamble.UgGiveaway> giveaways)
        {
            foreach (var giveaway in giveaways)
            {
                if (giveaway.Price <= _bot.UseGamble.Points &&
                    _bot.UseGamble.PointsReserv <= _bot.UseGamble.Points - giveaway.Price)
                {
                    var data = await Web.UseGambleJoinGiveawayAsync(_bot, giveaway);
                    if (data != null && data.Content != "\n")
                    {
                        if (Properties.Settings.Default.FullLog)
                        {
                            WriteLog(data);
                        }
                        else
                        {
                            if (data.Color != Color.Yellow && data.Color != Color.Red)
                            {
                                WriteLog(data);
                            }
                        }
                    }
                }
            }
            return true;
        }

        private async Task<bool> JoinGiveaways(List<SteamTrade.StGiveaway> giveaways)
        {
            foreach (var giveaway in giveaways)
            {
                var data = await Web.SteamTradeJoinGiveawayAsync(_bot, giveaway);
                if (data != null && data.Content != "\n")
                {
                    if (Properties.Settings.Default.FullLog)
                    {
                        WriteLog(data);
                    }
                    else
                    {
                        if (data.Color != Color.Yellow && data.Color != Color.Red)
                        {
                            WriteLog(data);
                        }
                    }
                }
            }
            return true;
        }

        private async Task<bool> JoinGiveaways(List<PlayBlink.PbGiveaway> giveaways)
        {
            foreach (var giveaway in giveaways)
            {
                if (giveaway.Price <= _bot.PlayBlink.MaxJoinValue && giveaway.Price <= _bot.PlayBlink.Points && _bot.PlayBlink.Level >= giveaway.Level)
                {
                    if (_bot.PlayBlink.PointReserv <= _bot.PlayBlink.Points - giveaway.Price || giveaway.Price == 0)
                    {
                        var data = await Web.PlayBlinkJoinGiveawayAsync(_bot, giveaway);
                        if (data != null && data.Content != "\n")
                        {
                            if (Properties.Settings.Default.FullLog)
                            {
                                WriteLog(data);
                            }
                            else
                            {
                                if (data.Color != Color.Yellow && data.Color != Color.Red)
                                {
                                    WriteLog(data);
                                }
                            }

                            //if (data.Content.Contains("Captcha"))
                            //{
                            //    break;
                            //}
                        }
                    }
                }
            }
            return true;
        }

        private void Message_TryLogin(string site)
        {
            if (Properties.Settings.Default.FullLog)
            {
                WriteLog(new Log($"{Messages.GetDateTime()} {{{site}}} {strings.TryLogin}", Color.White, true, true));
            }
        }

        private void SetStatusPanel(string text, Image image)
        {
            toolStripStatusLabel1.Image = image;
            toolStripStatusLabel1.Text = text;
        }

        private void cbPBEnabled_CheckedChanged(object sender, EventArgs e)
        {
            if (lblPBStatus.Enabled)
            {
                BlockTabpage(tabPagePB, false);
                cbPBEnabled.Enabled = true;
                _bot.PlayBlink.Enabled = false;
            }
            else
            {
                BlockTabpage(tabPagePB, true);
                _bot.PlayBlink.Enabled = true;
            }
        }

        private void WriteLog(Log log)
        {
            LogBuffer = log;
            LogChanged?.Invoke();
        }

        private void файлToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void настройкиToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }
    }
}