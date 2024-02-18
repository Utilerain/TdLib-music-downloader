using System.ComponentModel.Design;
using TdLib;
using TdLib.Bindings;
using TdApi = TdLib.TdApi;

internal static class TdLib_MusicDownloader
{
    private const int ApiId = 94575; //you can set custom api id
    private const string ApiHash = "a3406de8d171bb422bb6ddf3bbd800e2"; //you can set custom api hash
    private static string PhoneNumber = "";
    private const string ApplicationVersion = "1.0.0";

    private static TdApi.Update.UpdateFile _file;
    private static TdClient _client;
    private static readonly ManualResetEventSlim ReadyToAuthenticate = new();
    

    private static bool _authNeeded;
    private static bool _passwordNeeded;
    private static bool _exit = false;

    private static async Task Main()
    {
        // Creating Telegram client and setting minimal verbosity to Fatal since we don't need a lot of logs :)
        _client = new TdClient();
        _client.Bindings.SetLogVerbosityLevel(TdLogLevel.Fatal);

        // Subscribing to all events
        _client.UpdateReceived += async (_, update) => { await ProcessUpdates(update); };

        // Waiting until we get enough events to be in 'authentication ready' state
        ReadyToAuthenticate.Wait();

        // We may not need to authenticate since TdLib persists session in 'td.binlog' file.
        // See 'TdlibParameters' class for more information, or:
        // https://core.telegram.org/tdlib/docs/classtd_1_1td__api_1_1tdlib_parameters.html
        if (_authNeeded)
        {
            // Interactively handling authentication
            await HandleAuthentication();
        }

        var currentUser = await _client.GetMeAsync();
        var fullUserName = $"{currentUser.FirstName} {currentUser.LastName}".Trim();
        Console.WriteLine($"Successfully logged in as [{currentUser.Id}] / [@{currentUser.Usernames?.ActiveUsernames[0]}] / [{fullUserName}]");
        while (!_exit)
        {
            try
            {
                Console.WriteLine();
                Console.WriteLine("getme - getme\ngetchats <limit> - get chats\ngetchat <id> get chat info\ndownloadmusicfromchat <id> <path> - load music from chat to selected path\nlogout - logout from account\nexit - exit from app");
                Console.Write("$ ");
                string wait = Console.ReadLine();
                string[] temp = wait.Split(' ', 2);

                string command = temp[0];
                string[] args;

                if (temp.Length > 1)
                {
                    args = temp[1].Split(' ');
                }
                else
                {
                    args = [" "];
                }
                await HandleCommands(command, args);
            }
            catch (Exception ex) 
            {
                Console.WriteLine("Oops! An error!");
                Console.WriteLine(ex.ToString());
            }
        }
    }

    private static async Task HandleAuthentication()
    {
        // Setting phone number
        Console.Write("Insert phone number: ");
        PhoneNumber = Console.ReadLine();

        await _client.ExecuteAsync(new TdApi.SetAuthenticationPhoneNumber
        {
            PhoneNumber = PhoneNumber
        });

        // Telegram servers will send code to us
        Console.Write("Insert the login code: ");
        var code = Console.ReadLine();

        await _client.ExecuteAsync(new TdApi.CheckAuthenticationCode
        {
            Code = code
        });

        if (!_passwordNeeded) { return; }

        // 2FA may be enabled. Cloud password is required in that case.
        Console.Write("Insert the password: ");
        var password = Console.ReadLine();

        await _client.ExecuteAsync(new TdApi.CheckAuthenticationPassword
        {
            Password = password
        });
    }

