using System;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using Newtonsoft.Json;
using WalletConnectSharp.Core.Events;
using WalletConnectSharp.Core.Events.Request;
using WalletConnectSharp.Core.Events.Response;
using WalletConnectSharp.Core.Models;
using WalletConnectSharp.Core.Network;
using Websocket.Client;
using WebsocketClientLite.PCL;

namespace WalletConnectSharp.Desktop.Network
{
    public class PCLWebsocketTransport : ITransport
    {
        private MessageWebsocketRx _client;
        //private var x;
        private EventDelegator _eventDelegator;

        private CancellationTokenSource _checkConnectedCancellationToken = new CancellationTokenSource();

        private IDisposable _dataFrameSubscription;

        public PCLWebsocketTransport(EventDelegator eventDelegator)
        {
            this._eventDelegator = eventDelegator;
        }

        public void Dispose()
        {
            if (_client != null)
                _client.Dispose();
        }

        public bool Connected
        {
            get
            {
                return _client != null && _client.IsConnected;
            }
        }

        public event EventHandler<MessageReceivedEventArgs> MessageReceived;
        public string URL { get; private set; }

        public async Task Open(string url, bool clearSubscriptions = true)
        {
            if (url.StartsWith("https"))
                url = url.Replace("https", "wss");
            else if (url.StartsWith("http"))
                url = url.Replace("http", "ws");

            if (_client != null)
                return;

            this.URL = url;

            _client = new MessageWebsocketRx();
            _client.IgnoreServerCertificateErrors = true;
            if (_dataFrameSubscription != null)
            {
                _dataFrameSubscription.Dispose();
            }
            _dataFrameSubscription = _client.WebsocketConnectWithStatusObservable(new Uri(this.URL)).Where(x => x.state == ConnectionStatus.DataframeReceived).Select(x => x).Subscribe(OnMessageReceived);

            await checkConnected(_checkConnectedCancellationToken);
        }

        private async Task checkConnected(CancellationTokenSource _checkConnectedCancellationToken)
        {
            while (!_checkConnectedCancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100);

                if (Connected)
                {
                    System.Diagnostics.Debug.Write("--CONNECTED--");
                    _checkConnectedCancellationToken.Cancel();
                }
            }
        }

        private async void OnMessageReceived((IDataframe dataframe, ConnectionStatus state) responseMessage)
        {
            if (responseMessage.state == ConnectionStatus.DataframeReceived)
            {
                var json = responseMessage.dataframe.Message;

                var msg = JsonConvert.DeserializeObject<NetworkMessage>(json);

                await SendMessage(new NetworkMessage()
                {
                    Payload = "",
                    Type = "ack",
                    Silent = true,
                    Topic = msg.Topic
                });


                if (this.MessageReceived != null)
                    MessageReceived(this, new MessageReceivedEventArgs(msg, this));
            }
        }

        public async Task Close()
        {
            if (_dataFrameSubscription != null)
                _dataFrameSubscription.Dispose();
            _client.Dispose();
        }

        public async Task SendMessage(NetworkMessage message)
        {
            var finalJson = JsonConvert.SerializeObject(message);

            await this._client.GetSender().SendText(finalJson);
        }

        public async Task Subscribe(string topic)
        {
            await SendMessage(new NetworkMessage()
            {
                Payload = "",
                Type = "sub",
                Silent = true,
                Topic = topic
            });
        }

        public async Task Subscribe<T>(string topic, EventHandler<JsonRpcResponseEvent<T>> callback) where T : JsonRpcResponse
        {
            await Subscribe(topic);

            _eventDelegator.ListenFor(topic, callback);
        }

        public async Task Subscribe<T>(string topic, EventHandler<JsonRpcRequestEvent<T>> callback) where T : JsonRpcRequest
        {
            await Subscribe(topic);

            _eventDelegator.ListenFor(topic, callback);
        }

        public void ClearSubscriptions()
        {

        }
    }

}
