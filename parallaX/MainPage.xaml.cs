using System;
using System.IO;
using System.Diagnostics;
using System.Net.Http;
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
using Newtonsoft.Json;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media;
using Windows.UI;

namespace parallaX
{
    public sealed partial class MainPage : Page
    {
        static StringBuilder logStr = new StringBuilder();
        static StringBuilder rwxStr = new StringBuilder();
        static ulong sizeCounter = 0;
        static bool testWritePerm = false;

        public MainPage()
        {
            this.InitializeComponent();
            showSplashPanel();
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
            string[] targets = target.Split(',');

            foreach (string t in targets)
            {

                ////Debug.WriteLine("USB not found!");

                bool bIsFile = false;

                if (t.Substring(t.Length - 1) == "\\")
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
                            //Debug.WriteLine("USB not found!");
                            updateText("ERROR: USB not found.\n\n* * *  D U M P  F A I L E D  * * * \n");
                            playSound("onError.wav", false);
                        }

                        // Display each storage device
                        storage = removableStorages[0];

                        //Debug.WriteLine("USB found!");
                    }
                    else if (method == "net")
                    {
                        client = new HttpClient();
                    }

                    StorageFolder sourceDir = await StorageFolder.GetFolderFromPathAsync(t);

                    if (!bIsFile)
                    {

                        if (method == "usb")
                            await CopyFolderAsync(sourceDir, storage);
                        else if (method == "net")
                            await HttpDumpFolder(client, sourceDir, server);
                    }
                    else if (bIsFile)
                    {

                        StorageFile sourceFile = await StorageFile.GetFileFromPathAsync(t);

                        if (method == "usb")
                            await CopyFileAsync(sourceFile, storage);
                        else if (method == "net")
                            await HttpDumpFile(client, sourceFile, server);

                        BasicProperties pro = await sourceFile.GetBasicPropertiesAsync();
                        sizeCounter = sizeCounter + pro.Size;
                    }

