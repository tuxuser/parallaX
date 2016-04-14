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

            // textBoxDebug.Text = "\nparallaX initialized... Waiting for your input!\n\n";
        }

        // TEXTBOX UPDATER
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

            if (splashPanel.Opacity == 1)
            {
                HideSplashPanel.Begin();
            }
            if (settingsPanel.Opacity == 1)
            {
                HideSettingsPanel.Begin();
            }
            if (debugPanel.Opacity == 0)
            {
                ShowDebugPanel.Begin();
            }

            textBoxDebug.Text = "";

            updateText("\n* * * * * * * * * * * * * * * *\n");
            updateText("* * *  D U M P i N G  * * *\n");
            updateText("* * * * * * * * * * * * * * * * *\n\n");
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
                        updateText("\n* * *  D U M P  F A I L E D  * * * \n");
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
                        updateText("\n* * *  D U M P  F A I L E D  * * * \n");
                        Debug.WriteLine("USB not found!");
                    }
                }
            }
            catch (Exception ex)
            {
                updateText("\n* * *  D U M P  F A I L E D  * * * \n"+ex.Message);
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
                //updateText(file.Name + " (" + SizeSuffix(Convert.ToInt64(pro.Size)) + ") // Total dumped: " + SizeSuffix(Convert.ToInt64(sizeCounter)) + "\n");
                updateText(file.Name + " (" + SizeSuffix(Convert.ToInt64(pro.Size)) + ")\n");
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
            }
        }

        // B U T T O N S

        // 01 - DiRDUMP
        private void Dump_Button_Click(object sender, RoutedEventArgs e)
        {
            //s for static :)
            if (inputFileDumpSource.Text == "s")
            {
                updateText("\nUsing hardcoded path...\n\n");
                Dump("N:\\BlackBox\\", inputFileDumpDestination.Text);
                Dump("U:\\ShellState\\", inputFileDumpDestination.Text);
                Dump("S:\\ProgramData\\Microsoft\\Windows\\AppRepository\\", inputFileDumpDestination.Text);
                Dump("S:\\programdata\\microsoft\\windows\\apprepository\\", inputFileDumpDestination.Text);

            }
            else
            {
                updateText("\nUsing custom path...\n\n");
                Dump(inputFileDumpSource.Text, inputFileDumpDestination.Text);
            }
        }

        // 02-DiSCDUMP
        private async void discdumpButton_Click(object sender, RoutedEventArgs e)
        {
            Dump("O:\\MSXC\\", "usb");
            Dump("O:\\Licenses\\", "usb");
        }

        // 03-SETTiNGS
        private void settingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (splashPanel.Opacity == 1)
            {
                HideSplashPanel.Begin();
            }
            if (debugPanel.Opacity == 1)
            {
                HideDebugPanel.Begin();
            }
            if (settingsPanel.Opacity == 0)
            {
                ShowSettingsPanel.Begin();
            }
        }
    }
}