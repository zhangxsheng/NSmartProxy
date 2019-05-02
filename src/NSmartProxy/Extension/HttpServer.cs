﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NSmartProxy.Database;
using NSmartProxy.Interfaces;

namespace NSmartProxy.Extension
{
    partial class HttpServer
    {
        #region HTTPServer

        public INSmartLogger Logger;
        public IDbOperator Dbop;

        public HttpServer(INSmartLogger logger, IDbOperator dbop)
        {
            Logger = logger;
            Dbop = dbop;
            //第一次加载所有mime类型
            PopulateMappings();

        }

        public async Task StartHttpService(CancellationTokenSource ctsHttp, int WebManagementPort)
        {
            try
            {
                HttpListener listener = new HttpListener();
                listener.Prefixes.Add($"http://+:{WebManagementPort}/");
                //TcpListener listenerConfigService = new TcpListener(IPAddress.Any, WebManagementPort);
                Logger.Debug("Listening HTTP request on port " + WebManagementPort.ToString() + "...");
                await AcceptHttpRequest(listener, ctsHttp);
            }
            catch (HttpListenerException ex)
            {
                Logger.Debug("Please run this program in administrator mode." + ex);
                Server.Logger.Error(ex.ToString(), ex);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex);
                Server.Logger.Error(ex.ToString(), ex);
            }
        }

        private async Task AcceptHttpRequest(HttpListener httpService, CancellationTokenSource ctsHttp)
        {
            httpService.Start();
            while (true)
            {
                var client = await httpService.GetContextAsync();
                ProcessHttpRequestAsync(client);
            }
        }

        private async Task ProcessHttpRequestAsync(HttpListenerContext context)
        {
            string baseFilePath = "./Extension/HttpServerStaticFiles/";
            var request = context.Request;
            var response = context.Response;


            try
            {
                //TODO ***通过request来的值进行接口调用
                string unit = request.RawUrl.Replace("//", "");
                int idx1 = unit.LastIndexOf("#");
                if (idx1 > 0) unit = unit.Substring(0, idx1);
                int idx2 = unit.LastIndexOf("?");
                if (idx2 > 0) unit = unit.Substring(0, idx2);
                int idx3 = unit.LastIndexOf(".");

                //TODO 通过后缀获取不同的文件，若无后缀，则调用接口
                if (idx3 > 0)
                {

                    if (!File.Exists(baseFilePath + unit))
                    {
                        Server.Logger.Debug($"未找到文件{baseFilePath + unit}");
                        return;

                    }
                    //mime类型
                    ProcessMIME(response, unit.Substring(idx3));
                    using (FileStream fs = new FileStream(baseFilePath + unit, FileMode.Open))
                    {
                        await fs.CopyToAsync(response.OutputStream);
                    }
                }
                else
                {
                    unit = unit.Replace("/", "");
                    response.ContentEncoding = Encoding.UTF8;
                    response.ContentType = "application/json";
                    //TODO XXXXXX 调用接口 接下来要用分布类隔离并且用API特性限定安全
                    var json = this.GetType().GetMethod(unit).Invoke(this, null);
                    //getJson
                    //var json = GetClientsInfoJson();
                    await response.OutputStream.WriteAsync(HtmlUtil.GetContent(json.ToString()));
                    //await response.OutputStream.WriteAsync(HtmlUtil.GetContent(request.RawUrl));
                    // response.OutputStream.Close();

                }
                //suffix = unit.Substring(unit.LastIndexOf(".")+1,)

            }
            catch (Exception e)
            {
                Logger.Error(e.Message, e);
                throw;
            }
            finally
            {
                response.OutputStream.Close();
            }
        }

        private void ProcessMIME(HttpListenerResponse response, string suffix)
        {
            if (suffix == ".html" || suffix == ".js")
            {
                response.ContentEncoding = Encoding.UTF8;
            }

            string val = "";
            if (_mimeMappings.TryGetValue(suffix, out val))
            {
                // found!
                response.ContentType = val;
            }
            else
            {
                response.ContentType = "application/octet-stream";
            }

        }

        public string GetClientsInfoJson()
        {
            var ConnectionManager = ClientConnectionManager.GetInstance();
            StringBuilder json = new StringBuilder("[ ");
            foreach (var app in ConnectionManager.PortAppMap)
            {
                json.Append("{ ");
                json.Append(KV2Json("port", app.Key)).C();
                json.Append(KV2Json("clientId", app.Value.ClientId)).C();
                json.Append(KV2Json("appId", app.Value.AppId)).C();
                json.Append(KV2Json("blocksCount", app.Value.TcpClientBlocks.Count)).C();
                //反向连接
                json.Append(KV2Json("revconns"));
                json.Append("[ ");
                foreach (var reverseClient in app.Value.ReverseClients)
                {
                    json.Append("{ ");
                    if (reverseClient.Connected)
                    {
                        json.Append(KV2Json("lEndPoint", reverseClient.Client.LocalEndPoint.ToString())).C();
                        json.Append(KV2Json("rEndPoint", reverseClient.Client.RemoteEndPoint.ToString()));
                    }

                    //json.Append(KV2Json("p", c)).C();
                    //json.Append(KV2Json("port", ca.Key));
                    json.Append("}");
                    json.C();
                }

                json.D();
                json.Append("]").C();
                ;

                //隧道状态
                json.Append(KV2Json("tunnels"));
                json.Append("[ ");
                foreach (var tunnel in app.Value.Tunnels)
                {
                    json.Append("{ ");
                    if (tunnel.ClientServerClient != null)
                    {
                        Socket sktClient = tunnel.ClientServerClient.Client;
                        if (tunnel.ClientServerClient.Connected)

                            json.Append(KV2Json("clientServerClient", $"{sktClient.LocalEndPoint}-{sktClient.RemoteEndPoint}"))
                                .C();
                    }
                    if (tunnel.ConsumerClient != null)
                    {
                        Socket sktConsumer = tunnel.ConsumerClient.Client;
                        if (tunnel.ConsumerClient.Connected)
                            json.Append(KV2Json("consumerClient", $"{sktConsumer.LocalEndPoint}-{sktConsumer.RemoteEndPoint}"))
                                .C();
                    }

                    json.D();
                    //json.Append(KV2Json("p", c)).C();
                    //json.Append(KV2Json("port", ca.Key));
                    json.Append("}");
                    json.C();
                }

                json.D();
                json.Append("]");
                json.Append("}").C();
            }

            json.D();
            json.Append("]");
            return json.ToString();
        }

        private string KV2Json(string key)
        {
            return "\"" + key + "\":";
        }
        private string KV2Json(string key, object value)
        {
            return "\"" + key + "\":\"" + value.ToString() + "\"";
        }

        public List<string> GetUsers()
        {
            //using (var dbop = Dbop.Open())
            //{
                return Dbop.Select(0, 10);
            //}
        }

        #endregion
        //TODO XXXX
        //API login

        //API Users
        //REST
        //AddUser
        //RemoveUser
        //

        //NoApi Auth
    }
}
