using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using IniParser;
using IniParser.Model;

namespace WorkshopDownloader;

public partial class SettingsWindow : Window
{
    private readonly FileIniDataParser _parser;
    private IniData _data;
    public readonly SteamData SteamConfig = new SteamData();
    public readonly ConfigData GeneralConfig = new ConfigData();
    
    public SettingsWindow()
    {
        _parser = new FileIniDataParser();
        if (File.Exists("./settings.ini"))
        {
            _data = _parser.ReadFile("./settings.ini");
            SteamConfig.LoginAnonymous = bool.Parse(_data["Steam"]["LoginAnonymously"]);
            SteamConfig.Username = _data["Steam"]["Username"];
            SteamConfig.Password = _data["Steam"]["Password"];
            SteamConfig.SteamApiKey = _data["Steam"]["SteamApiKey"];
            GeneralConfig.DownloadDirectory = _data["Config"]["DownloadDirectory"];
        }
        else
        {
            _data = new IniData();
            SaveSettings();
        }

        DataContext = SteamConfig;
        InitializeComponent();
        UsernameInput.IsEnabled = PasswordInput.IsEnabled = !SteamConfig.LoginAnonymous;
        LoginAnonCheckbox.Checked += LoginAnon_OnChecked;
        LoginAnonCheckbox.Unchecked += LoginAnon_OnChecked;
        Closed += (o, args) => SaveSettings();
    }

    public void SaveSettings()
    {
        _data["Steam"]["LoginAnonymously"] = SteamConfig.LoginAnonymous.ToString();
        _data["Steam"]["Username"] = SteamConfig.Username;
        _data["Steam"]["Password"] = SteamConfig.Password;
        _data["Steam"]["SteamApiKey"] = SteamConfig.SteamApiKey;
        _data["Config"]["DownloadDirectory"] = GeneralConfig.DownloadDirectory;
        _parser.WriteFile("./settings.ini", _data);
    }
    
    private void LoginAnon_OnChecked(object sender, RoutedEventArgs e)
    {
        UsernameInput.IsEnabled = PasswordInput.IsEnabled = !SteamConfig.LoginAnonymous;
    }

    public class SteamData
    {
        public bool LoginAnonymous { get; set; } = true;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string SteamApiKey { get; set; } = "";
    }

    public class ConfigData
    {
        public string DownloadDirectory { get; set; }
    }
}