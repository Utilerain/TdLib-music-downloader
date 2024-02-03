using System;
using TdLib;
using TdLib.Bindings;
using TdApi = TdLib.TdApi;

namespace TdLib_music_class
{
    public class Client
    {
        private TdClient _client;

        public Client(TdLib.TdApi.SetTdlibParameters parameters) 
        {
            _client = new TdClient();
            _client.Execute(parameters);
        }

    }
}
