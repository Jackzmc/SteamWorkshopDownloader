using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Accessibility;
using Newtonsoft.Json;
using Path = System.IO.Path;

namespace WorkshopDownloader
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly Regex WorkshopUrlRegex =
            new Regex(@"https?://steamcommunity.com\/(workshop|sharedfiles)/filedetails/\?id=(\d+)");
        
        private int itemId;
        private int gameId;

        private List<WorkshopItem> downloadQueue = new List<WorkshopItem>();

        private HttpClient http;
        private Process? SteamCMDProcess;

        private SettingsWindow SettingsWindow;
        
        public MainWindow()
        {
            SettingsWindow = new SettingsWindow();
            AppDomain.CurrentDomain.ProcessExit += CurrentDomainOnProcessExit;
            http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "WorkshopDownloader/v0.1.0");
            InitializeComponent();
            DownloadDirectory.Text = SettingsWindow.GeneralConfig.DownloadDirectory;
            FetchButton.Click += FetchUrl;
            DownloadButton.Click += DownloadItems;
            WorkshopUrl.TextChanged += ValidateFetchButton;
            DownloadDirectory.TextChanged += ValidateFetchButton;
            Items.AddHandler(KeyDownEvent, new KeyEventHandler(OnWorkshopItemKeyDown));
        }

        private void OpenSettingsWindow(object sender, RoutedEventArgs routedEventArgs)
        {
            SettingsWindow.Show();
        }

        private void CurrentDomainOnProcessExit(object? sender, EventArgs e)
        {
            SteamCMDProcess?.Kill();
        }

        private async Task UpdateSteamCMD(DirectoryInfo di)
        {
            di.Delete();
            if (OperatingSystem.IsWindows())
            {
                di.Create();
                var dest = Path.Join(di.ToString(), "steamcmd.zip");
                var response = await http.GetAsync("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip");
                await using (var fs = new FileStream(dest, FileMode.CreateNew))
                {
                    await response.Content.CopyToAsync(fs);
                }
                Debug.WriteLine("Extracting");
                ZipFile.ExtractToDirectory(dest, di.ToString());
                Debug.WriteLine("Cleaning up");
                File.Delete(dest);
            }
            else
            {
                MessageBox.Show("OS Not Supported, sorry", "", MessageBoxButton.OK, MessageBoxImage.Error);
                throw new NotImplementedException();
            }
        }
        private async void DownloadItems(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Checking for steamcmd");
            DirectoryInfo di = new DirectoryInfo("./steamcmd");
            Debug.WriteLine(di.ToString());
            if (!di.Exists)
            {
                Debug.WriteLine("No steamcmd folder found, downloading....");
                await UpdateSteamCMD(di);
            }

            var args = new StringBuilder($"+force_install_dir \"{DownloadDirectory.Text}\" +login ");
            if (SettingsWindow.SteamConfig.LoginAnonymous)
                args.Append("anonymous");
            else
                args.Append($"{SettingsWindow.SteamConfig.Username} {SettingsWindow.SteamConfig.Password}");
            foreach (var entry in downloadQueue)
            {
                args.Append($" +workshop_download_item {entry.ConsumerAppId} {entry.PublishedFileId}");
            }

            // args.Append(" +quit");
            Debug.WriteLine($"Running: {args}");
            var output = new StreamWriter(new FileStream(Path.Join(di.ToString(), "output.log"), FileMode.Create), Encoding.UTF8);
            var processInfo = new ProcessStartInfo(Path.Join(di.ToString(), "steamcmd.exe"), args.ToString())
            {
                WorkingDirectory = di.ToString(),
                // RedirectStandardOutput = true,
                // RedirectStandardError = true
            };
            var process = new Process();
            process.StartInfo = processInfo;
            /*process.OutputDataReceived += (_, ed) =>
            {
                Debug.WriteLine(ed.Data);
                Debug.Flush();
                output.WriteLine("[Out] " + ed.Data);
                output.Flush();
                if (ed.Data != null && ed.Data.Contains("Two-factor code"))
                {
                    Debug.WriteLine("-- TWO FACTOR NEEDED --");
                    MessageBox.Show()
                }
            };
            process.ErrorDataReceived += (_, ed) =>
            {
                Debug.WriteLine(ed.Data);
                Debug.Flush();
                output.WriteLine("[Err] " + ed.Data);
                output.Flush();
            };*/
            process.Start();
            // Debug.WriteLine($"Got process of {process.Id}");
            /*process.BeginOutputReadLine();
            process.BeginErrorReadLine();*/
            await process.WaitForExitAsync();
            output.Close();
        }

        private void ValidateFetchButton(object sender, TextChangedEventArgs e)
        {
            if (Directory.Exists(DownloadDirectory.Text))
            {
                FetchButton.IsEnabled = WorkshopUrlRegex.IsMatch(WorkshopUrl.Text);
                DownloadButton.IsEnabled = downloadQueue.Count > 0;
                if(SettingsWindow.GeneralConfig.DownloadDirectory != DownloadDirectory.Text)
                {
                    SettingsWindow.GeneralConfig.DownloadDirectory = DownloadDirectory.Text;
                    SettingsWindow.SaveSettings();
                }
            }
            else
            {
                FetchButton.IsEnabled = false;
                DownloadButton.IsEnabled = false;
            }
        }

        private async void FetchUrl(object sender, RoutedEventArgs routedEventArgs)
        {
            DownloadButton.IsEnabled = false;
            var match = WorkshopUrlRegex.Match(WorkshopUrl.Text);
            if (match.Success)
            {
                var id = match.Groups[2].Value;
                Debug.WriteLine($"fetching id = {id}");
                try
                {
                    var children = await GetCollectionChildren(id);
                    if (children != null)
                    {
                        var items = await GetItemDetails(children);
                        downloadQueue.AddRange(items);
                        foreach (var item in items)
                        {
                            Items.Items.Add(new Label().Content = $"{item.Title} ({item.PublishedFileId})");
                        }
                    }
                    else
                    {
                        var items = await GetItemDetails(new string[]{ id });
                        downloadQueue.Add(items[0]);
                        Items.Items.Add(new Label().Content = $"{items[0].Title} ({items[0].PublishedFileId})");
                    }

                    DownloadButton.IsEnabled = true;
                    FetchButton.IsEnabled = false;
                    WorkshopUrl.Text = "";
                }
                catch (Exception ex)
                {
                    // ignored
                }
            }
            else
            {
                MessageBox.Show("Url is not a valid workshop url", "Invalid Url", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddItem(WorkshopItem item)
        {
            downloadQueue.Add(item);
            var entry = new ListBoxItem()
            {
                Content = new Label()
                {
                    Content = $"{item.Title} ({item.PublishedFileId})",
                    Name = item.PublishedFileId
                }
            };
            var menu = Items.Items.Add(entry);
        }

        private void OnWorkshopItemMouseClick(object sender, MouseButtonEventArgs e)
        {
            var label = (Label)sender;
            OpenBrowser($"https://steamcommunity.com/sharedfiles/filedetails/?id={label.Name}");
        }

        private void OnWorkshopItemKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Delete or Key.Back)
            {
                Debug.WriteLine("[correct] Key: " + e.Key.ToString());
                Debug.WriteLine("Selected items: " + Items.SelectedItems.Count);
                foreach (var item in Items.SelectedItems)
                {
                    Debug.WriteLine($"item {item}");
                    if (item is Label label)
                    {
                        Debug.WriteLine($"Delete: {label.Content}");
                    }
                }
            }
            else
            {
                Debug.WriteLine("Key: " + e.Key.ToString() + " SystemKey: " + e.SystemKey.ToString());
            }
        }
        
        public static void OpenBrowser(string url)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}"));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
        }

        private async Task<WorkshopItem[]> GetItemDetails(string[] ids)
        {
            var publishedfileids = ids.Select((id, i) => $"publishedfileids[{i}]={id}").ToArray();
            var content =
                new StringContent($"itemcount={publishedfileids.Length}&{string.Join("&", publishedfileids)}");
            content.Headers.Clear();
            content.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
            var response = await http.PostAsync(
                "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", content);
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var res = JsonConvert.DeserializeObject<GetPublishedFileDetailsResponse>(body);
                if (res?.response != null)
                {
                    return res.response.publishedfiledetails;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                MessageBox.Show(body, $"GetItemDetails failed: {response.StatusCode.ToString()}", MessageBoxButton.OK, MessageBoxImage.Error);
                throw new Exception("Request failed");
            }
        }

        private async Task<string[]?> GetCollectionChildren(string id)
        {
            var content =
                new StringContent($"collectioncount=1&publishedfileids[0]={id}");
            content.Headers.Clear();
            content.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
            var response = await http.PostAsync(
                    "https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/", content);
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var res = JsonConvert.DeserializeObject<GetCollectionDetailsResponse>(body);
                if (res?.response?.resultcount > 0)
                {
                    return res.response.collectiondetails[0].children.Select(child => child.publishedfileid)
                        .ToArray();
                }
                else
                {
                    return null;
                }
            }
            else
            {
                MessageBox.Show(body, $"GetCollectionChildren failed: {response.StatusCode.ToString()}", MessageBoxButton.OK, MessageBoxImage.Error);
                throw new Exception("Request failed");
            }
        }

        [Serializable]
        private class GetCollectionDetailsResponse
        {
            public GetCollectionDetailsResponseResponse response;
        }
        
        [Serializable]
        private class GetCollectionDetailsResponseResponse
        {
            public int resultcount;
            public CollectionDetail[] collectiondetails;
        }
        [Serializable]
        private class CollectionDetail
        {
            public CollectionChildren[] children;
        }
        [Serializable]
        private class CollectionChildren
        {
            public string publishedfileid;
            public int sortorder;
            public int filetype;
        }

        [Serializable]
        private class GetPublishedFileDetailsResponse
        {
            public GetPublishedFileDetailsResponseResponse response;
        }

        [Serializable]
        private class GetPublishedFileDetailsResponseResponse
        {
            public WorkshopItem[] publishedfiledetails;
        }

        [Serializable]
        #pragma warning disable CS8618
        private class WorkshopItem
        {
            [JsonProperty("result")] public int Result;
            [JsonProperty("publishedfileid")] public string PublishedFileId;
            [JsonProperty("creator")] public string Creator;
            [JsonProperty("creator_app_id")] public int CreatorAppId;
            [JsonProperty("consumer_app_id")] public int ConsumerAppId;
            [JsonProperty("filename")] public string FileName;
            [JsonProperty("file_size")] public int FileSize;
            [JsonProperty("file_url")] public string FileUrl;
            [JsonProperty("preview_url")] public string PreviewUrl;
            [JsonProperty("hcontent_preview")] public string HContentPreview;
            [JsonProperty("title")] public string Title;
            [JsonProperty("description")] public string Description;
            [JsonProperty("time_created")] public int TimeCreated;
            [JsonProperty("time_updated")] public int TimeUpdated;
            [JsonProperty("subscriptions")] public int Subscriptions;
            [JsonProperty("favorited")] public int Favorited;
            [JsonProperty("views")] public int Views;
        }
        #pragma warning restore CS8618


    }
}