using System;
using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using System.Threading.Tasks;
using System.Text;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Devices.Enumeration;
using Windows.Gaming.Input;
using Windows.System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace parallaX
{

    // iNiT
    public sealed partial class MainPage : Page
    {

        static StringBuilder logStr = new StringBuilder();
        static ulong sizeCounter = 0;

        [DllImport("XboxOneExtensions.dll", SetLastError = true)]
        static extern IntPtr LoadLibrary(string lpFileName);

        static bool CheckLibrary(string fileName)
        {
            return LoadLibrary(fileName) == IntPtr.Zero;
        }

        public MainPage()
        {
            this.InitializeComponent();

            ShowSplashPanel.Begin();
            ShowLogo.Begin();
            ShowMenu.Begin();
        }

        // LOGPANEL UPDATER
        public void updateText(string text)
        {
            textBoxDebug.Text += text;
            scrollView.ScrollToVerticalOffset(scrollView.MaxHeight);
        }

        // HTTP POST
        public async void PostAsync(string uri, byte[] data)
        {
            GC.Collect();
            ByteArrayContent byteContent = new ByteArrayContent(data);
            var httpClient = new HttpClient();
            await httpClient.PostAsync(uri, byteContent);
            Debug.WriteLine("Upload finished!");
            // textBoxDebug.Text += "\nUpload finished!\r\n";
        }

        // DUMPER
        private async void Dump(string target, string method, string server = "unknown")
        {
            bool bIsFile = false;
            if(target.Substring(target.Length - 1)=="\\")
                bIsFile = false;
            else
                bIsFile = true;

            sizeCounter = 0;
            logStr = new StringBuilder();

            textBoxDebug.Text = "";

            updateText("* * * * * * * * * * * * * * * *\n");
            updateText("* * *  D U M P i N G  * * *\n");
            updateText("* * * * * * * * * * * * * * * *\n\n");

            showDebugPanel();

            if (bIsFile)
                updateText("F i L E :: " + target + "\n");
            else
                updateText("D i R :: " + target + "\n");

            try
            {
                if (method == "net")
                {
                    updateText("D E S T :: NETWORK\n\n");

                    if (!bIsFile)
                    {
                        foreach (string f in Directory.GetFiles(target))
                        {
                            byte[] array = File.ReadAllBytes(f);
                            PostAsync(server+"/dump.php?filename=" + f, array);
                        }

                        foreach (string d in Directory.GetDirectories(target))
                        {
                            Dump(d, method);
                        }
                    }
                    else if(bIsFile)
                    {
                        Debug.WriteLine("net dump only supports dirs");
                        updateText("ERROR: Dump method 'net' only supports directories.\n\n* * *  D U M P  F A I L E D  * * * \n");
                    }
                }
                else if (method == "usb")
                {
                    // Find all storage devices using the known folder
                    var removableStorages = await KnownFolders.RemovableDevices.GetFoldersAsync();
                    if (removableStorages.Count > 0)
                    {
                        // Display each storage device
                        foreach (StorageFolder storage in removableStorages)
                        {
                            Debug.WriteLine("USB found!");
                            updateText("D E S T :: USB DEViCE\n\n");

                            if (!bIsFile)
                            {
                                StorageFolder sourceDir = await StorageFolder.GetFolderFromPathAsync(target);
                                await CopyFolderAsync(sourceDir, storage);
                            }
                            else if(bIsFile)
                            {
                                StorageFile sourceFile = await StorageFile.GetFileFromPathAsync(target);
                                await CopyFileAsync(sourceFile, storage);

                                BasicProperties pro = await sourceFile.GetBasicPropertiesAsync();
                                sizeCounter = sizeCounter + pro.Size;
                            }

                            logStr.Append(SizeSuffix(Convert.ToInt64(sizeCounter)).ToString() + " bytes dumped!\r\n");
                            Debug.WriteLine(SizeSuffix(Convert.ToInt64(sizeCounter)).ToString() + " bytes dumped!");

                            updateText("\nDUMPSiZE: " + SizeSuffix(Convert.ToInt64(sizeCounter)).ToString() + " bytes\r\n");
                            updateText("\n* * *  D U M P  S U C C E S S F U L  * * * \n");

                            StorageFile sampleFile = await storage.CreateFileAsync("log.txt", Windows.Storage.CreationCollisionOption.ReplaceExisting);
                            await FileIO.WriteTextAsync(sampleFile, logStr.ToString());
                            break;
                        }
                        updateText("\r\n");
                    }
                    else
                    {
                        updateText("ERROR: USB not found.\n\n* * *  D U M P  F A I L E D  * * * \n");
                        Debug.WriteLine("USB not found!");
                    }
                }
            }
            catch (Exception ex)
            {
                updateText("ERROR: " + ex.Message + "\n\n* * *  D U M P  F A I L E D  * * * \n");
                Debug.WriteLine(ex.Message);
            }
        }

        //Folder copy function
        public async Task CopyFolderAsync(StorageFolder source, StorageFolder destination)
        {
            StorageFolder destinationFolder = null;
            destinationFolder = await destination.CreateFolderAsync(source.Name, CreationCollisionOption.OpenIfExists);

            foreach (StorageFile file in await source.GetFilesAsync())
            {
                BasicProperties pro = await file.GetBasicPropertiesAsync();
                sizeCounter = sizeCounter + pro.Size;
                Debug.WriteLine(file.Name + " (" + SizeSuffix(Convert.ToInt64(pro.Size)) + ") // Total dumped: " + SizeSuffix(Convert.ToInt64(sizeCounter)));
                updateText(file.Name + " (Size: " + SizeSuffix(Convert.ToInt64(pro.Size)) + ")\n");
                await file.CopyAsync(destinationFolder, file.Name + ".x", NameCollisionOption.ReplaceExisting);

            }
            foreach (StorageFolder folder in await source.GetFoldersAsync())
            {
                await CopyFolderAsync(folder, destinationFolder);
            }
        }

        //File copy function
        public async Task CopyFileAsync(StorageFile source, StorageFolder destination)
        {
            await source.CopyAsync(destination, source.Name + ".x", NameCollisionOption.ReplaceExisting);
        }

        //Size analytics
        static readonly string[] SizeSuffixes = { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
        static string SizeSuffix(Int64 value)
        {
            if (value < 0) { return "-" + SizeSuffix(-value); }
            if (value == 0) { return "0.0 bytes"; }

            int mag = (int)Math.Log(value, 1024);
            decimal adjustedSize = (decimal)value / (1L << (mag * 10));

            return string.Format("{0:n1} {1}", adjustedSize, SizeSuffixes[mag]);
        }

        //Test Read & Write permissions on folder <dir>
        private void testRWX(string dir)
        {
            bool validDir = false;
            bool r = false;
            bool w = false;
            bool x = false;

            try
            {
                Debug.WriteLine("Testing RWX for " + dir + ":");
                string[] files = Directory.GetFiles(dir);
                string[] dirs = Directory.GetDirectories(dir);
                if (files.Length > 0 || dirs.Length > 0)
                    validDir = true;

                if (validDir)
                {
                    Debug.WriteLine("R is true!");
                    r = true;

                    string testline = "test";
                    string testfile = "test.txt";
                    using (StreamWriter sw = File.CreateText(dir + testfile))
                    {
                        sw.WriteLine(testline);
                    }

                    using (StreamReader sr = File.OpenText(dir + testfile))
                    {
                        if (sr.ReadLine() != null)
                        {
                            w = true;
                            Debug.WriteLine("W is true!");
                        }
                    }
                }

                Debug.WriteLine("Result: R: " + r.ToString() + " W: " + w.ToString() + " X: " + w.ToString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                updateText("ERROR: " + ex.Message + "\n");
            }
        }

        // GET USERS
        private async void GetUsers()
        {
            var removableStorages = await KnownFolders.RemovableDevices.GetFoldersAsync();
            if (removableStorages.Count > 0)
            {
                foreach (StorageFolder storage in removableStorages)
                {
                    StringBuilder usrStr = new StringBuilder();
                    int counter = 0;
                    IReadOnlyList<User> users = await User.FindAllAsync();
                    foreach (User user in users)
                    {
                        counter = counter + 1;
                        usrStr.Append("User No.: " + counter + "\r\n");
                        usrStr.Append("AccountName: " + (string)await user.GetPropertyAsync(KnownUserProperties.AccountName) + "\r\n");
                        usrStr.Append("DisplayName: " + (string)await user.GetPropertyAsync(KnownUserProperties.DisplayName) + "\r\n");
                        usrStr.Append("DomainName: " + (string)await user.GetPropertyAsync(KnownUserProperties.DomainName) + "\r\n");
                        usrStr.Append("FirstName: " + (string)await user.GetPropertyAsync(KnownUserProperties.FirstName) + "\r\n");
                        usrStr.Append("GuestHost: " + (string)await user.GetPropertyAsync(KnownUserProperties.GuestHost) + "\r\n");
                        usrStr.Append("LastName: " + (string)await user.GetPropertyAsync(KnownUserProperties.LastName) + "\r\n");
                        usrStr.Append("PrincipleName: " + (string)await user.GetPropertyAsync(KnownUserProperties.PrincipalName) + "\r\n");
                        usrStr.Append("ProviderName: " + (string)await user.GetPropertyAsync(KnownUserProperties.ProviderName) + "\r\n");
                        usrStr.Append("SIP Uri: " + (string)await user.GetPropertyAsync(KnownUserProperties.SessionInitiationProtocolUri) + "\r\n");
                        usrStr.Append("Auth Status: " + user.AuthenticationStatus.ToString() + "\r\n");
                        usrStr.Append("User Type: " + user.Type.ToString() + "\r\n\r\n");
                    }
                    Debug.Write(usrStr);
                    StorageFile sampleFile = await storage.CreateFileAsync("users.txt", Windows.Storage.CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteTextAsync(sampleFile, usrStr.ToString());
                    break;
                }
            }
        }

        ///////////////////////////////////
        // B U T T O N S
        ///////////////////////////

        // DiRDUMP
        private async void dirdumpBTN_Click(object sender, RoutedEventArgs e)
        {
            if (dumpSource.Text == "")
            {
                textBoxDebug.Text = "";

                updateText("* * * * * * * * * * * * *\n");
                updateText("* * *  E R R O R  * * *\n");
                updateText("* * * * * * * * * * * * *\n\n");
                updateText("Set a 'DUMPSOURCE' in Settings and try again...");

                showDebugPanel();
            }
            else
            {
                Dump(dumpSource.Text, dumpDestination.Text);
            }
        }

        // READFiLE>BYTES
        public async Task<byte[]> ReadFile(StorageFile file)
        {
            byte[] fileBytes = null;
            using (IRandomAccessStreamWithContentType stream = await file.OpenReadAsync())
            {
                fileBytes = new byte[stream.Size];
                using (DataReader reader = new DataReader(stream))
                {
                    await reader.LoadAsync((uint)stream.Size);
                    reader.ReadBytes(fileBytes);
                }
            }

            return fileBytes;
        }

        // DiSCDUMP
        private async void discdumpBTN_Click(object sender, RoutedEventArgs e)
        {

            /*
            gameThumb.Opacity = 1;

            BitmapImage bitmapImage = new BitmapImage();
            bitmapImage.UriSource = new Uri("O:\\MSXC\\Metadata\\Package0.xvc\\480x480_1.png");
            gameThumb.Source = bitmapImage;
            */

            updateText("");

            updateText("* * * * * * * * * * * * * * * * * * * * * *\n");
            updateText("* * *  D U M P i N G  G A M E  * * *\n");
            updateText("* * * * * * * * * * * * * * * * * * * * * *\n\n");

            updateText("S O U R C E :: BD-ROM\n");
            updateText("D E S T :: USB DEViCE\n\n");

            showDebugPanel();

            try
            {
                StorageFile catalog = await StorageFile.GetFileFromPathAsync("O:\\MSXC\\Metadata\\catalog.js");
                byte[] test = await ReadFile(catalog);
                string text  = System.Text.Encoding.Unicode.GetString(test);
                var game = JsonConvert.DeserializeObject<dynamic>(text);

                string version = game.version;
                string title = game.packages[0].vui[0].title;
                string size = game.packages[0].size;

                updateText("Title: " + title + " (v" + version + ")\n");
                updateText("Size: " + size + " bytes\n\n");

            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        // FiLEBROWSER
        private void filebrowserBTN_Click(object sender, RoutedEventArgs e)
        {
            showFilebrowserPanel();
        }

        // TOOLBOX
        private void toolboxBTN_Click(object sender, RoutedEventArgs e)
        {
            showToolboxPanel();
        }

        // DEViCELiST
        private async void devicelistBTN_Click(object sender, RoutedEventArgs e)
        {
            textBoxDebug.Text = "";

            updateText("* * * * * * * * * * * * * * * * * * * * * * * * *\n");
            updateText("* * *  D E V i C E L i S T - D U M P  * * *\n");
            updateText("* * * * * * * * * * * * * * * * * * * * * * * * *\n\n");

            showDebugPanel();

            var removableStorages = await KnownFolders.RemovableDevices.GetFoldersAsync();
            if (removableStorages.Count > 0)
            {
                // Display each storage device
                foreach (StorageFolder storage in removableStorages)
                {
                    StringBuilder devStr = new StringBuilder();
                    int counter = 0;
                    foreach (DeviceInformation di in await DeviceInformation.FindAllAsync())
                    {
                        counter = counter + 1;
                        devStr.Append("Dev No." + counter + "\r\nID: " + di.Id + "\r\nDefault: " + di.IsDefault + "\r\nEnabled: " + di.IsEnabled + "\r\nKind: " + di.Kind + "\r\nName: " + di.Name + "\r\n\r\n");
                    }
                    Debug.Write(devStr);
                    updateText(devStr.ToString());
                    StorageFile sampleFile = await storage.CreateFileAsync("devices.txt", Windows.Storage.CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteTextAsync(sampleFile, devStr.ToString());
                    break;
                }

                updateText("Devicelist successfully exported...");
            }
            else
            {
                updateText("Unable to save file.\nNo USB Device found!");
            }
        }

        // USERLiST
        private async void userlistBTN_Click(object sender, RoutedEventArgs e)
        {

            textBoxDebug.Text = "";

            updateText("* * * * * * * * * * * * * * * * * * * * * * *\n");
            updateText("* * *  U S E R L i S T - D U M P  * * *\n");
            updateText("* * * * * * * * * * * * * * * * * * * * * * *\n\n");

            showDebugPanel();

            var removableStorages = await KnownFolders.RemovableDevices.GetFoldersAsync();
            if (removableStorages.Count > 0)
            {
                foreach (StorageFolder storage in removableStorages)
                {
                    StringBuilder usrStr = new StringBuilder();
                    int counter = 0;
                    IReadOnlyList<User> users = await User.FindAllAsync();
                    foreach (User user in users)
                    {
                        counter = counter + 1;
                        usrStr.Append("User No.: " + counter + "\r\n");
                        usrStr.Append("AccountName: " + (string)await user.GetPropertyAsync(KnownUserProperties.AccountName) + "\r\n");
                        usrStr.Append("DisplayName: " + (string)await user.GetPropertyAsync(KnownUserProperties.DisplayName) + "\r\n");
                        usrStr.Append("DomainName: " + (string)await user.GetPropertyAsync(KnownUserProperties.DomainName) + "\r\n");
                        usrStr.Append("FirstName: " + (string)await user.GetPropertyAsync(KnownUserProperties.FirstName) + "\r\n");
                        usrStr.Append("GuestHost: " + (string)await user.GetPropertyAsync(KnownUserProperties.GuestHost) + "\r\n");
                        usrStr.Append("LastName: " + (string)await user.GetPropertyAsync(KnownUserProperties.LastName) + "\r\n");
                        usrStr.Append("PrincipleName: " + (string)await user.GetPropertyAsync(KnownUserProperties.PrincipalName) + "\r\n");
                        usrStr.Append("ProviderName: " + (string)await user.GetPropertyAsync(KnownUserProperties.ProviderName) + "\r\n");
                        usrStr.Append("SIP Uri: " + (string)await user.GetPropertyAsync(KnownUserProperties.SessionInitiationProtocolUri) + "\r\n");
                        usrStr.Append("Auth Status: " + user.AuthenticationStatus.ToString() + "\r\n");
                        usrStr.Append("User Type: " + user.Type.ToString() + "\r\n\r\n");
                    }
                    Debug.Write(usrStr);
                    updateText(usrStr.ToString());
                    StorageFile sampleFile = await storage.CreateFileAsync("users.txt", Windows.Storage.CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteTextAsync(sampleFile, usrStr.ToString());
                    break;
                }

                updateText("Userlist successfully exported...");
            }
            else
            {
                updateText("Unable to save file.\nNo USB Device found!");
            }

        }

        // SETTiNGS
        private void settingsBTN_Click(object sender, RoutedEventArgs e)
        {
            showSettingsPanel();
        }

        ///////////////////////////////////
        // T R A N S i T i O N S
        ///////////////////////////

        private void showDebugPanel()
        {
            if (splashPanel.Opacity == 1)
            {
                HideSplashPanel.Begin();
            }
            if (toolboxPanel.Opacity == 1)
            {
                toolboxPanelFadeOut.Begin();
            }
            if (filebrowserPanel.Opacity == 1)
            {
                filebrowserPanelFadeOut.Begin();
            }
            if (settingsPanel.Opacity == 1)
            {
                settingsPanelFadeOut.Begin();
            }
            if (debugPanel.Opacity == 0)
            {
                Canvas.SetZIndex(debugPanel, 9999);
                Canvas.SetZIndex(filebrowserPanel, 0);
                Canvas.SetZIndex(toolboxPanel, 0);
                Canvas.SetZIndex(settingsPanel, 0);

                debugPanelFadeIn.Begin();
            }
        }

        private void showFilebrowserPanel()
        {
            if (splashPanel.Opacity == 1)
            {
                HideSplashPanel.Begin();
            }
            if (debugPanel.Opacity == 1)
            {
                debugPanelFadeOut.Begin();
            }
            if (toolboxPanel.Opacity == 1)
            {
                toolboxPanelFadeOut.Begin();
            }
            if (settingsPanel.Opacity == 1)
            {
                settingsPanelFadeOut.Begin();
            }
            if (filebrowserPanel.Opacity == 0)
            {
                Canvas.SetZIndex(debugPanel, 0);
                Canvas.SetZIndex(filebrowserPanel, 9999);
                Canvas.SetZIndex(toolboxPanel, 0);
                Canvas.SetZIndex(settingsPanel, 0);

                filebrowserPanelFadeIn.Begin();
            }
        }

        private void showToolboxPanel()
        {
            if (splashPanel.Opacity == 1)
            {
                HideSplashPanel.Begin();
            }
            if (debugPanel.Opacity == 1)
            {
                debugPanelFadeOut.Begin();
            }
            if (filebrowserPanel.Opacity == 1)
            {
                filebrowserPanelFadeOut.Begin();
            }
            if (settingsPanel.Opacity == 1)
            {
                settingsPanelFadeOut.Begin();
            }
            if (toolboxPanel.Opacity == 0)
            {
                Canvas.SetZIndex(debugPanel, 0);
                Canvas.SetZIndex(filebrowserPanel, 0);
                Canvas.SetZIndex(toolboxPanel, 9999);
                Canvas.SetZIndex(settingsPanel, 0);

                toolboxPanelFadeIn.Begin();
            }
        }

        private void showSettingsPanel()
        {
            if (splashPanel.Opacity == 1)
            {
                HideSplashPanel.Begin();
            }
            if (debugPanel.Opacity == 1)
            {
                debugPanelFadeOut.Begin();
            }
            if (toolboxPanel.Opacity == 1)
            {
                toolboxPanelFadeOut.Begin();
            }
            if (filebrowserPanel.Opacity == 1)
            {
                filebrowserPanelFadeOut.Begin();
            }
            if (settingsPanel.Opacity == 0)
            {
                Canvas.SetZIndex(debugPanel, 0);
                Canvas.SetZIndex(filebrowserPanel, 0);
                Canvas.SetZIndex(toolboxPanel, 0);
                Canvas.SetZIndex(settingsPanel, 9999);

                settingsPanelFadeIn.Begin();
            }
        }
    }
}