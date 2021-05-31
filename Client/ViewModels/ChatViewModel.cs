﻿using Client.Models;
using Client.Utility;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Client.ViewModels
{
    class ChatViewModel : INotifyPropertyChanged
    {
        //private delegate void MessageSendType(string message);
        //private MessageSendType sendType;

        //private bool canWrite = true;
        public event PropertyChangedEventHandler PropertyChanged;

        private int _countLeft;

        private double _previousScrollOffset;

        private MainViewModel mainVM;
        private Chat chat;

        private MediaMessage curMediaMessage;
        //private MediaPlayer player;

        //private Timer timer;

        private string isWritingText;
        private string messageText = "";

        private bool? canNotify;
        private Visibility loaderVisibility;
        private Message inputMessage;

        public Emoji.Wpf.EmojiData.Group selectedEmojiGroup;

        public string searchEmojiText;
        public Emoji.Wpf.EmojiData.Emoji selectedEmoji;
        public ObservableCollection<Emoji.Wpf.EmojiData.Emoji> emojis;

        Views.EmojiWindow window;

        public ChatViewModel(MainViewModel mainVM)
        {
            this.mainVM = mainVM;
            Chat = mainVM.SelectedChat;
            Chat.ClientId = mainVM.Client.SqlId;

            Settings = Settings.Instance;
            CanNotify = Settings.GetMute(chat.SqlId);

            InputMessage = new TextMessage();
            chat.Messages.Clear();

            chat._messageCount = 15;
            chat._messageOffset = 0;
            chat._offsetDate = DateTime.Now;
            _countLeft = chat._messageCount;

            LoaderVisibility = Visibility.Hidden;

            InputMessage = new TextMessage();

            EmojiGroups = new ObservableCollection<Emoji.Wpf.EmojiData.Group>(Emoji.Wpf.EmojiData.AllGroups);

            //client = ClientUserInfo.getInstance();
            //ChatClient = chatClient;
            //sendType = SendText;

            //player = new MediaPlayer();
            //player.MediaEnded += MediaEnded;

            //timer = new Timer();
            //timer.Elapsed += MediaPosTimer;
            //timer.Interval = 500;

            TextBox_KeyDownCommand = new Command(TextBox_KeyDown);
            TextBox_KeyUpCommand = new Command(TextBox_KeyUp);
            TextBox_EnterPressedCommand = new Command(TextBox_EnterPressed);

            //MediaPlayCommand = new Command(MediaPlay);
            //MediaPosChangedCommand = new Command(MediaPosChanged);
            //SendCommand = new Command(Send);
            OpenFileCommand = new Command(OpenFile);
            CancelFileCommand = new Command(CancelFile);
            SendFileCommand = new Command(SendFile);

            OpenEmojiCommand = new Command(OpenEmoji);
            EmojiChangedCommand = new Command(EmojiChanged);
            EmojiGroupChangedCommand = new Command(EmojiGroupChanged);
            SearchEmojiTextChangedCommand = new Command(SearchEmojiTextChanged);
            FavEmojiCommand = new Command(FavEmoji);

            EditChatCommand = new Command(EditChat);
            LeaveChatCommand = new Command(LeaveChat);

            DownloadFileCommand = new Command(DownloadFile);

            CanNotifyChangedCommand = new Command(CanNotifyChanged);

            LoadCommand = new Command(Load);
            UnloadCommand = new Command(Unload);

            window = new Views.EmojiWindow();
            window.DataContext = this;
        }

        public ICommand TextBox_KeyDownCommand { get; }
        public ICommand TextBox_KeyUpCommand { get; }
        public ICommand TextBox_EnterPressedCommand { get; }

        //public ICommand MediaPlayCommand { get; }
        //public ICommand MediaPosChangedCommand { get; }
        //public ICommand SendCommand { get; }
        public ICommand OpenEmojiCommand { get; }
        public ICommand EmojiGroupChangedCommand { get; }
        public ICommand EmojiChangedCommand { get; }
        public ICommand SearchEmojiTextChangedCommand { get; }
        public ICommand FavEmojiCommand { get; }

        public ICommand EditChatCommand { get; }
        public ICommand LeaveChatCommand { get; }

        public ICommand CanNotifyChangedCommand { get; }

        public ICommand OpenFileCommand { get; }
        public ICommand SendFileCommand { get; }
        public ICommand CancelFileCommand { get; }

        public ICommand DownloadFileCommand { get; }

        public ICommand LoadCommand { get; }
        public ICommand UnloadCommand { get; }

        public Chat Chat { get => chat; set => Set(ref chat, value); }
        public Settings Settings { get; }

        public string IsWritingText { get => isWritingText; set => Set(ref isWritingText, value); }
        public Message InputMessage { get => inputMessage; set => Set(ref inputMessage, value); }
        public string MessageText { get => messageText; set => Set(ref messageText, value); }
        public bool? CanNotify { get => canNotify; set => Set(ref canNotify, value); }

        public Emoji.Wpf.EmojiData.Group SelectedEmojiGroup { get => selectedEmojiGroup; set => Set(ref selectedEmojiGroup, value); }
        public ObservableCollection<Emoji.Wpf.EmojiData.Group> EmojiGroups { get; }

        public string SearchEmojiText { get => searchEmojiText; set => Set(ref searchEmojiText, value); }
        public Emoji.Wpf.EmojiData.Emoji SelectedEmoji { get => selectedEmoji; set => Set(ref selectedEmoji, value); }
        public ObservableCollection<Emoji.Wpf.EmojiData.Emoji> Emojis { get => emojis; set => Set(ref emojis, value); }

        public Visibility LoaderVisibility { get => loaderVisibility; set => Set(ref loaderVisibility, value); }
        public System.Windows.Controls.ScrollViewer Scroll { get; set; }


        public void SetScrollViewer(ref System.Windows.Controls.ScrollViewer scroll)
        {
            Scroll = scroll;
            Scroll.ScrollChanged += ScrollPositionChanged;
            Scroll.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
        }

        private async void ScrollPositionChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
        {
            if (Scroll.VerticalOffset == 0 && _previousScrollOffset != Scroll.VerticalOffset)
            {
                _countLeft = chat._messageCount;
                _previousScrollOffset = Scroll.VerticalOffset;
                LoaderVisibility = Visibility.Visible;
                Scroll.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled;
                await LoadMore();
                Scroll.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
                LoaderVisibility = Visibility.Hidden;
            }
            else
                _previousScrollOffset = Scroll.VerticalOffset;
        }

        private async void Load(object obj)
        {
            await LoadMore();
            //Scroll.ScrollToEnd();
        }

        private async Task LoadMore()
        {
            await Utility.MessageLoader.LoadMessage(this.Chat, mainVM.Client.SqlId, 30, 30);
        }

        private async void TextBox_EnterPressed(object obj)        
        {
            if (String.IsNullOrEmpty(messageText)) return;
            await mainVM.ChatClient.SendMessageTextAsync(new ChatService.ServiceMessageText() { Text = MessageText, UserId = mainVM.Client.SqlId }, Chat.SqlId);
            Chat.Messages.Add(new UserMessage(new TextMessage(messageText, DateTime.Now)));
            Chat.LastMessage = new TextMessage(messageText, DateTime.Now);
            MessageText = null;
            if (mainVM.Chats.IndexOf(chat) != 0)
            {
                mainVM.Chats.Move(mainVM.Chats.IndexOf(chat), 0);
            }
            //Scroll.ScrollToBottom();
        }

        private void TextBox_KeyUp(object obj)
        {
            if (String.IsNullOrEmpty(messageText))
                mainVM.ChatClient.MessageIsWritingAsync(Chat.SqlId, null);
        }

        private void TextBox_KeyDown(object obj)
        {
            if (String.IsNullOrEmpty(messageText))
                mainVM.ChatClient.MessageIsWritingAsync(Chat.SqlId, mainVM.Client.SqlId);
        }

        //private void MediaEnded(object sender, EventArgs e)
        //{
        //    player.Close();
        //    timer.Stop();
        //    curMediaMessage.CurrentLength = 0;
        //    curMediaMessage.IsPlaying = false;
        //}

        //private void MediaPosTimer(object sender, EventArgs e)
        //{
        //    App.Current.Dispatcher.Invoke(() =>
        //    {
        //        curMediaMessage.CurrentLength = player.Position.Ticks;
        //    });
        //}

        private void CanNotifyChanged(object param)
        {
            Settings.AddMute(chat.SqlId, canNotify);
        }

        private void EditChat(object param)
        {
            Views.EditGroupWindow window = new Views.EditGroupWindow();
            window.DataContext = new EditGroupViewModel(mainVM);
            window.ShowDialog();
        }

        public void LeaveChat(object param)
        {
            if (Chat != null)
            {
                chat.Messages.Add(SystemMessage.UserLeftChat(DateTime.Now, mainVM.Client.UserName));
                mainVM.ChatClient.LeaveFromChatroom(mainVM.Client.SqlId, Chat.SqlId);
                Chat.CanWrite = false;
                chat.Messages.Clear();
                mainVM.Chats.Remove(chat);
                mainVM.SelectedChat = null;
                Settings.RemoveMute(chat.SqlId);
            }
        }

        public async void DownloadFile(object param)
        {
            FileMessage message = (FileMessage)param;

            SaveFileDialog saveFileDialog = new SaveFileDialog();
            string extension = message.FileName.Substring(message.FileName.LastIndexOf('.'));
            saveFileDialog.Filter = $"(*{extension}*)|*{extension}*";
            saveFileDialog.FileName = message.FileName;
            if (saveFileDialog.ShowDialog() != true)
                return;

            string filename = saveFileDialog.FileName;
            if (message is ImageMessage)
            {
                BitmapImage imageMessage = (message as ImageMessage).Bitmap;

                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(imageMessage));

                using (var filestream = new FileStream(filename, System.IO.FileMode.Create))
                {
                    encoder.Save(filestream);
                }
            }

            Stream stream = null;
            MemoryStream memoryStream = null;
            FileStream fileStream = null;
            long lenght = 0;

            ChatService.FileClient fileClient = new ChatService.FileClient();
            try
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    string name = fileClient.FileDownload(message.StreamId, out lenght, out stream);
                    if (lenght <= 0)
                        return;
                    memoryStream = FileHelper.ReadFileByPart(stream);

                    fileStream = new FileStream(filename, FileMode.Create);
                    memoryStream.CopyTo(fileStream);
                });
            }
            catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message); }
            finally
            {
                if (fileStream != null) fileStream.Close();
                if (memoryStream != null) memoryStream.Close();
                if (stream != null) stream.Close();
            }
        }

        //private void MediaPlay(object param)
        //{
        //    MediaMessage message = (MediaMessage)((SourceMessage)param).Message;
        //    if (player.Source == null)
        //    {
        //        curMediaMessage = message;
        //        player.Open(new Uri(message.FileName, UriKind.Absolute));
        //        player.Position = TimeSpan.FromTicks(message.CurrentLength);
        //        player.Play();
        //        timer.Start();
        //        curMediaMessage.IsPlaying = true;
        //    }
        //    else if (message != curMediaMessage)
        //    {
        //        curMediaMessage.IsPlaying = false;
        //        curMediaMessage.CurrentLength = 0;
        //        player.Close();
        //        player.Open(new Uri(message.FileName, UriKind.Absolute));
        //        player.Position = TimeSpan.FromTicks(message.CurrentLength);
        //        player.Play();
        //        timer.Start();
        //        curMediaMessage = message;
        //        curMediaMessage.IsPlaying = true;
        //    }
        //    else if (curMediaMessage.IsPlaying)
        //    {
        //        player.Pause();
        //        timer.Stop();
        //        curMediaMessage.IsPlaying = false;
        //    }
        //    else
        //    {
        //        player.Play();
        //        timer.Start();
        //        curMediaMessage.IsPlaying = true;
        //    }
        //}

        private void OpenFile(object param)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "Text files (*.txt, *.docx, *.doc)|*.txt;*.docx;*.doc"
                 + "|Image files (*.jpg, *.jpeg, *.png)|*.jpg;*.jpeg;*.png"
                 + "|Presentation files (*.pptx)|*.pptx"
                 + "|Audio files (*.mp3, *.vawe)|*.mp3;*.vawe"
                 + "|Zip files (*.zip)|*.zip";
            if (dialog.ShowDialog() == true)
            {
                IsWritingText = dialog.FileName;
                InputMessage = new FileMessage(dialog.FileName);
            }
        }

        private void SendFile(object param)
        {
            FileMessage message = (FileMessage)inputMessage;

            if (message.FileName.Length > 0)
            {
                Chat.Messages.Add(new SessionSendedMessage(message));
                Chat.LastMessage = new FileMessage(message.FileName, DateTime.Now);

                if (mainVM.Chats.IndexOf(chat) != 0)
                {
                    mainVM.Chats.Move(mainVM.Chats.IndexOf(chat), 0);
                }

                Scroll.ScrollToBottom();
            }

            InputMessage = new TextMessage();
        }

        private void CancelFile(object param)
        {
            InputMessage = new TextMessage();
        }

        private void OpenEmoji(object param)
        {
            if (!window.IsActive)
            {
                window.Show();
            }
        }

        private void EmojiGroupChanged(object param)
        {
            if (SelectedEmojiGroup != null)
            {
                foreach (var item in Emoji.Wpf.EmojiData.AllGroups)
                {
                    if (item.Icon == SelectedEmojiGroup.Icon)
                    {
                        Emojis = new ObservableCollection<Emoji.Wpf.EmojiData.Emoji>(item.EmojiList);
                        break;
                    }
                }
            }
        }

        private void EmojiChanged(object param)
        {
            if (SelectedEmoji != null)
            {
                if (inputMessage is TextMessage)
                {
                    MessageText += selectedEmoji.Text;
                }
            }
        }

        private void SearchEmojiTextChanged(object param)
        {
            if (!String.IsNullOrWhiteSpace(SearchEmojiText))
            {
                Emojis = new ObservableCollection<Emoji.Wpf.EmojiData.Emoji>();
                Task.Run(() =>
                {
                    foreach (var group in Emoji.Wpf.EmojiData.AllGroups)
                    {
                        foreach (var emoji in group.EmojiList)
                        {
                            if (StringExtensions.ContainsAtStart(emoji.Name, searchEmojiText))
                            {
                                App.Current.Dispatcher.Invoke(() =>
                                {
                                    Emojis.Add(emoji);
                                });
                            }
                        }
                    }
                });
            }
            else Emojis.Clear();
        }

        private void FavEmoji(object param)
        {

        }

        private async void ShowMore(object param)
        {
            await LoadMore();
        }

        //private void MediaPosChanged(object param)
        //{
        //    MediaMessage message = (MediaMessage)((SourceMessage)param).Message;
        //    if (message == curMediaMessage)
        //        player.Position = TimeSpan.FromTicks(message.CurrentLength);
        //}

        private void Unload(object param)
        {
            if (Chat != null)
            {
                mainVM.ChatClient.MessageIsWriting(Chat.SqlId, null);
                if (curMediaMessage != null)
                {
                    curMediaMessage.IsPlaying = false;
                    curMediaMessage.CurrentLength = 0;
                    curMediaMessage = null;
                    //player.Close();
                }
                chat.Messages.Clear();
                chat._messageOffset = 0;
                chat._offsetDate = DateTime.Now;
            }
        }

        public void Set<T>(ref T prop, T value, [System.Runtime.CompilerServices.CallerMemberName] string prop_name = "")
        {
            prop = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop_name));
        }
    }
}