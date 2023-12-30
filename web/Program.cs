using Microsoft.Extensions.FileProviders;
using Microsoft.VisualBasic;
using NAudio.MediaFoundation;
using NAudio.Wave;
using System;
using System.ComponentModel;
using System.IO;
using System.Net.WebSockets;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;

namespace web
{
    public class Program
    { 
        private static readonly List<WebSocket> _scoket = new List<WebSocket>();
        private static int count = 0;
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.WebHost.UseUrls(new[] { "http://*:5054" });
            // Add services to the container.
            builder.Services.AddAuthorization();

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();


            //builder.WebHost.UseContentRoot("files");
            var app = builder.Build();
            
            app.UseStaticFiles(new StaticFileOptions() {
                FileProvider = new PhysicalFileProvider(
                    Path.Combine(builder.Environment.ContentRootPath, "files")),
                RequestPath = "/files"
            });

            var webSocketOptions = new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(5)
            };
            app.UseWebSockets(webSocketOptions);

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseAuthorization();

            var capture = new WasapiLoopbackCapture();
            capture.WaveFormat = new WaveFormat(48000, 32, 2);

            capture.DataAvailable += (s, e) =>
            {
                if (e.BytesRecorded > 0)
                {
                    count++;
                    foreach (var item in _scoket)
                    {
                        try
                        {
                            item.SendAsync(new ArraySegment<byte>(e.Buffer, 0, e.BytesRecorded), WebSocketMessageType.Binary, count % 4 == 0, CancellationToken.None);
                        }
                        catch (Exception ex)
                        { 
                            Console.WriteLine(ex); 
                        }
                    }
                }
            };
            capture.StartRecording();

            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/ws")
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        //if (capture.CaptureState != NAudio.CoreAudioApi.CaptureState.Starting)
                        //{
                        //    capture.StartRecording();
                        //}
                        //else
                        //{
                        //    capture.StopRecording();
                        //    capture.StartRecording();
                        //}
                        await Echo(webSocket);
                    }
                    else
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    }
                }
                else
                {
                    await next(context);
                }

            });

            app.MapGet("/Index", (HttpContext httpContext) =>
            {
                httpContext.Response.ContentType = "text/html; charset=utf-8";

                var filePath = Path.Combine(System.Environment.CurrentDirectory, "./files/pcm.html");
                return File.ReadAllText(filePath);
                //return "<html><body><audio buffered=\"0\" preload=\"none\" controls src=\"/Voice\"></body></html>";
            }).WithName("Hello");

            app.Run();
        }

        private static async Task Echo(WebSocket webSocket)
        {
            _scoket.Add(webSocket);
            var buffer = new byte[1024 * 4];
            var receiveResult = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer), CancellationToken.None);

            while (!receiveResult.CloseStatus.HasValue)
            {
                await Task.Delay(1000);
            }

            await webSocket.CloseAsync(
                receiveResult.CloseStatus.Value,
                receiveResult.CloseStatusDescription,
                CancellationToken.None);
            _scoket.Remove(webSocket);
        }
    }
}