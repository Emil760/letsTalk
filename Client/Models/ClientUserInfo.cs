﻿using Client.Utility;
using System;
using System.IO;
using System.ServiceModel;
using System.Windows.Media.Imaging;

namespace Client.Models
{
    public class ClientUserInfo : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        private string userName;
        private BitmapImage userImage = null; // Аватарка

        public ClientUserInfo()
        {
            UserImage = null;
        }

        public ClientUserInfo(Guid unique_id, int sqlId, ChatService.ChatClient chatClient, string userName)
        {
            ConnectionId = unique_id;
            SqlId = sqlId;
            ChatClient = chatClient;
            UserName = userName;
        }

        public ChatService.ChatClient ChatClient { private set; get; } // Сеанс

        public int SqlId { private set; get; } // Id в БД

        public Guid ConnectionId { private set; get; } // Сеансовый Id

        public string UserName { get => userName; set => Set(ref userName, value); } // Никнейм (Логин == Никнейм)

        public BitmapImage UserImage { get => userImage; set => Set(ref userImage, value); }

        public void DownloadAvatarAsync()
        {
            ChatService.DownloadRequest request = new ChatService.DownloadRequest(SqlId);
            var fileClient = new ChatService.FileClient();
            Stream stream = null;
            long lenght;
            try
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    fileClient.AvatarDownload(SqlId, out lenght, out stream);
                    MemoryStream memoryStream = FileHelper.ReadFileByPart(stream);

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var bitmapImage = new BitmapImage();
                        bitmapImage.BeginInit();
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.StreamSource = memoryStream;
                        bitmapImage.EndInit();

                        UserImage = bitmapImage;
                    });
                    memoryStream.Close();
                    memoryStream.Dispose();
                    stream.Close();
                    stream.Dispose();
                });

            }
            catch (FaultException<ChatService.ConnectionExceptionFault> ex)
            {
                throw ex;
            }
        }

        public void Set<T>(ref T prop, T value, [System.Runtime.CompilerServices.CallerMemberName] string prop_name = "")
        {
            prop = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(prop_name));
        }
    }

}
