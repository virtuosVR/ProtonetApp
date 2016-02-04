﻿using GalaSoft.MvvmLight;
using Newtonsoft.Json;
using ProtoApp.Objects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace ProtoApp
{
    public class ProtonetClient : ObservableObject, IProtonetClient
    {
        const string API = "api/v1/";
        const string TOKEN = "tokens/";
        const string ME = "me/";

        const string USER = "users/";
        const string MEEPS = "meeps/";

        const string TOKEN_NAME = "X-Protonet-Token";


        public bool IsAuthentificated => Token != null;


        private Me user;
        public Me User
        {
            get { return user; }
            set
            {
                if (Set(nameof(User), ref user, value))
                    RaisePropertyChanged(nameof(IsAuthentificated));
            }
        }
        


        public string Token { get; private set; }

        private CancellationTokenSource cts = new CancellationTokenSource();
        public void CancelAllRequests()
        {
            cts.Cancel();
            cts.Dispose();
            cts = new CancellationTokenSource();
        }

        public event EventHandler AuthentificationComplete;
        public void OnAuthentificationComplete() => AuthentificationComplete?.Invoke(this, EventArgs.Empty);

        public event EventHandler AuthentificationFailed;
        public void OnAuthentificationFailed() => AuthentificationFailed?.Invoke(this, EventArgs.Empty);

        public event EventHandler LoggedOut;
        public void OnLoggedOut() => LoggedOut?.Invoke(this, EventArgs.Empty);




        private HttpClient client = new HttpClient();

        public ProtonetClient(string url)
        {
            if (!url.EndsWith(API))
            {
                if (!url.EndsWith("/"))
                    url += "/";
                url += API;
            }

            client.BaseAddress = new Uri(url);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        }
      


        
        public async Task<bool> Authentificate (string tokenString)
        {
            ClearLoginData();

            Token = tokenString;

            await CreateAuthentificatedClientSettings();
            

            return true;
        }

        

        public async Task<bool> Authentificate(string user, string password)
        {
            ClearLoginData();

            var tokenResp = await GetToken(user, password);

            if (string.IsNullOrWhiteSpace(tokenResp?.Token))
                return false;

            Token = tokenResp.Token;

            await CreateAuthentificatedClientSettings();

            return true;
        }

        private async Task CreateAuthentificatedClientSettings()
        {
            client.DefaultRequestHeaders.Add("X-Protonet-Token", Token);
            User = await GetMe();

            OnAuthentificationComplete();
        }


        private void ClearLoginData()
        {
            User = null;
            Token = null;
            client.DefaultRequestHeaders.Remove(TOKEN_NAME);
        }

        public void Logout()
        {
            ClearLoginData();
            OnLoggedOut();
        }











        
        public async Task<Me> GetMe ()
        {
            var response = await GetAndReadResponseObject<MeContainer>(ME);
            
            return response?.Me;
        }

        public async Task<TokenResponse> GetToken(string user, string password)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, TOKEN);
            
            var cred = $"{user}:{password}";
            var crypt = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(cred));
            request.Headers.Add("Authorization", crypt);

            return await SendAndReadResponseObject<TokenResponse>(request);
        }

        public async Task<List<PrivateChat>> GetChats()
        {
            var responseObject = await GetAndReadResponseObject<PrivateChatsContainer>(User.PrivateChatsUrl);
            return responseObject?.Chats;
        } 

        public async Task<PrivateChat> GetChat(string url)
        {
            var responseObject = await GetAndReadResponseObject<PrivateChatContainer>(url);
            return responseObject?.Chat;
        }

        public async Task<List<Meep>> GetChatMeeps(string url)
        {
            var responseObject = await GetAndReadResponseObject<MeepsContainer>(url);
            return responseObject?.Meeps;
        } 

        public async Task<Meep> CreateMeep (string url, NewMeep meep)
        {
            var json = JsonConvert.SerializeObject(meep);
            var content = new StringContent(json);
            content.Headers.ContentType.MediaType = "application/json";
            var responseObject = await PostAndReadResponseObject<MeepContainer>(url, content);
            return responseObject.Meep;
        }

        public async Task<Meep> CreateFileMeep (string url, Stream file)
        {
            var content = new StreamContent(file);
            //content.Headers.ContentType.MediaType = "application/octet-stream";
            var responseObject = await PostAndReadResponseObject<MeepContainer>(url, content);

            return responseObject.Meep;
        }


        

        public async Task<Stream> GetDownloadStream(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var resp = await HandleUnauthorizedAccess(client.SendAsync(request));
            return await resp?.Content?.ReadAsStreamAsync();
            
        }



        private async Task<Stream> SendAndReadResponseStream(string url)
        {
            var response = await HandleUnauthorizedAccess(new Task<HttpResponseMessage> (async () =>
           {
               var resp = await client.GetAsync(url);
               CheckResponseStatus(resp);
               return resp;
           }));
            

            return await response.Content.ReadAsStreamAsync();
        }

        
        private async Task<string> SendAndReadResponseContent(HttpRequestMessage message)
        {
            var resp = await client.SendAsync(message);
            CheckResponseStatus(resp);

            return await resp.Content.ReadAsStringAsync();
        }
        private async Task<T> SendAndReadResponseObject<T>(HttpRequestMessage message)
        {
            var json = await HandleUnauthorizedAccess(SendAndReadResponseContent(message));

            return JsonConvert.DeserializeObject<T>(json);
        }

 
        private async Task<string> GetAndReadResponseContent(string url)
        {
            var resp = await client.GetAsync(url);

            CheckResponseStatus(resp);

            return await resp.Content.ReadAsStringAsync();

        }
        private async Task<T> GetAndReadResponseObject<T>(string url)
        {
            var json = await HandleUnauthorizedAccess(GetAndReadResponseContent(url));

            return JsonConvert.DeserializeObject<T>(json);
        }
        

        private async Task<string> PostAndReadResponseContent(string url, HttpContent content)
        {
            var resp = await client.PostAsync(url, content);

            CheckResponseStatus(resp);

            return await resp.Content.ReadAsStringAsync();
        }
        private async Task<T> PostAndReadResponseObject<T>(string url, HttpContent content )
        {
            var json = await HandleUnauthorizedAccess(PostAndReadResponseContent(url, content));
            return JsonConvert.DeserializeObject<T>(json);
        }

        private async Task<T> HandleUnauthorizedAccess<T>(Task<T> task)
        {
            try
            {
                return await task;
            }
            catch (UnauthorizedAccessException)
            {
                ClearLoginData();
                OnAuthentificationFailed();
                return await Task.FromResult<T>(default(T));
            }
        }

        private void CheckResponseStatus(HttpResponseMessage response)
        {
            switch (response.StatusCode)
            {
                case System.Net.HttpStatusCode.Unauthorized:
                    throw new UnauthorizedAccessException();
                //....
            }
            response.EnsureSuccessStatusCode();
        }


    }
}