                    logStr.Append(SizeSuffix(Convert.ToInt64(sizeCounter)).ToString() + " bytes dumped!\r\n");
                    //Debug.WriteLine(SizeSuffix(Convert.ToInt64(sizeCounter)).ToString() + " bytes dumped!");
                    updateText("\nDUMPSiZE: " + SizeSuffix(Convert.ToInt64(sizeCounter)).ToString() + " bytes\r\n");
                    updateText("\n* * *  D U M P  S U C C E S S F U L  * * * \n");
                    playSound("onSuccess.wav", false);
                    updateText("\n\n");

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
                }
                catch (Exception ex)
                {
                    //Debug.WriteLine(ex.Message);
                    updateText("ERROR: " + ex.Message + "\n\n* * *  D U M P  F A I L E D  * * * \n");
                    playSound("onError.wav", false);
                    updateText("\n\n");
                }
            }
        }

        // HTTP POST
        public async Task PostAsync(HttpClient client, string uri, byte[] data)
        {
            GC.Collect();
            ByteArrayContent byteContent = new ByteArrayContent(data);
            await client.PostAsync(uri, byteContent);
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
                    try
                    {
                        StorageFile file = (StorageFile)item;
                        BasicProperties pro = await item.GetBasicPropertiesAsync();
                        sizeCounter = sizeCounter + pro.Size;
                        //Debug.WriteLine(item.Name + " (" + SizeSuffix(Convert.ToInt64(pro.Size)) + ") // Total dumped: " + SizeSuffix(Convert.ToInt64(sizeCounter)));
                        //updateText(item.Path + " (Size: " + SizeSuffix(Convert.ToInt64(pro.Size)) + ")\n");
                        textBoxDebug.Text = "Dumping: " + item.Path;
                        byte[] array = await ReadFile(file);
                        await PostAsync(client, server + "netDump.php?filename=" + file.Path, array);
                    }
                    catch (Exception ex)
                    {
                        //Debug.WriteLine(item.Name + " failed to copy... (" + ex + ")");
                        //updateText(item.Path + " (FAiLED... " + ex + ")\n");
                        textBoxDebug.Text = "Failed: " + item.Path;
                        playSound("onError.wav", false);
                    }
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
            await PostAsync(client, server + "netDump.php?filename=" + source.Path, array);
        }

        //File copy function
        public async Task CopyFileAsync(StorageFile source, StorageFolder destination)
        {
            await source.CopyAsync(destination, source.Name + ".x", NameCollisionOption.ReplaceExisting);
        }

        //File copy function
        public async Task CopyPayloadAsync(StorageFile source, StorageFolder destination)
        {
            await source.CopyAsync(destination, source.Name, NameCollisionOption.ReplaceExisting);
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
                            //Debug.WriteLine("ERROR: " + ex.Message);
                        }
                    }
                }
                else if (item.IsOfType(StorageItemTypes.File))
                {
                    //Debug.WriteLine("Saving file: " + item.Path);
                    try
                    {
                        await Task.Run(() => File.Copy(item.Path, destinationFolder.Path + item.Name + ".x"));
                        StorageFile file = (StorageFile)item;
                        BasicProperties pro = await item.GetBasicPropertiesAsync();
                        sizeCounter = sizeCounter + pro.Size;
                        //Debug.WriteLine(item.Name + " (" + SizeSuffix(Convert.ToInt64(pro.Size)) + ") // Total dumped: " + SizeSuffix(Convert.ToInt64(sizeCounter)));
                        updateText(item.Path + " (Size: " + SizeSuffix(Convert.ToInt64(pro.Size)) + ")\n");
                    }
                    catch (Exception ex)
                    {
                        //Debug.WriteLine(item.Name + " failed to copy... (" + ex + ")");
                        playSound("onError.wav", false);
                    }

                    //await file.CopyAsync(destinationFolder, item.Name + ".x", NameCollisionOption.ReplaceExisting);

                    if (testWritePerm)
                    {
                        try
                        {
                            StorageFile file = (StorageFile)item;
                            await Windows.Storage.FileIO.AppendTextAsync(file, "0");
                            rwxStr.Append("FILE: " + file.Path + " TRUE");
                        }
                        catch (Exception ex)
                        {
                            //Debug.WriteLine("ERROR: " + ex.Message);
                        }
                    }
                }
                else
                {
                    updateText(item.Name + "is of unknown type!!! (maybe hardlink)\n");
                }
            }
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
                //Debug.WriteLine("Testing RWX for " + dir + ":");
                string[] files = Directory.GetFiles(dir);
                string[] dirs = Directory.GetDirectories(dir);
                if (files.Length > 0 || dirs.Length > 0)
                    validDir = true;

                if (validDir)
                {
                    //Debug.WriteLine("R is true!");
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
                            //Debug.WriteLine("W is true!");
                        }
                    }
                }

                //Debug.WriteLine("Result: R: " + r.ToString() + " W: " + w.ToString() + " X: " + w.ToString());
            }
            catch (Exception ex)
            {
                //Debug.WriteLine(ex.Message);
                updateText("ERROR: " + ex.Message + "\n");
            }
        }

        ///////////////////////////////////
        // B U T T O N S
        ///////////////////////////

        // DiRDUMP
        private async void dirdumpBTN_Click(object sender, RoutedEventArgs e)
        {
            playSound("onClick.wav", false);

            sizeCounter = 0;

            if (dumpSource.Text == "")
            {
                playSound("onError.wav", false);

                textBoxDebug.Text = "";
                updateText("* * * * * * * * ** * * * * * * * * * *\n");
                updateText("* * *  A T T E N T i O N  * * *\n");
                updateText("* * * * *  * * * * * * * * * * * * * *\n\n");
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
                textBoxDebug.Text = "";

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
            playSound("onClick.wav", false);

            sizeCounter = 0;

            updateText("");
            updateText("* * * * * * * * * * * * * * * * * * * * * *\n");
            updateText("* * *  D U M P i N G  G A M E  * * *\n");
            updateText("* * * * * * * * * * * * * * * * * * * * * *\n\n");

            updateText("S O U R C E :: BD-ROM\n");
            updateText("T A R G E T :: " + dumpDestination.Text.ToUpper() + "\n\n");

            updateText("* * * * * * * * * * * * * * * * * * * * * *\n\n");

            showDebugPanel();

            try
            {
                StorageFile catalog = await StorageFile.GetFileFromPathAsync("O:\\MSXC\\Metadata\\catalog.js");
                byte[] test = await ReadFile(catalog);
                string text = System.Text.Encoding.Unicode.GetString(test);
                var game = JsonConvert.DeserializeObject<dynamic>(text);

                string version = game.version;
                string title = game.packages[0].vui[0].title;
                string size = game.packages[0].size;

                updateText("GAME: " + title + " (v" + version + ")\n");
                updateText("SiZE: " + size + " bytes\n\n");

                string dumpSource = @"O:\MSXC\,O:\Licenses\";

                await Dump(dumpSource, "usb", "http://" + netServer.Text);

            }
            catch (Exception ex)
            {
                playSound("onError.wav", false);
                //Debug.WriteLine(ex);
            }
        }

        // x L O A D E R
        private async void xLoadBTN_Click(object sender, RoutedEventArgs e)
        {
            Int64 size_msvsmon_x64 = 1890216;
            Int64 size_msvsmon_x86 = 1364392;
            try
            {
                showDebugPanel();

                playSound("onSuccess.wav", false);

                textBoxDebug.Text = "";
                updateText("* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *\n");
                updateText("* * *                           x L O A D                            * * *\n");
                updateText("* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *\n\n");

                // i N i T

                // FiND USB
                StorageFolder xLoadUSB = null;

                var removableStorages = await KnownFolders.RemovableDevices.GetFoldersAsync();
                if (removableStorages.Count <= 0)
                {
                    updateText("xLOAD  >  P A Y L O A D  N O T  F O U N D !\n");
                    playSound("onError.wav", false);
                }
                else
                {
                    xLoadUSB = removableStorages[0];
                    updateText("xLOAD  >  P A Y L O A D  F O U N D  (" + removableStorages[0].Path + ")\n");
                    playSound("onSuccess.wav", false);
                }

                // x86MSVSMON

                StorageFolder remToolsRootDir = await StorageFolder.GetFolderFromPathAsync(@"D:\DevelopmentFiles\");
                StorageFile msvsmonX64 = await StorageFile.GetFileFromPathAsync(removableStorages[0].Path + @"xLOAD\msvsmon_x64.exe");

                // x64MSVSMON

                //StorageFolder remToolsX64Dir = await StorageFolder.GetFolderFromPathAsync(@"D:\DevelopmentFiles\VSRemoteTools\x64");
                //StorageFile msvsmonX64 = await StorageFile.GetFileFromPathAsync(@"E:\xLOAD\msvsmon_x64.exe");

                // 01 - DELETE VSREMOTETOOLS FROM D:\

                // Check if exists. Delte if exists..
                if (await remToolsRootDir.TryGetItemAsync(@"VSRemoteTools") != null)
                {
                    updateText("xLOAD  >  V S R T O O L S  F O U N D . . .  D E L E T i N G !\n");
                    Directory.Delete(@"D:\DevelopmentFiles\VSRemoteTools\", true);
                }
                else
                {
                    updateText("xLOAD  >  V S R T O O L S  N O T  F O U N D !\n");
                }

                // Check if has been deleted..
                if (await remToolsRootDir.TryGetItemAsync(@"VSRemoteTools") != null)
                {
                    updateText("xLOAD  >  V S R T O O L S  D E L E T i N G  F A i L E D !\n");
                }
                else
                {
                    updateText("xLOAD  >  V S R T O O L S  S U C C E S S F U L L Y  W i P E D !\n");
                }

                // 02 - GET xLOAD PAYMENT > BYTES

                StorageFile payload = await StorageFile.GetFileFromPathAsync(removableStorages[0].Path + @"xLOAD\xLoad.exe");
                byte[] payloadBytes = await ReadFile(payload);

                updateText("xLOAD  >  D R O P P E D  x L O A D  i N  R A M !\n");

                // 03 - WAIT FOR DEPLOYEMENT OF VSRTOOLS

                updateText("xLOAD  >  W A i T i N G  F O R  V S  D E P L O Y M E N T !\n");

                var isDeployed = false;

                while (isDeployed == false)
                {
                    StorageFile target = await StorageFile.GetFileFromPathAsync(@"D:\DevelopmentFiles\VSRemoteTools\x64\msvsmon.exe");
                    if (target != null)
                    {
                        // Deploy detected...

                        updateText("xLOAD  >  V S R T O O L S  D E P L O Y  D E T E C T E D !\n");
                        playSound("onSuccess.wav", false);

                        // Check/Compare Filesize...

                        updateText("xLOAD  >  Waiting for end of transfer");

                        BasicProperties targetPro = await target.GetBasicPropertiesAsync();
                        Int64 sizeTarget = Convert.ToInt64(targetPro.Size);

                        while (sizeTarget < size_msvsmon_x64)
                        {
                            targetPro = await target.GetBasicPropertiesAsync();
                            sizeTarget = Convert.ToInt64(targetPro.Size);
                            updateText(".");
                        }
                        updateText("\n");

                        // Final Size reached...

                        updateText("xLOAD  >  S i Z E  M A T C H E S !\n");

                        // 03 - COPY PAYLOADS TO VSRTOOLS DiR

                        StorageFolder remToolsX64Dir = await StorageFolder.GetFolderFromPathAsync(@"D:\DevelopmentFiles\VSRemoteTools\x64\");

                        try
                        {
                            await payload.CopyAsync(remToolsX64Dir, "msvsmon.exe", NameCollisionOption.ReplaceExisting);
                            //await CopyPayloadAsync(msvsmonXL, remToolsX86Dir);
                            await CopyPayloadAsync(msvsmonX64, remToolsX64Dir);

                            updateText("xLOAD  >  P A Y L O A D  D E P L O Y E D !\n");
                            playSound("onSuccess.wav", false);

                        }
                        catch (Exception)
                        {
                            updateText("xLOAD  >  D E P L O Y  F A i L E D !\n");
                            playSound("onError.wav", false);
                        }

                        isDeployed = true;
                    }
                    else
                    {
                        isDeployed = false;
                    }
                }

                // 04 - DONE?! CROSS FiNGERS

                updateText("xLOAD  >  D O N E .  i N i T i A L i Z i N G . . .\n\n");

                // Wait couple of seconds.. check for folder.

                await Task.Delay(5000);

                if (await remToolsRootDir.TryGetItemAsync(@"PAYLOAD_SUCCESS\") != null)
                {
                    updateText("! ! ! p X  P A Y L O A D  i N i T i A L i Z E D ! ! !\n");
                    updateText("! ! ! FOUND: " + remToolsRootDir + @"xLOAD_SUCCESS\");
                }

            }
            catch (Exception ex)
            {
                playSound("onError.wav", false);

                textBoxDebug.Text = "";

                updateText("* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *\n");
                updateText("* * *                           x L O A D                            * * *\n");
                updateText("* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *\n\n");

                updateText("xLOAD  >  E R R O R  (" + ex.Message + ")\n");

                showDebugPanel();
            }
        }

        // FiLEBROWSER
        private async void filebrowserBTN_Click(object sender, RoutedEventArgs e)
        {
            playSound("onClick.wav", false);
            showFilebrowserPanel();
        }

        // TOOLBOX
        private void toolboxBTN_Click(object sender, RoutedEventArgs e)
        {
            playSound("onClick.wav", false);
            showToolboxPanel();
        }

        // DEViCELiST
        private async void devicelistBTN_Click(object sender, RoutedEventArgs e)
        {
            playSound("onClick.wav", false);

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
                playSound("onError.wav", false);
                updateText("Unable to save file.\nNo USB Device found!");
            }
        }

        // USERLiST
        private async void userlistBTN_Click(object sender, RoutedEventArgs e)
        {
            playSound("onClick.wav", false);

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
                playSound("onError.wav", false);
                updateText("Unable to save file.\nNo USB Device found!");
            }

        }

        // TEST-RWX
        private void testRWXBTN_Click(object sender, RoutedEventArgs e)
        {
            playSound("onClick.wav", false);

            if (testRWXTarget.Text == "")
            {
                playSound("onError.wav", false);

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

        // dirList
        private async void dirListBTN_Click(object sender, RoutedEventArgs e)
        {
            textBoxDebug.Text = "";
            updateText("* * * * * * * * * * * * * * * * * * * * * * * * *\n");
            updateText("* * *  S E A R C H i N G  D i R * * *\n");
            updateText("* * * * * * * * * * * * * * * * * * * * * * * * *\n\n");

            showDebugPanel();

            try
            {
                StorageFolder appFolder = await StorageFolder.GetFolderFromPathAsync(dumpSource.Text);

                IReadOnlyList<StorageFolder> subfolders = await appFolder.GetFoldersAsync();

                updateText("ROOT: " + dumpSource.Text + "\n\n");

                foreach (StorageFolder folder in subfolders)
                {
                    //Debug.WriteLine(folder.Path);
                    updateText(folder.Path + "\n");

                    foreach (StorageFile file in await folder.GetFilesAsync())
                    {
                        //Debug.WriteLine(file.Path);
                        updateText(file.Path + "\n");
                    }
                }
            }
            catch (Exception ex)
            {
                playSound("onError.wav", false);
                //Debug.WriteLine(ex);
                updateText("ERROR: " + ex + "\n");
            }

        }

        // SETTiNGS
        private void settingsBTN_Click(object sender, RoutedEventArgs e)
        {
            playSound("onClick.wav", false);
            showSettingsPanel();
        }

        ///////////////////////////////////
        // A U D i O
        ///////////////////////////

        private async void playSound(string path, bool isLoop = false)
        {
            if (soundFX.IsChecked == true)
            {
                MediaElement mysong = new MediaElement();
                Windows.Storage.StorageFolder folder = await Windows.ApplicationModel.Package.Current.InstalledLocation.GetFolderAsync("Assets\\Audio");
                Windows.Storage.StorageFile file = await folder.GetFileAsync(path);
                var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
                mysong.SetSource(stream, file.ContentType);
                mysong.IsLooping = isLoop;
                mysong.Play();
            }
        }

        ///////////////////////////////////
        // T R A N S i T i O N S
        ///////////////////////////

        // BUTTONS
        private void buttonGotFocus(object sender, RoutedEventArgs e)
        {
            playSound("onFocus.wav", false);
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

        // PANELS
        private void showSplashPanel()
        {
            //playSound("onInitLoad.wav", false);
            ShowSplashPanel.Begin();
            ShowLogo.Begin();
            ShowMenu.Begin();
        }

        private void showDebugPanel()
        {
            if (splashPanel.Opacity == 1)
                HideSplashPanel.Begin();
            
            if (toolboxPanel.Opacity == 1)
                toolboxPanelFadeOut.Begin();

            if (filebrowserPanel.Opacity == 1)
                filebrowserPanelFadeOut.Begin();

            if (settingsPanel.Opacity == 1)
                settingsPanelFadeOut.Begin();

            if (debugPanel.Opacity == 0)

                Canvas.SetZIndex(debugPanel, 9999);
                Canvas.SetZIndex(filebrowserPanel, 0);
                Canvas.SetZIndex(toolboxPanel, 0);
                Canvas.SetZIndex(settingsPanel, 0);

                playSound("onTrans.wav", false);
                debugPanelFadeIn.Begin();
        }

        private void showFilebrowserPanel()
        {
            if (splashPanel.Opacity == 1)
                HideSplashPanel.Begin();

            if (debugPanel.Opacity == 1)
                debugPanelFadeOut.Begin();

            if (toolboxPanel.Opacity == 1)
                toolboxPanelFadeOut.Begin();

            if (settingsPanel.Opacity == 1)
                settingsPanelFadeOut.Begin();

            if (filebrowserPanel.Opacity == 0)

                Canvas.SetZIndex(debugPanel, 0);
                Canvas.SetZIndex(filebrowserPanel, 9999);
                Canvas.SetZIndex(toolboxPanel, 0);
                Canvas.SetZIndex(settingsPanel, 0);

                playSound("onTrans.wav", false);
                filebrowserPanelFadeIn.Begin();
        }

        private void showToolboxPanel()
        {
            if (splashPanel.Opacity == 1)
                HideSplashPanel.Begin();

            if (debugPanel.Opacity == 1)
                debugPanelFadeOut.Begin();

            if (filebrowserPanel.Opacity == 1)
                filebrowserPanelFadeOut.Begin();

            if (settingsPanel.Opacity == 1)
                settingsPanelFadeOut.Begin();

            if (toolboxPanel.Opacity == 0)

                Canvas.SetZIndex(debugPanel, 0);
                Canvas.SetZIndex(filebrowserPanel, 0);
                Canvas.SetZIndex(toolboxPanel, 9999);
                Canvas.SetZIndex(settingsPanel, 0);

                playSound("onTrans.wav", false);
                toolboxPanelFadeIn.Begin();
        }

        private void showSettingsPanel()
        {
            if (splashPanel.Opacity == 1)
                HideSplashPanel.Begin();

            if (debugPanel.Opacity == 1)
                debugPanelFadeOut.Begin();

            if (toolboxPanel.Opacity == 1)
                toolboxPanelFadeOut.Begin();

            if (filebrowserPanel.Opacity == 1)
                filebrowserPanelFadeOut.Begin();

            if (settingsPanel.Opacity == 0)

                Canvas.SetZIndex(debugPanel, 0);
                Canvas.SetZIndex(filebrowserPanel, 0);
                Canvas.SetZIndex(toolboxPanel, 0);
                Canvas.SetZIndex(settingsPanel, 9999);

                playSound("onTrans.wav", false);
                settingsPanelFadeIn.Begin();
        }
    }
}