    private static async Task ProcessUpdates(TdApi.Update update)
    {
        // Since Tdlib was made to be used in GUI application we need to struggle a bit and catch required events to determine our state.
        // Below you can find example of simple authentication handling.
        // Please note that AuthorizationStateWaitOtherDeviceConfirmation is not implemented.

        switch (update)
        {
            case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitTdlibParameters }:
                // TdLib creates database in the current directory.
                // so create separate directory and switch to that dir.
                var filesLocation = Path.Combine(AppContext.BaseDirectory, "db");
                await _client.ExecuteAsync(new TdApi.SetTdlibParameters
                {
                    ApiId = ApiId,
                    ApiHash = ApiHash,
                    DeviceModel = "PC",
                    SystemLanguageCode = "en",
                    ApplicationVersion = ApplicationVersion,
                    DatabaseDirectory = filesLocation,
                    FilesDirectory = filesLocation,
                    // More parameters available!
                });
                break;

            case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitPhoneNumber }:
            case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitCode }:
                _authNeeded = true;
                ReadyToAuthenticate.Set();
                break;

            case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitPassword }:
                _authNeeded = true;
                _passwordNeeded = true;
                ReadyToAuthenticate.Set();
                break;

            case TdApi.Update.UpdateUser:
                ReadyToAuthenticate.Set();
                break;

            case TdApi.Update.UpdateConnectionState { State: TdApi.ConnectionState.ConnectionStateReady }:
                // You may trigger additional event on connection state change
                break;

            case TdApi.Update.UpdateFile:
                _file = (TdApi.Update.UpdateFile)update;
                break;

            default:
                // ReSharper disable once EmptyStatement
                ;
                // Add a breakpoint here to see other events
                break;
        }
    }

    private static async IAsyncEnumerable<TdApi.Chat> GetChats(int limit)
    {
        var chats = await _client.ExecuteAsync(new TdApi.GetChats {
            Limit = limit
        });

        //get chat info in chats list
        foreach (var chatID in chats.ChatIds)
        {
            var chat = await _client.ExecuteAsync<TdApi.Chat>(new TdApi.GetChat { ChatId = chatID });

            if (chat.Type is TdApi.ChatType.ChatTypeSupergroup or TdApi.ChatType.ChatTypeBasicGroup or TdApi.ChatType.ChatTypePrivate)
            {
                yield return chat;
            }
        }
    }

    private static async Task HandleCommands(string command, string[] args)
    {
        switch (command)
        {
            case "getme":
                var user = await _client.ExecuteAsync(new TdApi.GetMe());
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(user));
                break;

            case "getchats":
                var limit = Convert.ToInt16(args[0]);
                var channels = GetChats(limit);

                Console.WriteLine($"Top {limit} chats:");

                await foreach (var channel in channels)
                {
                    Console.WriteLine($"[{channel.Id}] -> [{channel.Title}]");
                }
                break;

            case "getchat":
                var chat = await GetChat(Convert.ToInt64(args[0]));
                Console.WriteLine($"[{chat.Id}] -> [{chat.Title}]:\n");
                var messages = await _client.GetChatHistoryAsync(chat.Id, fromMessageId: chat.LastMessage.Id, limit: 1000);

                foreach (var message in messages.Messages_)
                {
                    if (message.Content is TdApi.MessageContent.MessageAudio)
                    {
                        Console.WriteLine("Audio: " + ((TdApi.MessageContent.MessageAudio)message.Content).Audio.FileName);
                    }
                    else if (message.Content is TdApi.MessageContent.MessageText)
                    {
                        var user_id = ((TdApi.MessageSender.MessageSenderUser)message.SenderId).UserId;
                        var usr = _client.GetUserAsync(user_id).Result;
                        Console.WriteLine($"Message from {usr.FirstName}: " + ((TdApi.MessageContent.MessageText)message.Content).Text.Text);
                    }
                }
                break;

            case "downloadmusicfromchat":
                await DownloadMusicFromChat(Convert.ToInt64(args[0]));
                break;

            case "logout":
                await _client.LogOutAsync();
                _exit = true;
                break;

            case "exit":
                _exit = true;
                break;

            default: break;
        }
    }

    private static async Task<TdApi.Chat> GetChat(long chat_id)
    {
        var chat = await _client.ExecuteAsync<TdApi.Chat>(new TdApi.GetChat 
        { 
            ChatId = chat_id,
        });

        if (chat.Type is TdApi.ChatType.ChatTypeSupergroup or TdApi.ChatType.ChatTypeBasicGroup or TdApi.ChatType.ChatTypePrivate)
        {
            return chat;
        }

        return null;
    }

    private static async Task DownloadMusicFromChat(long chat_id, string path = "./downloads")
    {
        var chat = await GetChat(chat_id);

        List<TdApi.MessageContent.MessageAudio> audio_message_contents = new();

        TdApi.Message last_loaded_message = chat.LastMessage;

        long last_message_id = 0;

        while (last_loaded_message.Id != last_message_id)
        {
            var messages = await _client.GetChatHistoryAsync(chat_id, fromMessageId: last_loaded_message.Id, limit: 100);
            last_message_id = last_loaded_message.Id; //for unlimited loading message

            foreach (var message in messages.Messages_)
            {
                if (message.Content is TdApi.MessageContent.MessageAudio) //checks if message's content contains audio
                {
                    var content = (TdApi.MessageContent.MessageAudio)message.Content;
                    audio_message_contents.Add(content); //add to list for download
                    Console.WriteLine("Add to list: " + content.Audio.FileName);
                    last_loaded_message = message;
                }
            }
        }

        Console.WriteLine($"Total count: {audio_message_contents.Count}");
        Console.WriteLine("Downloading files...");

        foreach (var audio_content in audio_message_contents)
        {
            try
            {
                Console.WriteLine("Downloading: " + audio_content.Audio.FileName);
                await _client.DownloadFileAsync(audio_content.Audio.Audio_.Id, priority: 16);
                while (!_file.File.Local.IsDownloadingCompleted) ; //solution for waiting a file download (may be used in future)

                Console.WriteLine($"Succesfully downloaded \"{audio_content.Audio.Title}\" at \"{_file.File.Local.Path}\"");

            }
            catch (Exception ex)
            {
                Console.WriteLine("Can't download file: " + audio_content.Audio.FileName);
                Console.WriteLine(ex.Message);
            }
        }
    }
}
