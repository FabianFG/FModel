﻿using FModel.Logger;
using FModel.Utils;
using FModel.ViewModels.StatusBar;
using FModel.Windows.CustomNotifier;
using FModel.Windows.DarkMessageBox;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;
using PakReader.Pak;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FModel.ViewModels.MenuItem
{
    public class BackupMenuItemViewModel : PropertyChangedBase
    {
        private string _header;
        private bool _isCheckable;
        private bool _isChecked;
        private bool _isEnabled = true;
        private bool _staysOpenOnClick = false;
        private string _downloadUrl;
        private double _size;
        private string _inputGestureText;
        private Image _icon;
        private ObservableCollection<PakMenuItemViewModel> _childrens;
        public string Header
        {
            get { return _header; }

            set { this.SetProperty(ref this._header, value); }
        }
        public bool IsCheckable
        {
            get { return _isCheckable; }

            set { this.SetProperty(ref this._isCheckable, value); }
        }
        public bool IsChecked
        {
            get { return _isChecked; }

            set { this.SetProperty(ref this._isChecked, value); }
        }
        public bool IsEnabled
        {
            get { return _isEnabled; }

            set { this.SetProperty(ref this._isEnabled, value); }
        }
        public bool StaysOpenOnClick
        {
            get { return _staysOpenOnClick; }

            set { this.SetProperty(ref this._staysOpenOnClick, value); }
        }
        public string DownloadUrl
        {
            get { return _downloadUrl; }

            set { this.SetProperty(ref this._downloadUrl, value); }
        }
        public double Size
        {
            get { return _size; }

            set { this.SetProperty(ref this._size, value); }
        }
        public string InputGestureText
        {
            get { return Size > 0 ? Strings.GetReadableSize(Size) : _inputGestureText; }

            set { this.SetProperty(ref this._inputGestureText, value); }
        }
        public Image Icon
        {
            get { return _icon; }

            set { this.SetProperty(ref this._icon, value); }
        }
        public ObservableCollection<PakMenuItemViewModel> Childrens
        {
            get { return _childrens; }

            set { this.SetProperty(ref this._childrens, value); }
        }
        public ICommand Command
        {
            get
            {
                return BackupCanExecute()
                    ? new CommandHandler(async() => await Backup().ConfigureAwait(false), () => true)
                    : new CommandHandler(Download, DownloadCanExecute);
            }

            private set { }
        }

        private async void Download()
        {
            if (DarkMessageBoxHelper.ShowOKCancel(string.Format(Properties.Resources.AboutToDownload, Header, InputGestureText), Properties.Resources.Warning, Properties.Resources.OK, Properties.Resources.Cancel) == MessageBoxResult.OK)
            {
                Stopwatch downloadTimer = Stopwatch.StartNew();
                StatusBarVm.statusBarViewModel.Set($"{Properties.Resources.Downloading} {Header}", Properties.Resources.Waiting);

                string path = $"{Properties.Settings.Default.OutputPath}//Backups//{Header}";
                using var client = new HttpClientDownloadWithProgress(DownloadUrl, path);
                client.ProgressChanged += (totalFileSize, totalBytesDownloaded, progressPercentage) =>
                {
                    StatusBarVm.statusBarViewModel.Set($"{Properties.Resources.Downloading} {Header}   🠞   {progressPercentage}%", Properties.Resources.Waiting);
                };

                await client.StartDownload().ConfigureAwait(false);

                downloadTimer.Stop();
                if (new FileInfo(path).Length > 0)
                {
                    DebugHelper.WriteLine("{0} {1} {2}", "[FModel]", "[CDN]", $"Downloaded {Header} in {downloadTimer.ElapsedMilliseconds} ms");
                    StatusBarVm.statusBarViewModel.Set(string.Format(Properties.Resources.DownloadSuccess, Header), Properties.Resources.Success);
                    Globals.gNotifier.ShowCustomMessage(Properties.Resources.Success, string.Format(Properties.Resources.DownloadSuccess, Header), "/FModel;component/Resources/check-circle.ico");
                }
                else
                {
                    File.Delete(path);
                    DebugHelper.WriteLine("{0} {1} {2}", "[FModel]", "[CDN]", $"Error while downloading {Header}, spent {downloadTimer.ElapsedMilliseconds} ms");
                    StatusBarVm.statusBarViewModel.Set(string.Format(Properties.Resources.DownloadError, Header), Properties.Resources.Error);
                    Globals.gNotifier.ShowCustomMessage(Properties.Resources.Error, string.Format(Properties.Resources.DownloadError, Header), "/FModel;component/Resources/alert.ico");
                }
            }
        }
        private bool DownloadCanExecute() => !Header.Equals(Properties.Resources.BackupPaks);

        private static readonly string _backupFileName = Folders.GetGameName() + "_" + DateTime.Now.ToString("MMddyyyy") + ".fbkp";
        private static readonly string _backupFilePath =  Properties.Settings.Default.OutputPath + "\\Backups\\" + _backupFileName;
        private async Task Backup()
        {
            StatusBarVm.statusBarViewModel.Reset();
            await Task.Run(() =>
            {
                DebugHelper.WriteLine("{0} {1} {2} {3}", "[FModel]", "[BackupMenuItemViewModel]", "[Create]", $"{_backupFileName} is about to be created");
                StatusBarVm.statusBarViewModel.Set($"{Properties.Settings.Default.PakPath}", Properties.Resources.Loading);

                using FileStream fileStream = new FileStream(_backupFilePath, FileMode.Create);
                using LZ4EncoderStream compressionStream = LZ4Stream.Encode(fileStream, LZ4Level.L00_FAST);
                using BinaryWriter writer = new BinaryWriter(compressionStream);
                foreach (PakFileReader pakFile in MenuItems.pakFiles.GetPakFileReaders())
                {
                    if (pakFile.AesKey == null)
                        continue;

                    if (!Globals.CachedPakFiles.ContainsKey(pakFile.FileName))
                    {
                        pakFile.ReadIndex(pakFile.AesKey);
                        Globals.CachedPakFiles[pakFile.FileName] = pakFile;
                        StatusBarVm.statusBarViewModel.Set(string.Format(Properties.Resources.MountedPakTo, pakFile.FileName, pakFile.MountPoint), Properties.Resources.Loading);
                    }
                    
                    foreach (var entry in pakFile)
                    {
                        // uasset or umap or idk
                        writer.Write(entry.Value.Offset);
                        writer.Write(entry.Value.Size);
                        writer.Write(entry.Value.UncompressedSize);
                        writer.Write(entry.Value.Encrypted);
                        writer.Write(entry.Value.StructSize);
                        writer.Write(pakFile.MountPoint + entry.Value.Name);
                        writer.Write(entry.Value.CompressionMethodIndex);

                        // uexp
                        if (entry.Value.Uexp != null)
                        {
                            writer.Write(entry.Value.Uexp.Offset);
                            writer.Write(entry.Value.Uexp.Size);
                            writer.Write(entry.Value.Uexp.UncompressedSize);
                            writer.Write(entry.Value.Uexp.Encrypted);
                            writer.Write(entry.Value.Uexp.StructSize);
                            writer.Write(pakFile.MountPoint + entry.Value.Uexp.Name);
                            writer.Write(entry.Value.Uexp.CompressionMethodIndex);
                        }
                        // ubulk
                        if (entry.Value.Ubulk != null)
                        {
                            writer.Write(entry.Value.Ubulk.Offset);
                            writer.Write(entry.Value.Ubulk.Size);
                            writer.Write(entry.Value.Ubulk.UncompressedSize);
                            writer.Write(entry.Value.Ubulk.Encrypted);
                            writer.Write(entry.Value.Ubulk.StructSize);
                            writer.Write(pakFile.MountPoint + entry.Value.Ubulk.Name);
                            writer.Write(entry.Value.Ubulk.CompressionMethodIndex);
                        }
                    }
                }
            }).ContinueWith(t =>
            {
                if (t.Exception != null) Tasks.TaskCompleted(t.Exception);
                else if (new FileInfo(_backupFilePath).Length > 0)
                {
                    DebugHelper.WriteLine("{0} {1} {2} {3}", "[FModel]", "[BackupMenuItemViewModel]", "[Create]", $"{_backupFileName} successfully created");
                    StatusBarVm.statusBarViewModel.Set(string.Format(Properties.Resources.CreateSuccess, _backupFileName), Properties.Resources.Success);
                    Globals.gNotifier.ShowCustomMessage(Properties.Resources.Success, string.Format(Properties.Resources.CreateSuccess, _backupFileName), "/FModel;component/Resources/check-circle.ico");
                }
                else
                {
                    File.Delete(_backupFilePath);
                    DebugHelper.WriteLine("{0} {1} {2} {3}", "[FModel]", "[BackupMenuItemViewModel]", "[Create]", $"{_backupFileName} is empty, hence deleted");
                    StatusBarVm.statusBarViewModel.Set(string.Format(Properties.Resources.CreateError, _backupFileName), Properties.Resources.Error);
                    Globals.gNotifier.ShowCustomMessage(Properties.Resources.Error, string.Format(Properties.Resources.CreateError, _backupFileName), "/FModel;component/Resources/alert.ico");
                }
            },
            TaskScheduler.FromCurrentSynchronizationContext());
        }
        private bool BackupCanExecute() => Header.Equals(Properties.Resources.BackupPaks);
    }
}
