﻿using Client.ChatService;
using Client.Models;
using Client.Utility;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Client.ViewModels
{
    public class MainViewModel : System.ComponentModel.INotifyPropertyChanged, ChatCallback
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        public delegate void ChatDelegate(Models.Chat chat);

        private ClientUserInfo clientUserInfo;
        private ChatClient ChatClient { set; get; } // Сеанс

        private ObservableCollection<Models.Chat> chats;
        private Models.Chat selectedChat;

        private ContentControl currentView;

        public MainViewModel()
        {
            LoadedWindowCommand = new Command(LoadedWindow);
            ClosedWindowCommand = new Command(ClosedWindow);
            SelectedChatChangedCommand = new Command(SelectedChatChanged);
            AddUserCommand = new Command(AddUser);
            CreateGroupCommand = new Command(CreateGroup);
            SettingsCommand = new Command(Settings);
            ChangeAvatarCommand = new Command(ChangeAvatar);

            Chats = new ObservableCollection<Models.Chat>();

            //SelectedHambugerOptionItemCommand = new Command(SelectedHambugerOptionItem);
        }

        private async void ChangeAvatar(object obj)
        {
            MessageBox.Show("!!!");
            UploadFileInfo uploadFileInfo = null;

            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Multiselect = false;
                openFileDialog.Filter = "Image files (*.jpg, *.jpeg, *.png)|*.jpg; *.jpeg; *.png";
                if (openFileDialog.ShowDialog() == true)
                {

                    MemoryStream memoryStream = Utility.ImageCropper.GetCroppedImage(openFileDialog.FileName);
                    memoryStream.Position = 0;

                    uploadFileInfo = new ChatService.UploadFileInfo { FileName = openFileDialog.FileName, FileStream = memoryStream, Responsed_SqlId = clientUserInfo.SqlId };
                    ChatService.AvatarClient avatarClient = new ChatService.AvatarClient();

                    if (uploadFileInfo.FileStream.CanRead)
                        await avatarClient.UserAvatarUploadAsync(uploadFileInfo.FileName, uploadFileInfo.Responsed_SqlId, uploadFileInfo.FileStream);

                    memoryStream.Position = 0;

                    var bitmap = new BitmapImage();

                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = memoryStream;
                    bitmap.EndInit();

                    clientUserInfo.UserImage = bitmap;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message);
            }
            finally
            {
                if (uploadFileInfo != null)
                {
                    if (uploadFileInfo.FileStream != null)
                        uploadFileInfo.FileStream.Dispose();
                }
            }
        }

        public MainViewModel(string name, int sqlId) : this()
        {
            ChatClient = new ChatClient(new InstanceContext(this));
            ClientUserInfo = new ClientUserInfo(sqlId, name);

            ChatClient.Connect(sqlId, name);
            //Demo();
        }

        public ICommand LoadedWindowCommand { get; }
        public ICommand ClosedWindowCommand { get; }
        //public ICommand SelectedHambugerOptionItemCommand { get; }
        public ICommand SelectedChatChangedCommand { get; }
        public ICommand AddUserCommand { get; }
        public ICommand CreateGroupCommand { get; }
        public ICommand SettingsCommand { get; }

        public ICommand ChangeAvatarCommand { get; }

        public ClientUserInfo ClientUserInfo { get => clientUserInfo; set => Set(ref clientUserInfo, value); } // Вся информация о подключенном юзере

        public ObservableCollection<Models.Chat> Chats { get => chats; set => Set(ref chats, value); }
        public Models.Chat SelectedChat { get => selectedChat; set => Set(ref selectedChat, value); }

        public ContentControl CurrentView { get => currentView; set => Set(ref currentView, value); }

        // После того как окно полностью прогрузилось, у нас происходит вызов загрузки аватарки с сервера к пользователю
        public void LoadedWindow(object sender)
        {
            try
            {
                clientUserInfo.DownloadAvatarAsync();
                LoadChatroomsAsync();
            }
            catch (FaultException<ConnectionExceptionFault> ex)
            {
                MessageBox.Show(ex.Message);
            }
            catch (IOException)
            {
                MessageBox.Show("avatar image could not be download");
            }
        }

        public void SelectedChatChanged(object param)
        {
            if (selectedChat != null)
            {
                Views.ChatUC chatView = new Views.ChatUC();
                ChatViewModel viewModel = new ChatViewModel(ChatClient, this);
                chatView.getControl = new Views.ChatUC.GetControlDelegate(viewModel.SetScrollViewer);
                chatView.DataContext = viewModel;
                CurrentView = chatView;
            }
        }

        private void AddUser(object param)
        {
            UserControl control = new Views.AddUserUC();
            AddUserViewModel viewModel = new AddUserViewModel(ChatClient);
            viewModel.AddChat += AddChatToChats;
            control.DataContext = viewModel;
            CurrentView = control;
        }

        private void CreateGroup(object param)
        {
            UserControl control = new Views.CreateGroupUC();
            CreateGroupViewModel viewModel = new CreateGroupViewModel(ChatClient);
            viewModel.AddChat += AddChatToChats;
            control.DataContext = viewModel;
            CurrentView = control;
        }

        private void Settings(object param)
        {
            UserControl control = new Views.UCSettings();
            control.DataContext = new SettingsViewModel();
            CurrentView = control;
        }

        private void UserLeftFromChatroom(int chatId, int userId)
        {
            var chat = FindChatroom(chatId);
            chat.UserLeavedChatroom(userId);
        }

        private void UserOnlineState(int userId, bool state)
        {
            foreach (var chat in chats)
            {
                if (chat.SetOnlineState(userId, state))
                {
                    chat.UserIsWriting = "";
                }
            }
        }

        private void UserAddedToChat(int chatId, int userId)
        {

        }

        private void UserRemovedFromChat(int chatId, int userId)
        {
            if (userId == clientUserInfo.SqlId)
                return;
            ChatGroup chat = FindChatroom(chatId) as ChatGroup;
            chat.RemoveUser(chat.FindUser(userId));
        }

        private Models.Chat FindChatroom(int chatId)
        {
            foreach (var chat in chats)
                if (chat.SqlId == chatId)
                    return chat;
            return null;
        }

        private void AddChatToChats(Models.Chat chat)
        {
            Chats.Add(chat);
        }

        //private void RemoveChatFromChats(Models.Chat chat)
        //{
        //    Chats.Remove(chat);
        //}

        private void ChatIsWriting(int chatId, int? userId)
        {
            var chat = FindChatroom(chatId);
            chat.MessageIsWriting(userId);
        }

        public void NotifyUserIsOnline(int sqlUserId)
        {
            UserOnlineState(sqlUserId, true);
        }

        public void NotifyUserIsOffline(int sqlUserId)
        {
            UserOnlineState(sqlUserId, false);
        }

        public void NotifyUserIsAddedToChat(int chatId, string chatName, ChatService.UserInChat[] usersInChat)
        {
            Task.Run(() => 
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    List<AvailableUser> availableUsers = new List<AvailableUser>();
                    foreach(var user in usersInChat)
                    {
                        if(user.UserSqlId != clientUserInfo.SqlId)
                         availableUsers.Add(new AvailableUser(user.UserName, user.UserSqlId) { IsOnline = user.IsOnline });
                    }

                    Chats.Add(availableUsers.Count > 1 ? new ChatGroup(chatId, chatName, availableUsers) { CanWrite = true }
                                                   : (Models.Chat)new ChatOne(chatId, availableUsers.First()) { CanWrite = true });
                });
            });

            ChatClient.AddedUserToChatIsOnline(this.clientUserInfo.SqlId, chatId);
        }

        public void NotifyUserIsRemovedFromChat(int userId, int chatId)
        {
            UserRemovedFromChat(chatId, userId);
        }

        public void UserJoinedToChatroom(int userId)
        {
            //throw new NotImplementedException();
        }

        public void UserLeftChatroom(int chatId, int userId)
        {
            UserLeftFromChatroom(chatId, userId);
        }

        public void ReplyMessage(ServiceMessageText message, int chatroomId)
        {
            var chat = FindChatroom(chatroomId);
            if (chat != null)
            {
                chat.Messages.Add(chat.GetMessageType(message.UserId, new TextMessage(message.Text, message.DateTime)));
            }
        }

        public void ReplyMessageIsWriting(Nullable<int> userSqlId, int chatSqlId)
        {
            ChatIsWriting(chatSqlId, userSqlId);
        }

        public void NotifyUserSendedFileToChat(ServiceMessageFile serviceMessageFile, int chatroomId)
        {
            var chat = FindChatroom(chatroomId);
            if (chat != null)
                chat.Messages.Add(chat.GetMessageType(serviceMessageFile.UserId, new FileMessage(serviceMessageFile.FileName, serviceMessageFile.DateTime, serviceMessageFile.StreamId)));
        }

        public void ClosedWindow(object sender)
        {
            //ChatClient.Disconnect();
        }

        public void Set<T>(ref T prop, T value, [System.Runtime.CompilerServices.CallerMemberName] string prop_name = "")
        {
            prop = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(prop_name));
        }

        private async void LoadChatroomsAsync()
        {
            Dictionary<Chatroom, UserInChat[]> chatrooms = await ChatClient.FindAllChatroomsForClientAsync(clientUserInfo.SqlId);
            Chats = new ObservableCollection<Models.Chat>
                (await System.Threading.Tasks.Task<List<Models.Chat>>.Run(() =>
            {
                List<Models.Chat> clientChatrooms = new List<Models.Chat>();
                foreach (Chatroom key in chatrooms.Keys)
                {
                    bool canWrite = false;
                    if (chatrooms[key].Length > 1)
                    {
                        List<AvailableUser> availableUsers = new List<AvailableUser>();
                        foreach (UserInChat userInChat in chatrooms[key])
                        {
                            if (userInChat.UserSqlId == clientUserInfo.SqlId)
                            {
                                canWrite = true;
                                continue;
                            }
                            availableUsers.Add(new AvailableUser { SqlId = userInChat.UserSqlId, Name = userInChat.UserName, IsOnline = userInChat.IsOnline });
                        }

                        clientChatrooms.Add(availableUsers.Count > 1 ? new ChatGroup(key.ChatSqlId, key.ChatName, availableUsers) { CanWrite = canWrite }
                                                                     : (Models.Chat)new ChatOne(key.ChatSqlId, availableUsers.First()) { CanWrite = canWrite });
                    }
                    else
                    {
                        clientChatrooms.Add(new ChatOne(key.ChatSqlId) { CanWrite = canWrite });
                    }
                }
                return clientChatrooms;
            }));

        }

        public void NotifyUserChangedAvatar(int userId)
        {
            foreach(Models.Chat chat in Chats)
            {
                AvailableUser user = chat.FindUser(userId);
                if (user != null)
                {
                    chat.DownloadAvatarAsync(user.SqlId);
                }
            }
        }

        public void NotifyСhatroomAvatarIsChanged(int chatId)
        {
            throw new NotImplementedException();
        }

        //private void Demo()
        //{
        //    ObservableCollection<SourceMessage> Messages = new ObservableCollection<SourceMessage>();

        //    Messages.Add(new SystemMessage(new TextMessage("User type messages")));
        //    Messages.Add(new UserMessage(new TextMessage("hello group asd asfas fasf asfas fasf asfpKJAOPSKfa")));
        //    Messages.Add(new UserMessage(new FileMessage("files/hall.png") { IsLoaded = false }));
        //    Messages.Add(new UserMessage(new MediaMessage("files/control.mp3")));
        //    Messages.Add(new UserMessage(new FileMessage("files/1.txt") { IsLoaded = true }));
        //    Messages.Add(new UserMessage(new FileMessage("files/1.txt") { IsLoaded = false }));

        //    Messages.Add(new SystemMessage(new TextMessage("Friend type messages")));
        //    Messages.Add(new SourceMessage(new TextMessage("hello group asd asfas fasf asfas fasf asfpKJAOPSKfa")));
        //    Messages.Add(new SourceMessage(new FileMessage("files/hall.png") { IsLoaded = false }));
        //    Messages.Add(new SourceMessage(new MediaMessage("files/control.mp3")));
        //    Messages.Add(new SourceMessage(new FileMessage("files/1.txt") { IsLoaded = true }));
        //    Messages.Add(new SourceMessage(new FileMessage("files/1.txt") { IsLoaded = false }));

        //    Messages.Add(new SystemMessage(new TextMessage("Group type messages")));
        //    Messages.Add(new GroupMessage(new TextMessage("hello group asd asfas fasf asfas fasf asfpKJAOPSKfa"), new AvailableUser() { Name = "eldar" }));
        //    Messages.Add(new GroupMessage(new FileMessage("files/hall.png") { IsLoaded = false }, new AvailableUser() { Name = "Tamerlan" }));
        //    Messages.Add(new GroupMessage(new MediaMessage("files/control.mp3"), new AvailableUser() { Name = "eldar" }));
        //    Messages.Add(new GroupMessage(new FileMessage("files/1.txt") { IsLoaded = true }, new AvailableUser() { Name = "Eldar" }));
        //    Messages.Add(new GroupMessage(new FileMessage("files/1.txt") { IsLoaded = false }, new AvailableUser() { Name = "Tamerlan" }));

        //    //Chats.Add(new ChatOne() { User = new AvailableUser() { Name = "eldar" }, Messages = Messages });
        //    //Chats.Add(new ChatGroup() { GroupName = "group1", Messages = Messages });

        //    BitmapImage image = new BitmapImage(new Uri("files/avatar1.png", UriKind.Relative));
        //    Chats.Add(new ChatOne() { Avatar = image, Messages = Messages });

        //    image = new BitmapImage(new Uri("files/avatar2.png", UriKind.Relative));
        //    Chats.Add(new ChatGroup() { Avatar = image, Messages = Messages });

        //    image = new BitmapImage(new Uri("files/avatar3.png", UriKind.Relative));
        //    Chats.Add(new ChatOne() { Avatar = image, Messages = Messages });

        //    //Messages.Add(new GroupMessage(new TextMessage("Hello !!!!"), "User2"));
        //    //Messages.Add(new GroupMessage(new FileMessage("files/hall.png"), "User3"));
        //    //Messages.Add(new GroupMessage(new FileMessage("files/2.pptx"), "Eldar"));
        //    //Messages.Add(new GroupMessage(new MediaMessage("files/control.mp3"), "Emil"));
        //}
    }
}
