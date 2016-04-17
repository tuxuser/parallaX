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
using Windows.UI.Xaml.Media;
using Windows.UI;

namespace parallaX
{

    // iNiT
    public sealed partial class MainPage : Page
    {

        static StringBuilder logStr = new StringBuilder();
        static StringBuilder rwxStr = new StringBuilder();
        static ulong sizeCounter = 0;
        static bool testWritePerm = false;

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

        // DUMPER
        private async Task Dump(string target, string method, string server = "unknown", bool checkWritePerm = false)
        {
            bool bIsFile = false;

            if (target.Substring(target.Length - 1) == "\\")
                bIsFile = false;
            else
                bIsFile = true;

            if (checkWritePerm)
                testWritePerm = true;
            else
                testWritePerm = false;

            sizeCounter = 0;
            rwxStr = new StringBuilder();
            logStr = new StringBuilder();

            textBoxDebug.Text = "";

			showDebugPanel();
            try
            {
                StorageFolder storage = null;
                HttpClient client = null;
                if (method == "usb")
                {
                    // Find all storage devices using the known folder
                    var removableStorages = await KnownFolders.RemovableDevices.GetFoldersAsync();
                    if (removableStorages.Count <= 0)
                    {
                        updateText("ERROR: USB not found.\n\n* * *  D U M P  F A I L E D  * * * \n");
                        Debug.WriteLine("USB not found!");
                    }

                    // Display each storage device
                    storage = removableStorages[0];

                    Debug.WriteLine("USB found!");
                }
                else if (method == "net")
                {
                    client = new HttpClient();
                }

                if (!bIsFile)
                {
                    StorageFolder sourceDir = await StorageFolder.GetFolderFromPathAsync(target);
                    if (method == "usb")
                        await CopyFolderAsync(sourceDir, storage);
                    else if (method == "net")
                        await HttpDumpFolder(client, sourceDir, server);
                }
                else if (bIsFile)
                {
                    StorageFile sourceFile = await StorageFile.GetFileFromPathAsync(target);
                    if (method == "usb")
                        await CopyFileAsync(sourceFile, storage);
                    else if (method == "net")
                        await HttpDumpFile(client, sourceFile, server);

                    BasicProperties pro = await sourceFile.GetBasicPropertiesAsync();
                    sizeCounter = sizeCounter + pro.Size;
                }

                logStr.Append(SizeSuffix(Convert.ToInt64(sizeCounter)).ToString() + " bytes dumped!\r\n");
                Debug.WriteLine(SizeSuffix(Convert.ToInt64(sizeCounter)).ToString() + " bytes dumped!");

                updateText("\nDUMPSiZE: " + SizeSuffix(Convert.ToInt64(sizeCounter)).ToString() + " bytes\r\n");
                updateText("\n* * *  D U M P  S U C C E S S F U L  * * * \n");

                if (method == "usb")
                {
                    StorageFile sampleFile = await storage.CreateFileAsync("log.txt", Windows.Storage.CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteTextAsync(sampleFile, logStr.ToString());
                    if (testWritePerm)
                    {
                        IStorageFile rwxFile = await storage.CreateFileAsync("rwx.txt", Windows.Storage.CreationCollisionOption.ReplaceExisting);
                        await FileIO.WriteTextAsync(rwxFile, rwxStr.ToString());
                    }
                }
                updateText("\r\n");
            }
            catch (Exception ex)
            {
                updateText("ERROR: " + ex.Message + "\n\n* * *  D U M P  F A I L E D  * * * \n");
                Debug.WriteLine(ex.Message);
            }
        }

        // HTTP POST
        public async Task PostAsync(HttpClient client, string uri, byte[] data)
        {
            GC.Collect();
            ByteArrayContent byteContent = new ByteArrayContent(data);
            await client.PostAsync(uri, byteContent);
            //Debug.WriteLine("Upload finished!");
            // textBoxDebug.Text += "\nUpload finished!\r\n";
        }

        public async Task HttpDumpFolder(HttpClient client, StorageFolder source, String server)
        {
            IReadOnlyList<IStorageItem> list = await source.GetItemsAsync();
            foreach (IStorageItem item in list)
            {
                if (item.IsOfType(StorageItemTypes.Folder))
                {
                    await HttpDumpFolder(client, (StorageFolder)item, server);
                }
                else if (item.IsOfType(StorageItemTypes.File))
                {
                    StorageFile file = (StorageFile)item;
                    BasicProperties pro = await item.GetBasicPropertiesAsync();
                    sizeCounter = sizeCounter + pro.Size;
                    Debug.WriteLine(item.Name + " (" + SizeSuffix(Convert.ToInt64(pro.Size)) + ") // Total dumped: " + SizeSuffix(Convert.ToInt64(sizeCounter)));
                    updateText(item.Path + " (Size: " + SizeSuffix(Convert.ToInt64(pro.Size)) + ")\n");
                    byte[] array = await ReadFile(file);
                    await PostAsync(client, server + "/dump.php?filename=" + file.Path, array);
                }
                else
                {
                    updateText(item.Name + "is of unknown type!!! (maybe hardlink)\n");
                }

            }
        }

        public async Task HttpDumpFile(HttpClient client, StorageFile source, String server)
        {
            byte[] array = await ReadFile(source);
            await PostAsync(client, server + "/dump.php?filename=" + source.Path, array);
        }

        //Folder copy function
        public async Task CopyFolderAsync(StorageFolder source, StorageFolder destination)
        {
            StorageFolder destinationFolder = null;
            // Replace ':' so 'C:\' => 'C\'
            String outputDir = "dump\\" + source.Path.Replace(":", "");
            destinationFolder = await destination.CreateFolderAsync(outputDir, CreationCollisionOption.OpenIfExists);
            IReadOnlyList<IStorageItem> list = await source.GetItemsAsync();
            foreach (IStorageItem item in list)
            {
                if (item.IsOfType(StorageItemTypes.Folder))
                {
                    await CopyFolderAsync((StorageFolder)item, destination);
                    if (testWritePerm)
                    {
                        try
                        {
                            string testline = "test";
                            string testfile = "rwx.txt";
                            IStorageFolder fldr = (IStorageFolder)item;
                            IStorageFile testFile = await fldr.CreateFileAsync(testfile, Windows.Storage.CreationCollisionOption.ReplaceExisting);
                            await Windows.Storage.FileIO.WriteTextAsync(testFile, testline);

                            if (File.Exists(testFile.Path))
                            {
                                rwxStr.Append("FOLDER: " + testFile.Path + " TRUE");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("ERROR: " + ex.Message);
                        }
                    }
                }
                else if (item.IsOfType(StorageItemTypes.File))
                {
                    Debug.WriteLine("Saving file: " + item.Path);
                    StorageFile file = (StorageFile)item;
                    BasicProperties pro = await item.GetBasicPropertiesAsync();
                    sizeCounter = sizeCounter + pro.Size;
                    Debug.WriteLine(item.Name + " (" + SizeSuffix(Convert.ToInt64(pro.Size)) + ") // Total dumped: " + SizeSuffix(Convert.ToInt64(sizeCounter)));
                    updateText(item.Path + " (Size: " + SizeSuffix(Convert.ToInt64(pro.Size)) + ")\n");
                    await file.CopyAsync(destinationFolder, item.Name + ".x", NameCollisionOption.ReplaceExisting);
          			if (testWritePerm)
                    {
                        try
                        {
                            await Windows.Storage.FileIO.AppendTextAsync(file, "0");
                            rwxStr.Append("FILE: " + file.Path + " TRUE");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("ERROR: " + ex.Message);
                        }
                    }
                }
                else
                {
                    updateText(item.Name + "is of unknown type!!! (maybe hardlink)\n");
                }
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

        ///////////////////////////////////
        // B U T T O N S
        ///////////////////////////

        // DiRDUMP
        private async void dirdumpBTN_Click(object sender, RoutedEventArgs e)
        {
            sizeCounter = 0;
            if (dumpSource.Text == "")
            {
                textBoxDebug.Text = "";

                updateText("* * * * * * * * * * * * *\n");
                updateText("* * * * * INFO * * *  * *\n");
                updateText("* * * * * * * * * * * * *\n\n");
                updateText("No 'DUMPSOURCE' given. Dumping RECURSIVE...");

                showDebugPanel();

                string drives = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
                foreach (char driveLetter in drives)
                {
                    await Dump(driveLetter + ":\\", dumpDestination.Text, "http://" + netServer.Text);
                }
            }
            else
            {
                updateText("");

                updateText("* * * * * * * * * * * * * * * * * * * * * * *\n");
                updateText("* * *  D U M P i N G  F i L E S  * * *\n");
                updateText("* * * * * * * * * * * * * * * * * * * * * * *\n\n");

                updateText("S O U R C E :: " + dumpSource.Text + "\n");
                updateText("T A R G E T :: " + dumpDestination.Text.ToUpper() + "\n\n");

                showDebugPanel();

                await Dump(dumpSource.Text, dumpDestination.Text, "http://" + netServer.Text);
            }
        }

        // DiSCDUMP
        private async void discdumpBTN_Click(object sender, RoutedEventArgs e)
        {
            sizeCounter = 0;
            updateText("");

            updateText("* * * * * * * * * * * * * * * * * * * * * *\n");
            updateText("* * *  D U M P i N G  G A M E  * * *\n");
            updateText("* * * * * * * * * * * * * * * * * * * * * *\n\n");

            updateText("S O U R C E :: BD-ROM\n");
            updateText("T A R G E T :: " + dumpDestination.Text.ToUpper() + "\n\n");

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

                updateText("GAME: " + title + " (v" + version + ")\n");
                updateText("SiZE: " + size + " bytes\n\n");

                await Dump(@"O:\MSXC\", dumpDestination.Text, netServer.Text);
                await Dump(@"O:\Licenses\", dumpDestination.Text, netServer.Text);

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

        // TEST-RWX
        private void testRWXBTN_Click(object sender, RoutedEventArgs e)
        {

            if (testRWXTarget.Text == "")
            {
                textBoxDebug.Text = "";

                updateText("* * * * * * * * * * * * *\n");
                updateText("* * *  E R R O R  * * *\n");
                updateText("* * * * * * * * * * * * *\n\n");
                updateText("Set a 'RWX-TARGET' and try again...");

                showDebugPanel();
            }
            else
            {
                textBoxDebug.Text = "";

                updateText("* * * * * * * * * * * * * * * * * * * * *\n");
                updateText("* * *  T E S T i N G  R W X  * * *\n");
                updateText("* * * * * * * * * * * * * * * * * * * * *\n\n");

                showDebugPanel();

                testRWX(testRWXTarget.Text);
            }
        }

        // EMPTY1
        private void empty1BTN_Click(object sender, RoutedEventArgs e)
        {
            // Do Something...
        }

        // EMPTY2
        private void empty2BTN_Click(object sender, RoutedEventArgs e)
        {
            // Do Something...
        }

        // SETTiNGS
        private void settingsBTN_Click(object sender, RoutedEventArgs e)
        {
            showSettingsPanel();
        }

        ///////////////////////////////////
        // T R A N S i T i O N S
        ///////////////////////////

        private void buttonGotFocus(object sender, RoutedEventArgs e)
        {
            Button current = (Button)sender;
            current.Background = new SolidColorBrush(Color.FromArgb(255, 80, 160, 0));
            current.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
        }

        private void buttonLostFocus(object sender, RoutedEventArgs e)
        {
            Button current = (Button)sender;
            current.Background = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
            current.Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 160, 0));
        }

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
