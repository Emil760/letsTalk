﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.IO;
using System.ServiceModel;
using System.Transactions;

namespace letsTalk
{
    // Реализация логики сервера
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single, // Single -> Объект ChatService является синглтоном
                    IncludeExceptionDetailInFaults = true, // Faults == Exceptions
                    ConcurrencyMode = ConcurrencyMode.Multiple)] // Multiple => Сервер должен держать нескольких пользователей себе (Под каждого юзера свой поток)
   public class ChatService : IChatService, IFileService
   {
        private static string connection_string = @"Server=(local);Database=MessengerDB;Integrated Security=true;";
        // Сервер хранит подключенных пользователей в Dictionary, задавая каждому уникальный ID-подключения (GUID)
        private Dictionary<Guid, ConnectedServerUser> connectedUsers = new Dictionary<Guid, ConnectedServerUser>();
        // Авторизация на сервер, метод ищет пользователя в БД
        public ServerUserInfo Authorization(AuthenticationUserInfo authenticationUserInfo)
        {
            ServerUserInfo serverUserInfo = null;
            try
            {
                using (SqlConnection sqlConnection = new SqlConnection(connection_string))
                {
                    sqlConnection.Open();
                    SqlCommand sqlCommand = new SqlCommand(@"SELECT* FROM Users WHERE [Login] = @Login
                                                                           AND [Password] = @Password", sqlConnection);

                    sqlCommand.Parameters.Add("@Login", SqlDbType.NVarChar).Value = authenticationUserInfo.Login;
                    sqlCommand.Parameters.Add("@Password", SqlDbType.NVarChar).Value = authenticationUserInfo.Password;

                    using (SqlDataReader reader = sqlCommand.ExecuteReader())
                    {
                        if (!reader.HasRows)
                        {
                            AuthorizationExceptionFault authorizationExceptionFault = new AuthorizationExceptionFault();
                            throw new FaultException<AuthorizationExceptionFault>(authorizationExceptionFault, authorizationExceptionFault.Message);
                        }

                        while (reader.Read())
                        {

                            serverUserInfo = new ServerUserInfo()
                            {
                                SqlId = int.Parse(reader["Id"].ToString()),
                                Name = reader["Name"].ToString()
                            };

                        }

                    }
                }
            }
            catch (SqlException ex)
            {
                Console.WriteLine(ex.Message);
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return serverUserInfo;
        }

        // Регистрация пользователя, добавление нового пользователя в БД
        public int Registration(ServerUserInfo serverUserInfo)
        {
            SqlTransaction sqlTransaction = null;
            SqlConnection sqlConnection = null;

            int UserId = -1;

            try
            {
                sqlConnection = new SqlConnection(connection_string);

                sqlConnection.Open();

                sqlTransaction = sqlConnection.BeginTransaction();

                SqlCommand sqlCommandLogin = new SqlCommand(@"SELECT [Login] FROM Users WHERE [Login] = @Login", sqlConnection);
                sqlCommandLogin.Transaction = sqlTransaction;
                sqlCommandLogin.CommandType = CommandType.Text;
                sqlCommandLogin.Parameters.AddWithValue("@Login", serverUserInfo.Login);

                using (SqlDataReader reader = sqlCommandLogin.ExecuteReader())
                {
                    if (reader.HasRows)
                    {
                        LoginExceptionFault loginExceptionFault = new LoginExceptionFault();
                        throw new FaultException<LoginExceptionFault>(loginExceptionFault, loginExceptionFault.Message);
                    }
                }

                SqlCommand sqlCommandName = new SqlCommand(@"SELECT [Name] FROM Users WHERE [Name] = @Name", sqlConnection);
                sqlCommandName.Transaction = sqlTransaction;
                sqlCommandName.CommandType = CommandType.Text;
                sqlCommandName.Parameters.AddWithValue("@Name", serverUserInfo.Name);

                using (SqlDataReader reader = sqlCommandName.ExecuteReader())
                {
                    if (reader.HasRows)
                    {
                        NicknameExceptionFault nicknameExceptionFault = new NicknameExceptionFault();
                        throw new FaultException<NicknameExceptionFault>(nicknameExceptionFault, nicknameExceptionFault.Message);
                    }
                }

                SqlCommand sqlCommandInsertUser = new SqlCommand(@"INSERT INTO Users(Name, Login, Password) VALUES(@Name, @Login, @Password); SELECT SCOPE_IDENTITY()", sqlConnection);
                sqlCommandInsertUser.Transaction = sqlTransaction;
                sqlCommandInsertUser.CommandType = CommandType.Text;
                sqlCommandInsertUser.Parameters.AddWithValue("@Name", serverUserInfo.Name);
                sqlCommandInsertUser.Parameters.AddWithValue("@Login", serverUserInfo.Login);
                sqlCommandInsertUser.Parameters.AddWithValue("@Password", serverUserInfo.Password);

                UserId = int.Parse(sqlCommandInsertUser.ExecuteScalar().ToString());

                sqlTransaction.Commit();
            }
            catch (SqlException sqlEx)
            {
                sqlTransaction.Rollback();
                Console.WriteLine("Rollback sql");
                Console.WriteLine(sqlEx.Message);
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                sqlConnection.Close();
            }

            Console.WriteLine("User with nickname: " + serverUserInfo.Name + " is registered");
            return UserId;
        }

        // Сервер отправляет аватарку зарегистированного пользователя в БД (Метод ищет аватарку пользователя, посредством связей в БД.
        // После того, как аватарка была найдена в БД, у нас открывается поток под эту картинку для того чтобы клиентская часть сегментами подгрузила её)
        public DownloadFileInfo AvatarDownload(DownloadRequest request)
        {
            DownloadFileInfo downloadFileInfo = new DownloadFileInfo();

            try
            {
                using (SqlConnection sqlConnection = new SqlConnection(connection_string))
                {
                    sqlConnection.Open();

                    SqlCommand sqlCommandFindAvatar = new SqlCommand(@"SELECT stream_id FROM Users WHERE Users.Id = @Id", sqlConnection);
                    sqlCommandFindAvatar.CommandType = CommandType.Text;
                    sqlCommandFindAvatar.Parameters.Add("@Id", SqlDbType.Int).Value = request.Requested_UserSqlId;

                    var stream_id = sqlCommandFindAvatar.ExecuteScalar();

                    if (stream_id.GetType() == typeof(DBNull))
                    {
                        return downloadFileInfo;
                    }

                    SqlCommand sqlCommandTakeAvatar = new SqlCommand($@"SELECT* FROM GetAvatar(@stream_id)", sqlConnection);
                    sqlCommandTakeAvatar.Parameters.Add("@stream_id", SqlDbType.UniqueIdentifier).Value = (Guid)stream_id;

                    using (SqlDataReader reader = sqlCommandTakeAvatar.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var stream = new FileStream(reader[0].ToString(), FileMode.Open, FileAccess.Read);

                            downloadFileInfo.FileExtension = reader[1].ToString();
                            downloadFileInfo.Length = long.Parse(reader[2].ToString());
                            downloadFileInfo.FileStream = stream;
                        }
                    }
                }
            }
            catch (SqlException sqlEx) { Console.WriteLine("SqlException: " + sqlEx.Message); }
            catch (IOException ioEx) { Console.WriteLine("IOException: " + ioEx.Message); }
            catch (Exception ex) { Console.WriteLine("Exception: " + ex.Message); }

            return downloadFileInfo;
        }

        // Здесь сервер заносит картинку в файловую таблицу, процесс обратный методу AvatarDownload
        public void AvatarUpload(UploadFileInfo uploadResponse)
        {

            try
            {
                using (TransactionScope trScope = new TransactionScope())
                {
                    using (SqlConnection sqlConnection = new SqlConnection(connection_string))
                    {
                        sqlConnection.Open();

                        SqlCommand sqlCommandAddAvatar = new SqlCommand($@" INSERT INTO DataFT(file_stream, name, path_locator)
                                                                        OUTPUT INSERTED.stream_id, GET_FILESTREAM_TRANSACTION_CONTEXT(),
                                                                        INSERTED.file_stream.PathName()
                                                                        VALUES(CAST('' as varbinary(MAX)), @name, dbo.GetPathLocatorForChild('Avatars'))", sqlConnection);

                        sqlCommandAddAvatar.CommandType = CommandType.Text;

                        sqlCommandAddAvatar.Parameters.Add("@name", SqlDbType.NVarChar).Value = "AVATAR" + uploadResponse.Responsed_UserSqlId + $".{uploadResponse.FileExtension}";

                        Guid stream_id;
                        byte[] transaction_context;
                        string full_path;

                        using (SqlDataReader sqlDataReader = sqlCommandAddAvatar.ExecuteReader())
                        {
                            sqlDataReader.Read();
                            stream_id = sqlDataReader.GetSqlGuid(0).Value;
                            transaction_context = sqlDataReader.GetSqlBinary(1).Value;
                            full_path = sqlDataReader.GetSqlString(2).Value;
                        }

                        const int bufferSize = 2048;

                        using (SqlFileStream sqlFileStream = new SqlFileStream(full_path, transaction_context, FileAccess.Write))
                        {
                            int bytesRead = 0;
                            var buffer = new byte[bufferSize];

                            while ((bytesRead = uploadResponse.FileStream.Read(buffer, 0, bufferSize)) > 0)
                            {
                                sqlFileStream.Write(buffer, 0, bytesRead);
                                sqlFileStream.Flush();
                            }

                        }

                        SqlCommand UpdateUserAvatar = new SqlCommand($@" UPDATE Users SET Users.stream_id = @stream_id WHERE Users.Id = @user_id", sqlConnection);

                        UpdateUserAvatar.CommandType = CommandType.Text;
                        UpdateUserAvatar.Parameters.Add("@stream_id", SqlDbType.UniqueIdentifier).Value = stream_id;
                        UpdateUserAvatar.Parameters.Add("@user_id", SqlDbType.Int).Value = uploadResponse.Responsed_UserSqlId;

                        UpdateUserAvatar.ExecuteNonQuery();

                        trScope.Complete();

                        Console.WriteLine($"avatar for user {uploadResponse.Responsed_UserSqlId} is added");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                StreamExceptionFault streamExceptionFault = new StreamExceptionFault();

                throw new FaultException<StreamExceptionFault>(streamExceptionFault, streamExceptionFault.Message);
            }
        }

        // Захват пользователя на сервере, и выдача ему уникального ID (сеансовый ID, не путать с SQL)
        public Guid Connect(int sqlId)
        {
            Guid uniqueId = Guid.NewGuid();

            ConnectedServerUser serverUser = new ConnectedServerUser()
            {
                SqlId = sqlId,
                OperationContext = OperationContext.Current
            };

            connectedUsers.Add(uniqueId, serverUser);
            Console.WriteLine($"User: {uniqueId} is Connected");

            return uniqueId;
        }

        // Процесс обратный Connect
        public void Disconnect(Guid uniqueId)
        {
            connectedUsers.Remove(uniqueId);
            Console.WriteLine($"User: {uniqueId} is Disconnected");
        }

        public Dictionary<int, string> GetUsers(int count, int offset, int callerId)
        {
            Dictionary<int, string> users = new Dictionary<int, string>();
            try
            {
                using (SqlConnection sqlConnection = new SqlConnection(connection_string))
                {
                    sqlConnection.Open();

                    SqlCommand sqlCommandShowMoreUsers = new SqlCommand(@"SELECT Id, Name FROM ShowMoreUsers(@count, @offset, @callerId)", sqlConnection);
                    sqlCommandShowMoreUsers.CommandType = CommandType.Text;

                    sqlCommandShowMoreUsers.Parameters.Add("@count", SqlDbType.SmallInt).Value = count;
                    sqlCommandShowMoreUsers.Parameters.Add("@offset", SqlDbType.SmallInt).Value = offset;
                    sqlCommandShowMoreUsers.Parameters.Add("@callerId", SqlDbType.SmallInt).Value = callerId;

                    using (SqlDataReader sqlDataReader = sqlCommandShowMoreUsers.ExecuteReader())
                    {
                        if (sqlDataReader.HasRows)
                        {
                            while (sqlDataReader.Read())
                            {
                                users.Add(sqlDataReader.GetSqlInt32(0).Value, sqlDataReader.GetSqlString(1).Value);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
            return users;
        }

        public void CreateChatroom(string chatName, List<int> users)
        {
            Console.WriteLine("chat: " + chatName);
            foreach(var item in users)
                Console.WriteLine("User:" + item);
        }

        public bool SendMessage(string message)
        {
            throw new NotImplementedException();
        }

    }
}
